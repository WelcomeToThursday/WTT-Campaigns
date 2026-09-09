"""Exercise the shipped Story Sandbox on an isolated runtime, never live profiles."""
import json
import secrets
import sys
from pathlib import Path

import test_integration as api


def main():
    artifact = Path(sys.argv[1]).resolve()
    server = api.PROJECT / 'Testing/StoryQuestServerV2'
    api.BASE = 'https://127.0.0.1:6991'
    assert json.loads((server / 'SPT_Data/configs/http.json').read_text(encoding='utf-8-sig'))['port'] == 6991
    fixture = json.loads((artifact / 'test-story.json').read_text())
    request, check = api.request, api.check
    templates = json.loads((server / 'SPT_Data/database/templates/items.json').read_text())
    check(templates[fixture['Bandage']]['_name'] == 'bandage', 'Bandage template exists in installed SPT database')
    username = 'story-sandbox-' + secrets.token_hex(5)
    registered = request('/launcher/v2/register', {'username': username, 'edition': 'Standard'})
    root = next(p['profileId'] for p in registered['Profiles'] if p['username'] == username)
    customization = json.loads((server / 'SPT_Data/database/templates/customization.json').read_text())
    def cosmetic(parent):
        return sorted(k for k, v in customization.items() if v.get('_parent') == parent and v['_props'].get('AvailableAsDefault') and 'Usec' in v['_props'].get('Side', []))[0]
    request('/client/game/profile/create', {'side': 'Usec', 'nickname': 'StoryNormal', 'headId': cosmetic('5cc085e214c02e000c6bea67'), 'voiceId': cosmetic('5fc100cf95572123ae738483')}, root)
    normal = request('/client/game/profile/list', session=root)[0]
    base = {'ProtocolVersion': 2, 'SeasonId': fixture['SeasonId']}
    created = request('/wtt-seasonal/create', {**base, 'OperationId': secrets.token_hex(16), 'Nickname': 'FieldTest', 'Side': 'Usec', 'PerkIds': [fixture['PerkId']]}, root)
    check(not created.get('Error'), 'Create separate test character: ' + str(created.get('Error')))
    character = created['SelectedCharacterId']
    switched = request('/wtt-seasonal/switch', {**base, 'Mode': 'seasonal', 'CharacterId': character}, root)
    check(not switched.get('Error'), 'Switch to Story Sandbox')
    child = switched['EffectiveProfileId']
    identity = {'Version': 2, 'SeasonId': fixture['SeasonId'], 'CharacterId': child}
    def read():
        result = request('/wtt-seasonal/story', identity, child)
        assert not result.get('Error'), result.get('Error')
        return result
    def mutate(operation, target, item_ids=()):
        state = read()
        payload = {**identity, 'ExpectedRevision': state['Revision'], 'OperationId': secrets.token_hex(16), 'ConversationId': (state['State'].get('Conversation') or {}).get('Id', ''), 'Target': target, 'ItemIds': list(item_ids)}
        result = request('/wtt-seasonal/story/' + operation, payload, child)
        check(not result.get('Error'), operation + ' succeeds: ' + str(result.get('Error')))
        return result, payload
    def profile():
        return request('/client/game/profile/list', session=child)[0]
    def count(p):
        return sum(i.get('upd', {}).get('StackObjectsCount', 1) for i in p['Inventory']['items'] if i['_tpl'] == fixture['Bandage'])
    def choice(state, key):
        return any(line['Id'] == fixture[key] for line in state['Choices'])
    before = profile()
    check(count(before) >= 2, 'Starter stash contains two usable bandages')
    state, _ = mutate('start', fixture['EntryId'])
    check(choice(state, 'AcceptId'), 'Prapor offers Field dressing')
    state, accepted = mutate('select', fixture['AcceptId'])
    check(state['Facts']['QuestStatuses'][fixture['QuestId']] == 'Started', 'Native quest accepted')
    check(len(state['State']['Notes']) == 2, 'Briefing and acceptance appear in journal')
    check(bool(state['Objectives']), 'Native handover objective visible')
    mutate('select', fixture['LeaveId'])
    state, _ = mutate('start', fixture['EntryId'])
    check(choice(state, 'HandoverId') and not choice(state, 'AcceptId'), 'Leaving and returning resumes accepted quest')
    selected = next(i['_id'] for i in profile()['Inventory']['items'] if i['_tpl'] == fixture['Bandage'])
    state, delivered = mutate('select', fixture['HandoverId'], [selected])
    check(count(before) - count(profile()) == 1, 'Handover consumes exactly one bandage')
    check(choice(state, 'FinishId'), 'Delivery unlocks reward collection')
    mutate('select', fixture['LeaveId'])
    state, _ = mutate('start', fixture['EntryId'])
    check(choice(state, 'FinishId') and not choice(state, 'HandoverId'), 'Reward remains available after leaving and returning')
    pre_reward = profile()
    state, completed = mutate('select', fixture['FinishId'])
    after = profile()
    check(state['Facts']['QuestStatuses'][fixture['QuestId']] == 'Success', 'Native quest completed')
    check(after['Info']['Experience'] - pre_reward['Info']['Experience'] == 250, 'Exactly 250 XP awarded')
    check(len(state['State']['Notes']) == 3, 'Completion note appears in journal')
    replay = request('/wtt-seasonal/story/select', completed, child)
    check(replay.get('Replayed') and profile()['Info']['Experience'] == after['Info']['Experience'], 'Reward retry cannot duplicate XP')
    mutate('select', fixture['LeaveId'])
    state, _ = mutate('start', fixture['EntryId'])
    check(all(not choice(state, key) for key in ['AcceptId', 'HandoverId', 'FinishId']), 'Completed visit cannot repeat quest or rewards')
    request('/wtt-seasonal/switch', {**base, 'Mode': 'normal'}, root)
    normal_after = request('/client/game/profile/list', session=root)[0]
    check(normal_after['Inventory'] == normal['Inventory'] and normal_after['Info']['Experience'] == normal['Info']['Experience'] and normal_after['Quests'] == normal['Quests'], 'Normal character inventory, XP and quests preserved')
    saved = json.loads((server / 'user/profiles' / (child + '.json')).read_text())
    messages = [m for d in saved['dialogues'].values() for m in d['messages'] if m.get('templateId') == fixture['QuestId'] + ' successMessageText']
    check(len(messages) == 1, 'Exactly one completion reward message persisted after retries')
    payment = sum(i.get('upd', {}).get('StackObjectsCount', 1) for i in messages[0]['items']['data'] if i['_tpl'] == '5449016a4bdc2d6f028b456f')
    check(payment >= 5000, 'Base 5,000 rouble payment delivered with native character bonuses')
    (artifact / 'route-checks.json').write_text(json.dumps({'passed': len(api.checks), 'checks': api.checks, 'root': root, 'child': child}, indent=2))
    print(f'Passed {len(api.checks)} playable story checks.')


if __name__ == '__main__':
    main()
