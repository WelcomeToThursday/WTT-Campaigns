"""Creator acceptance against Testing/Server only; native routes and synthetic profiles."""
import argparse
import copy
import json
import secrets
from pathlib import Path
from test_integration import PROJECT, SERVER, request, check, checks

MOD = SERVER / 'user/mods/SeasonalPerks'
FIXTURE = MOD / 'creator/acceptance-fixture.json'
STATE = PROJECT / 'Testing/creator-acceptance-state.json'


def main():
    phase = argparse.ArgumentParser()
    phase.add_argument('phase', choices=['verify', 'legacy', 'resume'])
    phase = phase.parse_args().phase
    fixture = json.loads(FIXTURE.read_text(encoding='utf-8'))
    previous = json.loads((PROJECT / 'Testing/restart-state.json').read_text())
    root = previous['root']
    body = {'ProtocolVersion': 2, 'SeasonId': fixture['SeasonId']}

    def seasonal(path, value=None, session=None):
        return request('/wtt-seasonal/' + path, {**body, **(value or {})}, session or root)

    if phase == 'legacy':
        snapshot = request('/wtt-seasonal/snapshot', {'ProtocolVersion': 2}, root)
        check(snapshot['SeasonId'] == '69e232a764dfe95549003f0f', 'Legacy season reactivated')
        switched = request('/wtt-seasonal/switch', {'ProtocolVersion': 2, 'Mode': 'seasonal'}, root)
        check(switched.get('EffectiveProfileId') == previous['child'], 'Original seasonal character preserved')
        check(set(previous['selected']).issubset(switched['State']['SeasonalPerks']) and switched['State']['Revision'] == previous['revision'], 'Original selected perks preserved')
        baseline = json.loads((PROJECT / 'Testing/normal-baseline.json').read_text(encoding='utf-8'))
        normal = request('/client/game/profile/list', session=root)
        for index in [0, 1]:
            for field in ['Inventory', 'Quests', 'Skills']:
                check(normal[index].get(field) == baseline[index].get(field), 'Normal character ' + str(index) + ' preserves ' + field)
        archived = json.loads(STATE.read_text())['child']
        check(request('/wtt-seasonal/hub/claim', {'ProtocolVersion': 2, 'SeasonId': fixture['SeasonId'], 'OperationId': secrets.token_hex(16), 'RewardId': fixture['GatedReward']}, archived).get('Error'), 'Archived season cannot claim rewards')
        check(request('/wtt-seasonal/snapshot', {'ProtocolVersion': 2}, archived).get('Error'), 'Archived seasonal session is rejected')
    elif phase == 'resume':
        saved = json.loads(STATE.read_text())
        snapshot = seasonal('switch', {'Mode': 'seasonal'})
        check(snapshot['EffectiveProfileId'] == saved['child'], 'Custom seasonal character restored')
        hub = seasonal('hub', session=saved['child'])
        check(hub['ClaimedRewards'] == saved['claimed'], 'Custom season claims persist across A-B-A')
        profile = request('/client/game/profile/list', session=saved['child'])[0]
        check(profile['Inventory']['items'] == saved['inventory'], 'Custom seasonal inventory preserved')
    else:
        snapshot = seasonal('snapshot')
        check(snapshot.get('SeasonId') == fixture['SeasonId'], 'Published pack activated: ' + str(snapshot.get('Error')))
        check(snapshot['ActiveMode'] == 'normal', 'Season switch resets active character to Normal')
        check(snapshot['DocumentTemplates'] == [fixture['DocumentTemplate']], 'Client document mappings come from active pack')
        check(request('/wtt-seasonal/snapshot', session=root).get('Error'), 'Old client protocol rejected for authored seasons')
        check(seasonal('snapshot', {'SeasonId': '0' * 24}).get('Error'), 'Wrong-season request rejected')
        created = seasonal('create', {'PerkIds': [fixture['PerkId']], 'Nickname': 'CreatorTest', 'Side': 'Usec'})
        check(not created.get('Error'), 'Create custom seasonal character: ' + str(created.get('Error')))
        switched = seasonal('switch', {'Mode': 'seasonal'})
        child = switched['EffectiveProfileId']
        check(child not in [root, previous['child']], 'New season uses independent profile')
        profile = request('/client/game/profile/list', session=child)[0]
        check(next(s['Progress'] for s in profile['Skills']['Common'] if s['Id'] == 'Strength') == 200, 'Custom starting skills applied')
        doc = next(i for i in profile['Inventory']['items'] if i['_tpl'] == fixture['DocumentTemplate'])
        check(doc['upd']['StackObjectsCount'] == 8, 'Custom starting document grant applied')
        check(fixture['PerkId'] in switched['State']['SeasonalPerks'], 'Authored perk selected')
        hub = seasonal('hub', session=child)
        check(hub['SeasonName'] == 'Creator acceptance season' and hub['Pages'][0]['Rewards'][0]['CanClaim'] is False, 'Custom branding and quest gate active')

        def native(action):
            result = request('/client/game/profile/items/moving', {'data': [action]}, child, allow_error=True)
            check(not result.get('err') and not (result.get('data') or {}).get('warnings'), 'Native ' + action['Action'] + ': ' + str(result.get('errmsg')))
            return result

        quests = request('/client/quest/list', session=child)
        check(any(q['_id'] == fixture['QuestId'] for q in quests), 'Authored quest registered and visible')
        native({'Action': 'QuestAccept', 'qid': fixture['QuestId'], 'type': 'Quest'})
        native({'Action': 'QuestHandover', 'qid': fixture['QuestId'], 'conditionId': fixture['ObjectiveId'], 'items': [{'id': doc['_id'], 'count': doc['upd']['StackObjectsCount']}]})
        native({'Action': 'QuestComplete', 'qid': fixture['QuestId'], 'removeExcessItems': False})
        profile = request('/client/game/profile/list', session=child)[0]
        doc = next(i for i in profile['Inventory']['items'] if i['_tpl'] == fixture['DocumentTemplate'])
        native({'Action': 'QuestAccept', 'qid': fixture['FollowupQuest'], 'type': 'Quest'})
        native({'Action': 'QuestHandover', 'qid': fixture['FollowupQuest'], 'conditionId': fixture['FollowupObjective'], 'items': [{'id': doc['_id'], 'count': doc['upd']['StackObjectsCount']}]})
        native({'Action': 'QuestComplete', 'qid': fixture['FollowupQuest'], 'removeExcessItems': False})
        hub = seasonal('hub', session=child)
        gated = next(r for r in hub['Pages'][0]['Rewards'] if r['Id'] == fixture['GatedReward'])
        check(gated['CanClaim'], 'Native quest completion unlocks authored reward: ' + gated['UnavailableReason'])
        for reward in [fixture['GatedReward'], fixture['CrateReward']]:
            hub = seasonal('hub', session=child)
            operation = {'OperationId': secrets.token_hex(16), 'ExpectedRevision': hub['Revision'], 'RewardId': reward}
            result = seasonal('hub/claim', operation, child)
            check(result.get('Committed'), 'Authored reward claim: ' + str(result.get('Error')))
            check(seasonal('hub/claim', operation, child).get('Committed'), 'Claim retry is idempotent')
        profile = request('/client/game/profile/list', session=child)[0]
        crate = next(i for i in profile['Inventory']['items'] if i['_tpl'] == fixture['Crate'])
        native({'Action': 'OpenRandomLootContainer', 'item': crate['_id']})
        profile = request('/client/game/profile/list', session=child)[0]
        check(not any(i['_id'] == crate['_id'] for i in profile['Inventory']['items']), 'Custom crate consumed through native opening')
        check(sum(i.get('upd', {}).get('StackObjectsCount', 1) for i in profile['Inventory']['items'] if i['_tpl'] == fixture['DocumentTemplate']) == 5, 'Two quests and a claim each consume one document')
        hub = seasonal('hub', session=child)
        exchange = seasonal('hub/exchange', {'OperationId': secrets.token_hex(16), 'ExpectedRevision': hub['Revision'], 'DocumentId': fixture['DocumentId'], 'Sources': {fixture['DocumentId']: 5}}, child)
        check(exchange.get('Committed'), 'Authored document exchange succeeds')
        profile = request('/client/game/profile/list', session=child)[0]
        check(sum(i.get('upd', {}).get('StackObjectsCount', 1) for i in profile['Inventory']['items'] if i['_tpl'] == fixture['DocumentTemplate']) == 1, 'Exchange charges configured document cost')
        hub = seasonal('hub', session=child)
        STATE.write_text(json.dumps({'root': root, 'child': child, 'claimed': hub['ClaimedRewards'], 'inventory': profile['Inventory']['items']}))
    (PROJECT / ('Research/creator-' + phase + '-checks.json')).write_text(json.dumps({'passed': len(checks), 'checks': checks}, indent=2))
    print('Creator', phase + ':', len(checks), 'checks passed.')


if __name__ == '__main__':
    main()
