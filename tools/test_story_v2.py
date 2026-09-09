"""Protocol-2 regression requests. Uses only synthetic accounts on Testing/Server:6975."""
import copy
import json
import secrets
from test_integration import PROJECT, SERVER, request, check, checks


def main():
    fixture = json.loads((SERVER / 'user/mods/SeasonalPerks/creator/acceptance-fixture.json').read_text())
    username = 'story-v2-' + secrets.token_hex(6)
    registered = request('/launcher/v2/register', {'username': username, 'edition': 'Standard'})
    root = next(p['profileId'] for p in registered['Profiles'] if p['username'] == username)
    cosmetics = json.loads((SERVER / 'SPT_Data/database/templates/customization.json').read_text())
    def cosmetic(parent):
        return next(k for k, v in cosmetics.items() if v.get('_parent') == parent and v['_props'].get('AvailableAsDefault') and 'Usec' in v['_props'].get('Side', []))
    request('/client/game/profile/create', {'side': 'Usec', 'nickname': 'StoryV2Test', 'headId': cosmetic('5cc085e214c02e000c6bea67'), 'voiceId': cosmetic('5fc100cf95572123ae738483')}, root)
    base = {'ProtocolVersion': 2, 'SeasonId': fixture['SeasonId']}
    created = request('/wtt-seasonal/create', {**base, 'OperationId': secrets.token_hex(16), 'Nickname': 'StoryV2', 'Side': 'Usec', 'PerkIds': [fixture['PerkId']]}, root)
    check(not created.get('Error'), 'Create synthetic protocol-2 character: ' + str(created.get('Error')))
    switched = request('/wtt-seasonal/switch', {**base, 'Mode': 'seasonal', 'CharacterId': created['SelectedCharacterId']}, root)
    child = switched['EffectiveProfileId']
    identity = {'Version': 2, 'SeasonId': fixture['SeasonId'], 'CharacterId': child}

    def read(extra=None):
        result = request('/wtt-seasonal/story', {**identity, **(extra or {})}, child)
        assert not result.get('Error'), result
        return result

    def profile():
        return request('/client/game/profile/list', session=child)[0]

    def payload(operation, target='', scene='V2Lobby', **extra):
        state = read()
        return {**identity, 'ExpectedRevision': state['Revision'], 'OperationId': secrets.token_hex(16),
                'ConversationId': (state['State'].get('Conversation') or {}).get('Id', ''),
                'Target': target, 'Scene': scene, 'Operation': operation, **extra}

    def send(operation, body, error=False):
        result = request('/wtt-seasonal/story/' + operation, body, child)
        check(bool(result.get('Error')) == error, operation + (' rejected' if error else ' accepted') + ': ' + str(result.get('Error')))
        return result

    state = read()
    check(state['Version'] == 2, 'Protocol-2 response advertised')
    auto = next(e for e in state['Definition']['EntryPoints'] if e['Scene'] == 'V2Lobby')
    automatic = next(d for d in state['Definition']['Dialogs'] if d['Id'] == auto['DialogId'])
    flag = next(a['Target'] for a in automatic['Lines'][0]['Actions'] if a['Type'] == 'CompleteItem')
    before = profile()
    send('prepare', payload('start', auto['Id'], scene='wrong'), error=True)
    check(profile() == before, 'Scene mismatch leaves inventory and story unchanged')
    body = payload('start', auto['Id'])
    prepared = send('prepare', body)
    retried = send('prepare', body)
    check(retried['PreparationId'] == prepared['PreparationId'], 'Lost preparation response retries reuse the original random context')
    check(prepared.get('Handover') and prepared['Handover']['Candidates'], 'Automatic NPC handover requests eligible item selection')
    check(profile() == before, 'Preparation does not commit earlier acceptance, inventory or rewards')
    check(read()['Revision'] == body['ExpectedRevision'], 'Cancelled preparation has no story revision change')
    bad = copy.deepcopy(body)
    bad['PreparationId'] = prepared['PreparationId']
    bad['Selections'] = {prepared['Handover']['ActionId']: ['0' * 24]}
    send('prepare', bad, error=True)
    check(profile() == before, 'Invalid item selection leaves entire staged chain unchanged')

    send('reconcile', payload('reconcile'))
    send('prepare', {**body, 'PreparationId': prepared['PreparationId']}, error=True)
    check(profile()['Inventory'] == before['Inventory'], 'Revision change rejects stale preparation without item consumption')
    inventory_body = payload('start', auto['Id'])
    inventory_prepare = send('prepare', inventory_body)
    moved = inventory_prepare['Handover']['Candidates'][0]
    item = next(i for i in profile()['Inventory']['items'] if i['_id'] == moved)
    location = {**item['location'], 'r': 1 if item['location'].get('r', 0) == 0 else 0}
    revision = read()['Revision']
    request('/client/game/profile/items/moving', {'data': [{'Action': 'Move', 'item': moved, 'to': {'id': item['parentId'], 'container': item['slotId'], 'location': location}}], 'tm': 0}, child)
    changed_inventory = profile()
    check(next(i for i in changed_inventory['Inventory']['items'] if i['_id'] == moved)['location'] != item['location'] and read()['Revision'] == revision, 'Native inventory changes independently of story revision')
    send('prepare', {**inventory_body, 'PreparationId': inventory_prepare['PreparationId']}, error=True)
    check(profile() == changed_inventory, 'Inventory changes invalidate preparation without committing any staged action')
    before = profile()
    body = payload('start', auto['Id'])
    prepared = send('prepare', body)

    body['PreparationId'] = prepared['PreparationId']
    body['Selections'] = {}
    selected_actions = []
    while prepared.get('Handover'):
        handover = prepared['Handover']
        selected_actions.append(handover['ActionId'])
        body['Selections'][handover['ActionId']] = [handover['Candidates'][0]]
        prepared = send('prepare', body)
        check(profile() == before, 'Every handover preparation remains uncommitted')
    check(len(selected_actions) == 2, 'Two automatic handovers are prepared independently')
    completed = send('start', body)
    after = profile()
    template = fixture['DocumentTemplate']
    count = lambda p: sum(i.get('upd', {}).get('StackObjectsCount', 1) for i in p['Inventory']['items'] if i['_tpl'] == template)
    check(count(before) - count(after) == 2, 'Prepared chain consumes exactly two items')
    check(flag in completed['State']['CompletedItems'], 'Standalone completion flag commits with the whole chain')
    check(completed['Lines'][-1]['Text'] == 'Both automatic deliveries are complete.' and completed['State']['Conversation']['Closed'], 'Closing NPC line is returned for acknowledgement')
    replay = send('start', body)
    check(replay['Replayed'] and not replay['Lines'] and profile() == after, 'Committed prepared retry cannot repeat items, rewards or playback')

    # A separate preparation becomes stale when a normal story mutation occurs.
    stale = payload('start', auto['Id'])
    stale_prepare = send('prepare', stale, error=True)  # Completed deliveries cannot consume again.
    check(profile() == after, 'Already-completed automatic delivery cannot consume additional items')
    legacy = payload('reconcile')
    legacy['Version'] = 1
    send('reconcile', legacy, error=True)
    check(profile() == after, 'Uncommitted legacy operation is rejected without side effects')

    # Active objectives can be read without a note reference.
    normal_entry = state['Definition']['EntryPoints'][0]['Id']
    greeting = send('start', payload('start', normal_entry))
    accept = next(l for l in greeting['Choices'] if l['Text'].startswith('Accept'))
    active = send('select', payload('select', accept['Id']))
    objective = fixture['ObjectiveId']
    read_result = send('read', payload('read', objective, Kind='condition'))
    check(objective in read_result['State']['ReadConditions'], 'Visible objective is readable without a discovered note association')
    send('read', payload('read', '0' * 24, Kind='condition'), error=True)
    send('close', payload('close'))

    before_raid = profile()
    start = request('/client/match/local/start', {'location': 'factory4_day', 'playerSide': 'pmc', 'mode': 'regular', 'timeVariant': 'CURR', 'transitionType': 0}, child)
    state = read()
    raid = state['State']['Raid']['Id']
    sequence = 0

    def observation(items=False, done=False):
        nonlocal sequence
        sequence += 1
        return {'CharacterId': child, 'RaidId': raid, 'Sequence': sequence, 'Level': 5, 'FreeSpecialSlots': 3,
                'Items': [{'Id': '123456789012345678901234', 'Template': template, 'StackCount': 1}] if items else [],
                'Counters': {objective: 1 if done else 0}, 'CompletedConditions': [objective] if done else [], 'Skills': {}}

    def event(binding, kind, obs=None, error=False):
        return send('raid', payload('raid', binding['Id'], scene='factory4_day', Kind=kind, RaidId=raid, Observation=obs), error)

    binding = next(b for b in state['Definition']['RaidBindings'] if b['Kind'] == 'Interact')
    event(binding, 'Interact', observation(), error=True)
    event(binding, 'Interact', observation(True), error=True)
    local = read({'Observation': observation(True, True)})
    check(local['Facts']['Items'][template] == 1 and local['Facts']['ConditionCounters'][objective] == 1, 'Current raid inventory and objective facts reach story snapshots')
    check(profile()['Inventory'] == before_raid['Inventory'], 'Reading observed loot never copies it into saved inventory')
    reset = read({'Observation': observation()})
    check(template not in reset['Facts']['Items'] and objective not in reset['Facts']['CompletedConditions'], 'Drop and reset remove stale story facts')
    invalid = observation(); invalid['Counters'] = {'0' * 24: 1}
    event(binding, 'Interact', invalid, error=True)
    invalid = observation(); invalid['Counters'][objective] = -1
    event(binding, 'Interact', invalid, error=True)
    invalid = observation(); invalid['Skills'] = {'unknown': 1}
    event(binding, 'Interact', invalid, error=True)
    raw = observation(True, True)
    raw['Items'][0]['Data'] = json.dumps({'_id': raw['Items'][0]['Id'], '_tpl': template, 'upd': {'StackObjectsCount': 1, 'SpawnedInSession': True}})
    check(read({'Observation': raw})['Facts']['Items'][template] == 1, 'Native item data survives both HTTP serializers')
    invalid = observation(True, True); invalid['RaidId'] = 'wrong'
    event(binding, 'Interact', invalid, error=True)
    invalid = observation(True, True); invalid['CharacterId'] = root
    event(binding, 'Interact', invalid, error=True)
    old = observation(True, True)
    read({'Observation': observation(True, True)})
    event(binding, 'Interact', old, error=True)
    repeated = observation(True, True)
    read({'Observation': repeated})
    check(bool(request('/wtt-seasonal/story', {**identity, 'Observation': repeated}, child).get('Error')), 'Repeated observation sequences are rejected')
    for binding in [b for b in state['Definition']['RaidBindings'] if b['Kind'] == 'Interact']:
        result = event(binding, 'Interact', observation(True, True))
        check(result['EventMediaId'] == binding['MediaId'] and not result['CinematicBindingId'], 'Ordinary event returns media without cinematic completion authority')
        check(binding['Id'] not in result['State']['CompletedBindings'], 'Event media does not commit survival-dependent progression')

    cinematic = next(b for b in state['Definition']['RaidBindings'] if b['Kind'] == 'Cinematic')
    begin = event(cinematic, 'begin', observation())
    check(begin['CinematicBindingId'] == cinematic['Id'], 'Cinematic start carries its exact completion binding')
    interrupted = event(cinematic, 'interrupt', observation())
    check(cinematic['Id'] not in interrupted['State']['CompletedBindings'] and not interrupted['Lines'], 'Interrupted cinematic does not run follow-up dialogue')
    event(cinematic, 'begin', observation())
    followup = event(cinematic, 'skip', observation())
    check(followup['Lines'] and followup['Lines'][0]['Text'] == 'The cinematic follow-up is visible.', 'Cinematic skip returns follow-up dialogue')
    check(any(a['Type'] == 'StartCinematic' for a in followup['Presentation']) and not followup['CinematicBindingId'], 'Follow-up cinematic is presentation-only, not a reused completion binding')
    request('/client/match/local/end', {'serverId': start['serverId'], 'results': {'profile': before_raid, 'result': 'Killed',
            'exitName': 'Gate 3', 'inSession': False, 'favorite': False, 'playTime': 600}, 'lostInsuredItems': [], 'transferItems': {}}, child)
    check(not read()['State']['Raid']['Pending'], 'Death clears pending progression after event media')
    invalid = observation(True, True)
    event(binding, 'Interact', invalid, error=True)
    (PROJECT / 'Research/story-v2-checks.json').write_text(json.dumps({'passed': len(checks), 'checks': checks}, indent=2))
    (PROJECT / 'Testing/story-v2-upgrade.json').write_text(json.dumps({'child': child, 'season': fixture['SeasonId'], 'request': body}))
    print('Story protocol 2:', len(checks), 'checks passed.')


if __name__ == '__main__':
    main()
