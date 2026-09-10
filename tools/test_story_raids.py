"""Story raid persistence on the isolated synthetic account, never the installed server."""
import copy
import json
import secrets
from test_integration import PROJECT, SERVER, request, check, checks


def main():
    account = json.loads((PROJECT / 'Testing/story-state.json').read_text())
    fixture = json.loads((SERVER / 'user/mods/WTT-Campaigns/creator/acceptance-fixture.json').read_text())
    child = account['child']
    identity = {'Version': 2, 'SeasonId': account['season'], 'CharacterId': child}

    def read():
        result = request('/wtt-campaigns/story', identity, child)
        assert not result.get('Error'), result
        return result

    def event(binding, kind, item='', raid_id=None, error=False):
        state = read()
        payload = {**identity, 'ExpectedRevision': state['Revision'], 'OperationId': secrets.token_hex(16),
                   'Target': binding, 'Kind': kind, 'ItemId': item, 'Scene': next(b for b in state['Definition']['RaidBindings'] if b['Id'] == binding)['ObjectPath'].split(':/')[0],
                   'RaidId': raid_id if raid_id is not None else state['State']['Raid']['Id']}
        result = request('/wtt-campaigns/story/raid', payload, child)
        check(bool(result.get('Error')) == error, kind + (' rejected' if error else ' committed'))
        return result, payload

    def begin():
        profile = request('/client/game/profile/list', session=child)[0]
        start = request('/client/match/local/start', {'location': 'factory4_day', 'playerSide': 'pmc',
                        'mode': 'regular', 'timeVariant': 'CURR', 'transitionType': 0}, child)
        state = read()
        check(state['State']['Raid']['Id'] == start['serverId'], 'Story raid identity matches native raid')
        return profile, start

    def end(profile, start, outcome):
        payload = {'serverId': start['serverId'], 'results': {'profile': profile, 'result': outcome,
                   'exitName': 'Gate 3', 'inSession': False, 'favorite': False, 'playTime': 600},
                   'lostInsuredItems': [], 'transferItems': {}}
        request('/client/match/local/end', payload, child)
        after = request('/client/game/profile/list', session=child)[0]
        snapshot = read()
        request('/client/match/local/end', payload, child)
        check(request('/client/game/profile/list', session=child)[0] == after, 'Raid-end retry cannot repeat rewards')
        check(read() == snapshot, 'Raid-end retry cannot change story progress')
        check(snapshot['State']['Raid']['Finished'], 'Story raid closes after native merge')
        return after, snapshot

    request('/client/game/start', session=child)
    state = read()
    bindings = {b['Kind']: b for b in state['Definition']['RaidBindings']}
    collectible, pending, cinematic = [bindings[k]['Id'] for k in ('Collectible', 'Trigger', 'Cinematic')]
    profile, start = begin()
    items = read()['State']['Raid']['SpawnedItems']
    check(bool(items), 'Collectible identities registered from native generated loot')
    event(collectible, 'Collectible', '0' * 24, error=True)
    event(collectible, 'Collectible', next(iter(items)), raid_id='wrong-raid', error=True)
    event(pending, 'Trigger')
    check(pending not in read()['State']['CompletedBindings'], 'Extraction-only effect remains pending during raid')
    event(cinematic, 'complete', error=True)
    started, _ = event(cinematic, 'begin')
    check(any(a['Type'] == 'StartCinematic' for a in started['Presentation']), 'Cinematic begin requests presentation')
    event(cinematic, 'interrupt')
    check(cinematic not in read()['State']['CompletedBindings'], 'Interrupted cinematic does not complete its binding')
    event(cinematic, 'begin')
    event(cinematic, 'skip')
    check(cinematic in read()['State']['CompletedBindings'], 'Skip acknowledges the active cinematic once')
    collected, payload = event(collectible, 'Collectible', next(iter(items)))
    check(collected['Facts']['QuestStatuses'][fixture['FollowupQuest']] == 'Success', 'Collectible completes native story quest in raid')
    check(collected['State']['Raid']['Experience'] == 75, 'Native raid reward recorded for merge')
    before_retry = request('/client/game/profile/list', session=child)[0]
    replay = request('/wtt-campaigns/story/raid', payload, child)
    check(replay.get('Replayed') and not replay['Presentation'] and not replay['Lines'], 'Raid retry has no repeated presentation')
    check(request('/client/game/profile/list', session=child)[0] == before_retry, 'Collectible retry cannot grant rewards twice')
    profile['Info']['Experience'] += 30
    after, state = end(profile, start, 'Killed')
    check(after['Info']['Experience'] == profile['Info']['Experience'] + 75, 'Raid report preserves both local XP and committed story reward')
    quest = next(q for q in after['Quests'] if q['qid'] == fixture['FollowupQuest'])
    check(quest['status'] in (4, 'Success'), 'Stale raid quest report cannot undo committed completion')
    check(collectible in state['State']['CompletedBindings'], 'Persistent collectible survives death')
    check(pending not in state['State']['CompletedBindings'] and not state['State']['Raid']['Pending'], 'Death discards extraction-only effects')
    check(not state['State']['Receipts'], 'Internal receipt history is absent from the client projection')
    profile, start = begin()
    event(pending, 'Trigger')
    after, state = end(profile, start, 'Survived')
    check(pending in state['State']['CompletedBindings'], 'Survival commits extraction-only effects')
    event(collectible, 'Collectible', next(iter(items)), error=True)
    profile, start = begin()
    request('/client/game/start', session=child)
    state = read()
    check(state['State']['Raid']['Finished'] and not state['State']['Raid']['Pending'], 'New session abandons unfinished raid state')
    check(collectible in state['State']['CompletedBindings'], 'New session preserves committed story progress')
    (PROJECT / 'Research/story-raid-checks.json').write_text(json.dumps({'passed': len(checks), 'checks': checks}, indent=2))
    print('Story raids:', len(checks), 'checks passed.')


if __name__ == '__main__':
    main()
