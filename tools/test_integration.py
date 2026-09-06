"""Exercise real SPT routes against the isolated server made by start_test_server.ps1.

Creates synthetic accounts only. Never connects to the installed server on 6969.
"""
import hashlib,json,ssl,struct,time,urllib.request,zlib
from pathlib import Path

PROJECT=Path(__file__).resolve().parents[1]
SERVER=PROJECT/'Testing/Server'
BASE='https://127.0.0.1:6975'
CONTEXT=ssl._create_unverified_context() # The isolated SPT server uses its own self-signed certificate.
checks=[]
def check(condition,label):
    assert condition,label
    checks.append(label)

def request(path,payload=None,session=None,raw=False,allow_error=False):
    headers={'Content-Type':'application/json'}
    if session:headers['Cookie']='PHPSESSID='+session
    body=zlib.compress(json.dumps(payload or {}).encode()) if not raw else None
    response=urllib.request.urlopen(urllib.request.Request(BASE+path,data=body,headers=headers),context=CONTEXT,timeout=40).read()
    if raw:return response
    try:response=zlib.decompress(response)
    except zlib.error:pass
    if not response:raise RuntimeError('Empty server response: '+path)
    value=json.loads(response)
    if 'err' in value:
        if allow_error:return value
        assert value['err']==0,value.get('errmsg')
        return value.get('data')
    return value

def check_visual(snapshot, pmc, mode):
    visual = next(c['Visual'] for c in snapshot['Characters'] if c['Mode'] == mode)
    check(set(visual) == {'Info', 'Customization', 'Equipment'}, mode + ' appearance-only response')
    check(visual['Info'] == {key: pmc['Info'][key] for key in ['Nickname', 'Level', 'Side']},
          mode + ' typed appearance info preserves the native contract')
    check(visual['Customization'] == pmc['Customization'], mode + ' native customization preserved')
    equipment = pmc['Inventory']['equipment']
    visible = {equipment}
    while True:
        descendants = {item['_id'] for item in pmc['Inventory']['items'] if item.get('parentId') in visible}
        if descendants <= visible:
            break
        visible.update(descendants)
    expected = [item for item in pmc['Inventory']['items'] if item['_id'] in visible]
    check(visual['Equipment'] == {'Id': equipment, 'Items': expected},
          mode + ' complete native equipment data preserved without stash items')

def main():
    username='season-test-'+str(int(time.time()))
    registered=request('/launcher/v2/register',{'username':username,'edition':'Standard'})
    check(registered['Response'],'Register synthetic account')
    root=next(p['profileId'] for p in registered['Profiles'] if p['username']==username)
    templates=json.loads((SERVER/'SPT_Data/database/templates/profiles.json').read_text())
    customization=json.loads((SERVER/'SPT_Data/database/templates/customization.json').read_text())
    def cosmetic(parent):return sorted(k for k,v in customization.items() if v.get('_parent')==parent and v['_props'].get('AvailableAsDefault') and 'Usec' in v['_props'].get('Side',[]))[0]
    request('/client/game/profile/create',{'side':'Usec','nickname':'NormalTest','headId':cosmetic('5cc085e214c02e000c6bea67'),'voiceId':cosmetic('5fc100cf95572123ae738483')},root)
    normal=request('/client/game/profile/list',session=root)
    snapshot=request('/seasonal-perks/snapshot',session=root)
    check_visual(snapshot, normal[0], 'normal')
    check(snapshot['ActiveMode']=='normal','Normal character initially active')
    catalogue=snapshot['Catalogue'];all_perks=catalogue['common']+catalogue['personal']
    check(len(all_perks)==39,'39 catalogue entries served')
    check(all(p['imageUrl'].startswith('/seasonal-perks/icons/') for p in all_perks),'Every artwork URL is local')
    for perk in all_perks:
        png=request(perk['imageUrl'],raw=True)
        check(png[:8]==b'\x89PNG\r\n\x1a\n' and struct.unpack('>II',png[16:24])==(272,272),'Icon '+perk['id'])
    names={snapshot['Locale'][p['id']+' name']:p['id'] for p in catalogue['personal']}
    selected=[names[n] for n in ['Polydipsia','Chronic Fatigue Syndrome','Exhaustion','Hercules']]
    request_data={'PerkIds':selected,'Nickname':'SeasonTest','Side':'Usec','ExpectedRevision':0}
    created=request('/seasonal-perks/create',request_data,root)
    check(not created.get('Error'),'Create isolated seasonal character: '+str(created.get('Error')))
    check(created['State']['Revision']==1,'Initial selection persisted')
    check(set(selected)<=set(created['State']['SeasonalPerks']),'All selected perks persisted')
    check(created['ActiveMode']=='normal','Creation leaves normal character selected')
    switched=request('/seasonal-perks/switch',{'Mode':'seasonal'},root)
    check(not switched.get('Error'),'Switch to seasonal character')
    child=switched['EffectiveProfileId']
    check(child!=root,'Independent session identities')
    seasonal=request('/client/game/profile/list',session=child)
    check_visual(switched, seasonal[0], 'seasonal')
    check(seasonal[0]['Info']['Nickname']=='SeasonTest','Seasonal session loads seasonal PMC')
    skills={s['Id']:s['Progress'] for s in seasonal[0]['Skills']['Common']}
    check(skills['Strength']==1500 and skills['Endurance']==1500,'Hercules creates level 15 Strength and Endurance')
    check(skills['Crafting']==5100,'Handyman creates Crafting level 51')
    equipment=next(i['_id'] for i in seasonal[0]['Inventory']['items'] if i.get('parentId')==seasonal[0]['Inventory']['equipment'])
    insurance=request('/client/game/profile/items/moving',{'data':[{'Action':'Insure','tid':'54cb50c76803fa8b248b4571','items':[equipment]}]},child,allow_error=True)
    check(insurance['err']!=0 and 'Insurance is disabled' in insurance.get('errmsg',''), 'Seasonal insurance transaction returns a client-visible rejection')
    after_insurance=request('/client/game/profile/list',session=child)
    check(after_insurance[0]['Inventory']==seasonal[0]['Inventory'] and after_insurance[0]['InsuredItems']==seasonal[0]['InsuredItems'], 'Rejected insurance neither charges money nor insures items')
    normal_items={i['_id'] for i in normal[0]['Inventory']['items'] if i.get('parentId')}
    seasonal_items={i['_id'] for i in seasonal[0]['Inventory']['items'] if i.get('parentId')}
    check(not normal_items.intersection(seasonal_items),'Independent inventory item IDs')
    check(seasonal[1]['_id']!=normal[1]['_id'],'Independent Scav IDs')
    bad=request('/seasonal-perks/edit',{'PerkIds':[names['Hercules']],'ExpectedRevision':1},root)
    check(bool(bad.get('Error')),'Reject insufficient budget')
    bad=request('/seasonal-perks/edit',{'PerkIds':[names['Lucky']],'ExpectedRevision':1},root)
    check(bool(bad.get('Error')),'Reject unsupported perk')
    bad=request('/seasonal-perks/edit',{'PerkIds':[names['Hemophilia'],names['Thrombophilia']],'ExpectedRevision':1},root)
    check(bool(bad.get('Error')),'Reject mutually exclusive perks on the server')
    bad=request('/seasonal-perks/edit',{'PerkIds':selected,'ExpectedRevision':0},root)
    check(bool(bad.get('Error')),'Reject stale edit')
    edited=request('/seasonal-perks/edit',{'PerkIds':selected,'ExpectedRevision':1},root)
    check(not edited.get('Error') and edited['State']['Revision']==2,'Save valid edit')
    check(edited['State']['AppliedGrants']==created['State']['AppliedGrants'],'Edits do not duplicate grant receipts')
    for mode in ['normal','seasonal','normal']:
        result=request('/seasonal-perks/switch',{'Mode':mode},root)
        check(result['ActiveMode']==mode,'Repeated switch to '+mode)
    after=request('/client/game/profile/list',session=root)
    differences=[]
    if after != normal:
        def compare(a,b,path=''):
            if isinstance(a,dict) and isinstance(b,dict):
                for key in set(a)|set(b):compare(a.get(key),b.get(key),path+'/'+key)
            elif isinstance(a,list) and isinstance(b,list) and len(a)==len(b):
                for i,(x,y) in enumerate(zip(a,b)):compare(x,y,path+'/'+str(i))
            elif a!=b:differences.append({'path':path,'before':a,'after':b})
        compare(normal,after)
        (PROJECT/'Testing/integration-differences.json').write_text(json.dumps(differences,indent=2),encoding='utf-8')
    def core_housekeeping(change):
        if change['path']=='/0/Hideout/sptUpdateLastRunTimestamp':
            return isinstance(change['after'],int) and change['after'] >= (change['before'] or 0)
        for index,area in enumerate(normal[0]['Hideout']['Areas']):
            if area['type']==4 and area['level']==0 and not any(slot.get('items') for slot in area.get('slots',[])):
                if change['path']==f'/0/Hideout/Areas/{index}/active':
                    return change['before'] is True and change['after'] is False
        return False
    check(all(core_housekeeping(change) for change in differences),'Normal PMC and Scav unchanged except verified SPT hideout housekeeping')
    (PROJECT/'Testing/restart-state.json').write_text(json.dumps({'root':root,'child':child,'selected':selected,'revision':2,'normalHash':hashlib.sha256(json.dumps(normal,sort_keys=True).encode()).hexdigest()}))
    (PROJECT/'Testing/normal-baseline.json').write_text(json.dumps(normal),encoding='utf-8')
    (PROJECT/'Research/integration-results.json').write_text(json.dumps({'checks':checks,'passed':len(checks)},indent=2))
    print('Passed',len(checks),'real-server integration checks.')

if __name__=='__main__':main()
