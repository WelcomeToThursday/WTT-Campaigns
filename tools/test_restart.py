"""Restart/recovery tests. Prepare only while the isolated server is stopped."""
import argparse, hashlib, json
from pathlib import Path
from test_integration import PROJECT, SERVER, request, check, checks

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('phase', choices=['prepare', 'verify'])
    args = parser.parse_args()
    state = json.loads((PROJECT/'Testing/restart-state.json').read_text(encoding="utf-8"))
    root, child = state['root'], state['child']
    profile_path = SERVER/'user/profiles'/f'{child}.json'
    profile = json.loads(profile_path.read_text(encoding="utf-8"))
    assert profile['info']['username'].startswith('season-test-'), 'Synthetic accounts only'
    link_path = SERVER/'user/profileData'/root/'cjSeasonalPerksAccount.json'
    if args.phase == 'prepare':
        assert not (SERVER/'test-server.pid').exists(), 'Stop the isolated server before preparing fixtures'
        # Simulate a saved raid marker after the game was interrupted.
        link = json.loads(link_path.read_text(encoding="utf-8"))
        link['ActiveRaidProfiles'] = [child]
        link['Mode'] = 'seasonal'
        link_path.write_text(json.dumps(link))
        # A previously granted skill changed later; reconnect/edit must not reapply its preset.
        strength = next(s for s in profile['characters']['pmc']['Skills']['Common'] if s['Id']=='Strength')
        strength['Progress'] = 500
        profile_path.write_text(json.dumps(profile))
        print('Prepared isolated restart fixtures.')
        return
    snapshot = request('/wtt-seasonal/snapshot', session=root)
    check(snapshot['ActiveMode']=='seasonal' and snapshot['EffectiveProfileId']==child, 'Active character survives server restart')
    check(snapshot['State']['Revision']==state['revision'], 'Selection revision survives restart')
    child_snapshot = request('/wtt-seasonal/snapshot', session=child)
    check(child_snapshot==snapshot, 'Launching seasonal profile resolves its original account')
    check(bool(request('/wtt-seasonal/switch', {'Mode':'normal'}, root).get('Error')), 'Raid marker blocks account switching after restart')
    mutation = {'PerkIds':state['selected'], 'ExpectedRevision':state['revision']}
    check(bool(request('/wtt-seasonal/edit', mutation, child).get('Error')), 'Raid marker blocks perk editing through seasonal identity')
    request('/client/game/start', session=child)
    edited = request('/wtt-seasonal/edit', mutation, child)
    check(not edited.get('Error'), 'Fresh solo client session clears interrupted raid marker')
    check(edited['State']['AppliedGrants']==snapshot['State']['AppliedGrants'], 'Restart and edit preserve grant receipts')
    seasonal = request('/client/game/profile/list', session=child)
    check(next(s['Progress'] for s in seasonal[0]['Skills']['Common'] if s['Id']=='Strength')==500, 'Reopening and editing never refill a previously granted preset')
    normal = request('/wtt-seasonal/switch', {'Mode':'normal'}, child)
    check(normal['EffectiveProfileId']==root, 'Seasonal identity switches back to the true normal profile')
    profile_after = request('/client/game/profile/list', session=root)
    baseline=json.loads((PROJECT/'Testing/normal-baseline.json').read_text(encoding='utf-8'))
    differences=[]
    def compare(a,b,path=''):
        if isinstance(a,dict) and isinstance(b,dict):
            for key in set(a)|set(b):compare(a.get(key),b.get(key),path+'/'+key)
        elif isinstance(a,list) and isinstance(b,list) and len(a)==len(b):
            for i,(x,y) in enumerate(zip(a,b)):compare(x,y,path+'/'+str(i))
        elif a!=b: differences.append({'path':path,'before':a,'after':b})
    compare(baseline,profile_after)
    (PROJECT/'Testing/restart-differences.json').write_text(json.dumps(differences,indent=2))
    def core_housekeeping(change):
        if change['path']=='/0/Hideout/sptUpdateLastRunTimestamp':
            return isinstance(change['after'],int) and change['after'] >= (change['before'] or 0)
        for index,area in enumerate(baseline[0]['Hideout']['Areas']):
            if area['type']==4 and area['level']==0 and not any(slot.get('items') for slot in area.get('slots',[])):
                if change['path']==f'/0/Hideout/Areas/{index}/active':
                    return change['before'] is True and change['after'] is False
        return False
    check(all(core_housekeeping(change) for change in differences), 'Restart preserves normal PMC and Scav except verified SPT hideout housekeeping')
    (PROJECT/'Research/restart-results.json').write_text(json.dumps({'checks':checks,'passed':len(checks),'coreHousekeepingPaths':[d['path'] for d in differences]},indent=2))
    print('Passed',len(checks),'restart/recovery checks.')

if __name__ == '__main__':
    main()
