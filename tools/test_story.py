"""Story acceptance on Testing/Server port 6975 with synthetic characters only."""
import json
import secrets
from pathlib import Path

from test_integration import PROJECT, SERVER, request, check, checks


def main():
    fixture = json.loads((SERVER / "user/mods/SeasonalPerks/creator/acceptance-fixture.json").read_text())
    root = json.loads((PROJECT / "Testing/restart-state.json").read_text())["root"]
    request("/client/game/start", session=root)
    season = fixture["SeasonId"]
    base = {"ProtocolVersion": 2, "SeasonId": season}
    snapshot = request("/wtt-seasonal/snapshot", base, root)
    check(snapshot.get("HasStory"), "Active test pack advertises story support")
    created = request("/wtt-seasonal/create", {**base, "OperationId": secrets.token_hex(16), "Nickname": "StoryTest", "Side": "Usec", "PerkIds": [fixture["PerkId"]]}, root)
    check(not created.get("Error"), "Synthetic story character created: " + str(created.get("Error")))
    character = created["SelectedCharacterId"]
    switched = request("/wtt-seasonal/switch", {**base, "Mode": "seasonal", "CharacterId": character}, root)
    child = switched["EffectiveProfileId"]
    identity = {"Version": 2, "SeasonId": season, "CharacterId": child}

    def read():
        result = request("/wtt-seasonal/story", identity, child)
        check(not result.get("Error"), "Story state read: " + str(result.get("Error")))
        return result

    def mutate(operation, target="", kind="", expect_error=False, item_ids=()):
        state = read()
        payload = {**identity, "ExpectedRevision": state["Revision"], "OperationId": secrets.token_hex(16),
                   "ConversationId": (state["State"].get("Conversation") or {}).get("Id", ""), "Target": target, "Kind": kind, "ItemIds": list(item_ids)}
        result = request("/wtt-seasonal/story/" + operation, payload, child)
        check(bool(result.get("Error")) == expect_error, operation + (" rejected" if expect_error else " committed") + ": " + str(result.get("Error")))
        return result, payload

    state = read()
    check(len(state["Definition"]["Chapters"]) == 2, "Synthetic story contains two chapters")
    check(request("/wtt-seasonal/story", {**identity, "CharacterId": root}, child).get("Error"), "Wrong-character story read rejected")
    check(request("/wtt-seasonal/story", {**identity, "SeasonId": "0" * 24}, child).get("Error"), "Wrong-season story read rejected")
    entry = state["Definition"]["EntryPoints"][0]["Id"]
    state, _ = mutate("start", entry)
    check(state["Line"]["Text"] == "This is a synthetic story-system test.", "NPC greeting advances automatically")
    bad = next(line for line in state["Choices"] if "invalid" in line["Text"])
    before = request("/client/game/profile/list", session=child)[0]
    mutate("select", bad["Id"], expect_error=True)
    after = request("/client/game/profile/list", session=child)[0]
    check(before == after, "Rejected native action rolls back variables, quests, inventory and profile state")
    accept = next(line for line in state["Choices"] if line["Text"].startswith("Accept"))
    state, accepted = mutate("select", accept["Id"])
    accepted_revision = state["Revision"]
    check(state["Facts"]["QuestStatuses"][fixture["QuestId"]] == "Started", "Dialogue uses native quest acceptance")
    check(len(state["State"]["Notes"]) == 1, "Dialogue unlocks journal note")
    replay = request("/wtt-seasonal/story/select", accepted, child)
    check(replay.get("Replayed") and replay["Revision"] == state["Revision"], "Accepted choice retry is idempotent")
    changed = {**accepted, "Target": bad["Id"]}
    check(request("/wtt-seasonal/story/select", changed, child).get("Error"), "Operation identifier cannot be reused for another choice")
    handover = next(line for line in state["Choices"] if line["Text"].startswith("Hand over"))
    profile_before = request("/client/game/profile/list", session=child)[0]
    mutate("select", handover["Id"], expect_error=True, item_ids=["0" * 24])
    selected = [i["_id"] for i in profile_before["Inventory"]["items"] if i["_tpl"] == fixture["DocumentTemplate"]]
    state, completed = mutate("select", handover["Id"], item_ids=selected)
    profile_after = request("/client/game/profile/list", session=child)[0]
    check(state["Facts"]["QuestStatuses"][fixture["QuestId"]] == "Success", "Handover and native quest completion commit together")
    check(state["Facts"]["QuestStatuses"][fixture["FollowupQuest"]] == "Started", "Second chapter automatically starts after prerequisite")
    def count(profile):
        return sum(i.get("upd", {}).get("StackObjectsCount", 1) for i in profile["Inventory"]["items"] if i["_tpl"] == fixture["DocumentTemplate"])
    check(count(profile_before) - count(profile_after) == 1, "Handover consumes exactly one item from a stack")
    check(profile_after["Info"]["Experience"] > profile_before["Info"]["Experience"], "Native quest experience reward granted")
    request("/wtt-seasonal/story/select", completed, child)
    check(request("/client/game/profile/list", session=child)[0] == profile_after, "Completion retry cannot repeat item consumption or rewards")
    note = next(iter(state["State"]["Notes"]))
    state, _ = mutate("read", note, "note")
    check(note in state["State"]["ReadNotes"], "Journal read marker persists")
    before_old_replay = request("/client/game/profile/list", session=child)[0]
    old = request("/wtt-seasonal/story/select", accepted, child)
    check(old.get("Replayed") and old["NativeRevision"] == accepted_revision < old["Revision"], "Old receipts retain the native update's revision alongside the latest snapshot")
    check(old["Facts"]["QuestStatuses"][fixture["QuestId"]] == "Success", "Old acceptance replay cannot regress the current quest snapshot")
    check(request("/client/game/profile/list", session=child)[0] == before_old_replay, "Old acceptance replay cannot change native quest rewards or inventory")
    mutate("raid", state["Definition"]["RaidBindings"][0]["Id"], "Collectible", expect_error=True)
    checkpoint = read()
    normal = request("/wtt-seasonal/switch", {**base, "Mode": "normal"}, root)
    check(not normal.get("Error"), "Normal character can be selected after story progress")
    check(request("/wtt-seasonal/story", identity, root).get("Error"), "Normal sessions cannot read a Seasonal story")
    check(request("/wtt-seasonal/story/select", accepted, child).get("Error"), "Inactive Seasonal sessions cannot replay story mutations")
    restored = request("/wtt-seasonal/switch", {**base, "Mode": "seasonal", "CharacterId": character}, root)
    check(not restored.get("Error"), "Seasonal character can be restored after isolation checks")
    check(read()["State"]["Notes"] == checkpoint["State"]["Notes"], "Character switching preserves story notes")
    legacy_child = json.loads((PROJECT / "Testing/restart-state.json").read_text())["child"]
    legacy_season = "69e232a764dfe95549003f0f"
    legacy_pack = SERVER / "user/mods/SeasonalPerks/creator/legacy.json"
    legacy_bytes = legacy_pack.read_bytes()
    legacy_switch = request("/wtt-seasonal/switch", {"ProtocolVersion": 2, "SeasonId": legacy_season, "Mode": "seasonal", "CharacterId": legacy_child}, root)
    check(not legacy_switch.get("Error") and not legacy_switch.get("HasStory"), "An existing season without authored story can be selected")
    legacy_identity = {"Version": 2, "SeasonId": legacy_season, "CharacterId": legacy_child}
    legacy_before = request("/client/game/profile/list", session=legacy_child)[0]
    empty_story = request("/wtt-seasonal/story", legacy_identity, legacy_child)
    check(not empty_story.get("Error"), "Story journal and visits can load without authored content")
    check(empty_story["Definition"]["Chapters"] == [] and empty_story["Definition"]["EntryPoints"] == [], "The fallback adds no story chapters or dialogue")
    check(request("/client/game/profile/list", session=legacy_child)[0] == legacy_before, "Opening the empty story system does not mutate the profile")
    empty_start = {**legacy_identity, "ExpectedRevision": empty_story["Revision"], "OperationId": secrets.token_hex(16), "Target": "0" * 24}
    check(request("/wtt-seasonal/story/start", empty_start, legacy_child).get("Error"), "Empty story cannot start an unauthored conversation")
    check(legacy_pack.read_bytes() == legacy_bytes, "System availability does not rewrite the existing season definition")
    restored = request("/wtt-seasonal/switch", {**base, "Mode": "seasonal", "CharacterId": character}, root)
    check(not restored.get("Error"), "Authored story character remains available after the empty-season check")
    (PROJECT / "Research/story-route-checks.json").write_text(json.dumps({"passed": len(checks), "checks": checks}, indent=2))
    (PROJECT / "Testing/story-state.json").write_text(json.dumps({"root": root, "child": child, "season": season}))
    print("Story:", len(checks), "checks passed.")


if __name__ == "__main__":
    main()
