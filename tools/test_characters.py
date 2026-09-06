"""Multiple characters/seasons and destructive actions on synthetic accounts only."""
import json
import secrets

from test_integration import PROJECT, SERVER, request, check, checks


def account():
    name = 'carousel-test-' + secrets.token_hex(6)
    registered = request('/launcher/v2/register', {'username': name, 'edition': 'Standard'})
    root = next(p['profileId'] for p in registered['Profiles'] if p['username'] == name)
    customization = json.loads((SERVER / 'SPT_Data/database/templates/customization.json').read_text())

    def cosmetic(parent):
        return next(k for k, v in customization.items() if v.get('_parent') == parent
                    and v['_props'].get('AvailableAsDefault') and 'Usec' in v['_props'].get('Side', []))

    request('/client/game/profile/create', {'side': 'Usec', 'nickname': 'Carousel',
            'headId': cosmetic('5cc085e214c02e000c6bea67'), 'voiceId': cosmetic('5fc100cf95572123ae738483')}, root)
    return root


def main():
    root = account()

    def call(action, **data):
        result = request('/wtt-seasonal/' + action, {'ProtocolVersion': 2, **data}, root)
        check(not result.get('Error'), action + ': ' + str(result.get('Error')))
        return result

    initial = call('snapshot')
    check(len(initial['Seasons']) >= 2, 'At least two installed seasons are playable together')
    def progression():
        return [{k: p.get(k) for k in ['_id', 'Info', 'Inventory', 'Skills', 'Quests', 'Customization', 'TradersInfo']}
                for p in request('/client/game/profile/list', session=root)]
    normal = progression()
    fixture = json.loads((SERVER / 'user/mods/SeasonalPerks/creator/acceptance-fixture.json').read_text())
    characters = []
    authored = next(s for s in initial['Seasons'] if s['Id'] == fixture['SeasonId'])
    legacy = next(s for s in initial['Seasons'] if s['Id'] == '69e232a764dfe95549003f0f')
    for index, season in enumerate([authored, authored, legacy]):
        operation = secrets.token_hex(16)
        payload = dict(SeasonId=season['Id'], OperationId=operation, Nickname='Season' + str(index), Side='Usec', PerkIds=[fixture['PerkId']] if index == 0 else [])
        created = call('create', **payload)
        child = created['SelectedCharacterId']
        check(child not in characters, 'New character gets a distinct profile ID')
        characters.append(child)
        again = call('create', **payload)
        check(again['SelectedCharacterId'] == child, 'Creation retry returns the same character')
        check(len(again['Characters']) == index + 2, 'Creation retry does not add another card')
        conflict = request('/wtt-seasonal/create', {'ProtocolVersion': 2, **payload, 'Nickname': 'Different'}, root)
        check(bool(conflict.get('Error')), 'Reusing a creation ID with different choices is rejected')

    baselines = {}
    hubs = {}
    for child in characters * 2:
        switched = call('switch', Mode='seasonal', CharacterId=child)
        check(switched['EffectiveProfileId'] == child, 'Switch selects exact character ID')
        summary = next(c for c in switched['Characters'] if c['Id'] == child)
        check(switched['SeasonId'] == summary['SeasonId'], 'Snapshot uses the selected character season')
        hub = request('/wtt-seasonal/hub', {'ProtocolVersion': 2}, child)
        check(not hub.get('Error') and hub['SeasonId'] == summary['SeasonId'], 'Rewards use the selected character season')
        quests = request('/client/quest/list', session=child)
        check(any(q['_id'] == fixture['QuestId'] for q in quests) == (child != characters[2]), 'Only the character own season quests are listed')
        if child == characters[2]:
            denied = request('/client/game/profile/items/moving', {'data': [{'Action': 'QuestAccept', 'qid': fixture['QuestId'], 'type': 'Quest'}]}, child, allow_error=True)
            check(bool(denied.get('err') or (denied.get('data') or {}).get('warnings')), 'Cannot accept a quest from another season')
        profile = request('/client/game/profile/list', session=child)
        stable = [{k: p.get(k) for k in ['_id', 'Inventory', 'Skills', 'Quests', 'Customization']} for p in profile]
        if child in baselines:
            check(stable == baselines[child], 'Switching preserves progression and equipment')
        baselines[child] = stable
        hubs[child] = hub['Id']
    check(hubs[characters[0]] != hubs[characters[2]], 'Different seasons have distinct battle passes')
    active_delete = request('/wtt-seasonal/delete', {'ProtocolVersion': 2, 'CharacterId': characters[2]}, root)
    check(bool(active_delete.get('Error')), 'Server rejects deletion while the target session is active')
    call('switch', Mode='normal')
    check(progression() == normal, 'Normal PMC and Scav are unchanged')
    for child, season in [(characters[0], authored), (characters[2], legacy)]:
        selected = call('switch', Mode='seasonal', CharacterId=child)
        check((fixture['PerkId'] in selected['State']['SeasonalPerks']) == (child == characters[0]), 'Personal modifiers remain attached to their own season and character')
        request('/client/game/start', session=child)
        profile = request('/client/game/profile/list', session=child)[0]
        raid = request('/client/match/local/start', {'location': 'bigmap', 'playerSide': 'pmc', 'mode': 'regular', 'timeVariant': 'CURR', 'transitionType': 0}, child)
        check(bool(raid.get('serverId')), 'Native raid starts for ' + season['Name'])
        for action in ['create', 'delete', 'wipe', 'switch']:
            rejected = request('/wtt-seasonal/' + action, {'ProtocolVersion': 2, 'CharacterId': characters[1], 'Mode': 'normal', 'OperationId': secrets.token_hex(16), 'SeasonId': season['Id']}, root)
            check(bool(rejected.get('Error')), 'Cannot ' + action + ' characters during a raid')
        if child == characters[0]:
            achievements = json.loads((SERVER / 'SPT_Data/database/templates/achievements.json').read_text())
            achievement = achievements[0]['id']
            profile.setdefault('Achievements', {})[achievement] = 1700000000
        request('/client/match/local/end', {'serverId': raid['serverId'], 'results': {'profile': profile, 'result': 'Survived', 'exitName': 'Crossroads', 'inSession': False, 'favorite': False, 'playTime': 600}, 'lostInsuredItems': [], 'transferItems': {}}, child)
        check(call('snapshot')['EffectiveProfileId'] == child, 'Raid finishes without changing the selected profile')
    call('switch', Mode='seasonal', CharacterId=characters[0])
    hub = request('/wtt-seasonal/hub', {'ProtocolVersion': 2}, characters[0])
    claimed = request('/wtt-seasonal/hub/claim', {'ProtocolVersion': 2, 'SeasonId': authored['Id'], 'OperationId': secrets.token_hex(16), 'ExpectedRevision': hub['Revision'], 'RewardId': fixture['CrateReward']}, characters[0])
    check(claimed.get('Committed'), 'Authored season reward can be claimed')
    call('switch', Mode='seasonal', CharacterId=characters[1])
    sibling_hub = request('/wtt-seasonal/hub', {'ProtocolVersion': 2}, characters[1])
    check(sibling_hub['ClaimedRewards'] == 0, 'Claiming a reward does not claim it for another character in the same season')
    call('switch', Mode='normal')
    outsider = account()
    for action in ['delete', 'wipe', 'switch', 'edit']:
        rejected = request('/wtt-seasonal/' + action,
                           {'ProtocolVersion': 2, 'CharacterId': characters[0], 'Mode': 'seasonal', 'OperationId': secrets.token_hex(16)}, outsider)
        check(bool(rejected.get('Error')), 'Another account cannot ' + action + ' this character')
    root_delete = request('/wtt-seasonal/delete', {'ProtocolVersion': 2, 'CharacterId': root}, root)
    check(bool(root_delete.get('Error')), 'Root account cannot be deleted through a seasonal card')
    earned = request('/client/game/profile/list', session=characters[0])[0]['Achievements']
    check(bool(earned), 'Fixture has earned achievements before wiping')
    wipe_operation = secrets.token_hex(16)
    wiped = call('wipe', CharacterId=characters[0], OperationId=wipe_operation)
    slot = next(c for c in wiped['Characters'] if c['Id'] == characters[0])
    check(slot['Wiped'] and not slot['Exists'], 'Wipe returns the character to creation')
    check(len(wiped['Characters']) == 4, 'Wipe keeps one recreatable card in the same season')
    check(not (SERVER / 'user/profiles' / (characters[0] + '.json')).exists(), 'Wipe removes the old progression file')
    replay = call('wipe', CharacterId=characters[0], OperationId=wipe_operation)
    check(next(c for c in replay['Characters'] if c['Id'] == characters[0])['Wiped'], 'Wipe retry keeps the same empty slot')
    denied = request('/wtt-seasonal/switch', {'ProtocolVersion': 2, 'Mode': 'seasonal', 'CharacterId': characters[0]}, root)
    check(bool(denied.get('Error')), 'A wiped character must be recreated before playing')
    recreation = dict(CharacterId=characters[0], SeasonId=authored['Id'], OperationId=secrets.token_hex(16), Nickname='Reborn', Side='Bear', PerkIds=[])
    recreated = call('create', **recreation)
    replacement = recreated['SelectedCharacterId']
    check(replacement == characters[0], 'Recreation retains the character slot and season')
    check(call('create', **recreation)['SelectedCharacterId'] == replacement, 'Recreation retry is idempotent')
    reset_profile = request('/client/game/profile/list', session=replacement)[0]
    check(reset_profile['Info']['Nickname'] == 'Reborn' and reset_profile['Info']['Side'] == 'Bear', 'Wipe permits choosing a new nickname and faction')
    check(reset_profile['Achievements'] == earned, 'Wipe preserves all earned achievements')
    selected = call('switch', Mode='seasonal', CharacterId=replacement)
    check(fixture['PerkId'] not in selected['State']['SeasonalPerks'], 'Old personal modifiers are replaced by the new selection')
    reset_hub = request('/wtt-seasonal/hub', {'ProtocolVersion': 2}, replacement)
    check(reset_hub['Revision'] == 0 and reset_hub['ClaimedRewards'] == 0, 'Wipe clears existing raid and reward progress')
    check(not any(i['_tpl'] == fixture['Crate'] for i in reset_profile['Inventory']['items']), 'Wipe removes previously claimed reward items')
    check(reset_profile['Inventory']['equipment'] != baselines[characters[0]][0]['Inventory']['equipment'], 'Recreation receives fresh equipment')
    replay = call('wipe', CharacterId=characters[0], OperationId=wipe_operation)
    check(next(c for c in replay['Characters'] if c['Id'] == replacement)['Exists'], 'Lost wipe response cannot wipe a newly recreated character again')
    call('switch', Mode='normal')
    deleted = call('delete', CharacterId=characters[1])
    check(len(deleted['Characters']) == 3 and all(c['Id'] != characters[1] for c in deleted['Characters']), 'Delete removes only its target')
    replay = call('delete', CharacterId=characters[1])
    check(len(replay['Characters']) == 3, 'Delete retry is safe')
    check(not (SERVER / 'user/profiles' / (characters[1] + '.json')).exists(), 'Deleted profile file is removed')
    check(progression() == normal, 'Wipe and delete leave regular progression unchanged')
    (PROJECT / 'Testing/carousel-test-state.json').write_text(json.dumps({'root': root, 'remaining': [replacement, characters[2]]}))
    (PROJECT / 'Research/character-checks.json').write_text(json.dumps({'passed': len(checks), 'checks': checks}, indent=2))
    print('PASS', len(checks), 'multiple-character checks')


if __name__ == '__main__':
    main()
