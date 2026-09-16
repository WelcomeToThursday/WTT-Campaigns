"""Compile the released KORD quests into an editable SPT campaign template.

Captured source files stay unchanged. Spatial coordinates come from export_kord_geometry.py.
"""
import copy
import argparse
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
def identity(name):
    return hashlib.sha256(('wtt-kord-copy:' + name).encode()).hexdigest()[:24]
def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))
def condition(kind, **fields):
    return dict(id=identity(json.dumps([kind, fields], sort_keys=True)), conditionType=kind, **fields)
def walk(value):
    if isinstance(value, dict):
        yield value
        for key, child in value.items():
            if key != 'props':
                yield from walk(child)
    elif isinstance(value, list):
        for child in value:
            yield from walk(child)


def main(check=False):
    quests = [q for q in read(ROOT / 'data/hub-quests.json') if q['localization']['en'].get(q.get('name'), '').startswith('[KORD BREACH]')]
    assert len(quests) == 12
    volumes = {v['Target']: v for v in read(ROOT / 'data/kord-geometry.json')['Volumes']}
    chapter = identity('chapter')
    roles = ['blackDivLead', 'blackDivAssault', 'blackDivBreacher', 'blackDivSupport', 'bossWedge', 'blackDivIb']
    result = dict(FormatVersion=9, Quests=quests, Zones=[], Items=[], QuestLoot=[], Crafts=[], TraderOffers=[],
                  Dependencies=['mod:com.blackdiv.tacticaltoaster', 'mod:com.wtt.contentbackport'],
                  Story=dict(FormatVersion=1, Chapters=[dict(Id=chapter, Name='KORD BREACH')], Quests=[], Variables=[], RaidBindings=[]))
    ids = {q['_id'] for q in quests}

    def item(tpl, source, name, width=1, height=1):
        result['Items'].append(dict(Id=tpl, CloneFrom=source, Name=name, Description='Campaign quest equipment.', Width=width, Height=height, StackMax=1))

    def zone(target, use, label=None):
        v = volumes[target]
        z = {k: copy.deepcopy(v[k]) for k in ['Location', 'Scene', 'Position', 'Rotation', 'Size']}
        z.update(Id=identity(target), Name=label or target, Uses=[use])
        if use == 'LeaveItemAtLocation':
            z['RequiredQuestId'] = '6a4f5a3d6bfb56d6de0b2dd8'
        result['Zones'].append(z)
        return z

    def salvage(target, template, label):
        z = zone(target, 'Salvage', label)
        z['Id'] = identity('salvage:' + target)
        # Recover the same camera/case position using the native timed recovery interaction.
        z['Size'] = dict(X=2.5, Y=2.5, Z=2.5)
        z['RequiredQuestId'] = '6a4f528221e50974ab006f6f' if template == '6a671d87ff6f8a0b080b6bf3' else '6a4f5c207368e70cae00e6ec'
        z['Salvage'] = dict(Recovery=True, RequiredItemTpl='544fb5454bdc2df8738b456a', SalvageTime=5,
                            ConsumeRequiredItem=False, Rewards=[dict(ItemTpl=template, Count=1, ToQuestInventory=False)])

    cameras = ['6a5f857dd29aef042a02380e', '6a5f85ae7270df443807f9fb', '6a5f85c12f90b1312600ada4',
               '6a5f85d4add9519187071c13', '6a5f862a7d87cb689d010883', '6a5f86176f882e6f720651c9']
    for index, tpl in enumerate(cameras, 1):
        item(tpl, '5b4391a586f7745321235ab2', f'Recovered KORD surveillance camera {index}')
        salvage(f'kord_breach_wifi_{index:02}', tpl, f'Recover surveillance camera {index} (multitool)')
    case = '6a671d87ff6f8a0b080b6bf3'
    item(case, '6740987b89d5e1ddc603f4f0', 'Case with military equipment', 2, 2)
    salvage('shoreline_kord_ges', case, 'Recover military equipment case (multitool)')
    item('6a6692d4beccfa243d0bb575', '590c621186f774138d11ea29', 'Decrypted Black Division laptop data')
    item('6a3563dacdaebb512e0a009c', '6740987b89d5e1ddc603f4f0', 'Briefcase with Black Division documents', 2, 2)

    for q in quests:
        q['_seasonalEnabled'] = True
        q['isStoryQuest'] = False
        q['inBufferZoneOnly'] = False
        q['dialogueId'] = None
        q['acceptanceAndFinishingSource'] = 'eft'
        q['progressSource'] = 'eft'
        q['image'] = '/wtt-campaigns/quest-icons/6a5cd2178fd7c2b201032f3f'
        q['conditions'].pop('AutoStart', None)
        # The old story completion gate treats false as optional. These captured
        # objectives are required tasks, not optional scouting/kill bonuses.
        for objective in q['conditions']['AvailableForFinish']:
            objective['isNecessary'] = True
        q['conditions']['AvailableForStart'] = [c for c in q['conditions']['AvailableForStart']
                                               if c['conditionType'] != 'Quest' or c.get('target') in ids]
        for stage in list(q['rewards']):
            if stage not in ['Started', 'Success', 'Fail']:
                assert not q['rewards'][stage]
                del q['rewards'][stage]
        for rewards in q['rewards'].values():
            for reward in rewards:
                reward.pop('availableInGameEditions', None)
                reward.pop('gameMode', None)
                if reward['type'] == 'AssortmentUnlock':
                    # Backport 2.0 Skier QBZ-191 cash price; the captured quest unlocks LL3.
                    result['TraderOffers'].append(dict(Id=reward['target'], Name='KORD QBZ-191 unlock',
                        TraderId=reward['traderId'], Items=copy.deepcopy(reward['items']),
                        Barter=[[dict(_tpl='5449016a4bdc2d6f028b456f', count=298811)]],
                        Loyalty=reward['loyaltyLevel'], RequiresUnlock=True, UnlockQuestId=q['_id'],
                        UnlimitedStock=True, PurchaseLimit=2))
        result['Story']['Quests'].append(dict(QuestId=q['_id'], ChapterId=chapter, AutoStart=q['_id']=='6a4f53f6be2cd3a87c0b708d'))
        # The old client's BTR does not expose the 1.0 task hand-in. Prapor acts as courier.
        if q['_id'] == '6a4f53f6be2cd3a87c0b708d':
            q['traderId'] = '54cb50c76803fa8b248b4571'
            q['localization']['en'][q['description']] = 'Prapor is handling the delivery to the BTR driver. Recover the case at the hydroelectric power station with a Leatherman multitool, then hand it to Prapor.'
        if q['_id'] == '6a4f528221e50974ab006f6f':
            q['localization']['en'][q['description']] += '\n\nSPT: recover the case at the hydroelectric station with a Leatherman multitool. Prapor will forward the package to the BTR driver.'
        if q['_id'] == '6a4f5c207368e70cae00e6ec':
            q['localization']['en'][q['description']] += '\n\nBring a Leatherman multitool. Recover each camera at its original planting location, then hand the recovered equipment to Prapor.'
        for n in walk(q['conditions']):
            n.pop('props', None)
            if 'savageRole' in n and n['savageRole']:
                n['savageRole'] = roles[:]
            if n.get('conditionType') == 'Location':
                n['target'] = [v for v in n['target'] if v != 'Sandbox_start']
            if n.get('conditionType') == 'VisitPlace' and n.get('target') in volumes:
                z = zone(n['target'], 'VisitPlace')
                n['target'] = z['Id']
            if n.get('conditionType') == 'LeaveItemAtLocation':
                z = zone(n.get('zoneId') or n['zoneIds'][0], 'LeaveItemAtLocation')
                n['zoneId'] = z['Id']
                n.pop('zoneIds', None)
            if n.get('conditionType') == 'SearchAndDestroy':
                target = n['target'][0]
                z = zone(target, 'Shoot', 'Black Division radio relay')
                variable = identity('destroyed:' + target)
                result['Story']['Variables'].append(dict(Id=variable, Scope='Profile', InitialValue=0))
                result['Story']['RaidBindings'].append(dict(Id=identity('binding:' + target), Name='Destroy radio relay',
                    Location=z['Location'], Kind='Shoot', ZoneId=z['Id'], Once=True, PersistOnDeath=True,
                    Condition=dict(Type='QuestStatus', Target=q['_id'], Status=['Started']),
                    Actions=[dict(Id=identity('action:' + target), Type='SetVariable', Target=variable, Value=1, Scope='Profile')]))
                n.update(conditionType='GlobalVariableValue', target=variable, value=1, compareMethod='>=')

    loot = [('6a4f5eed0aba4eefcd0b1cc7', '6a5f9cfcc409750b6e065592'),
            ('6a4f5f87c72b01b42201fc68', '6a3563dacdaebb512e0a009c'),
            ('6a4f5fda45f72a73f3089a8a', '6a5f9c49b8281c31a1062aea'),
            ('6a4f7a63e41198323708563b', '6a5f9c25e8c84d8b0602f22a')]
    for quest, template in loot:
        result['QuestLoot'].append(dict(QuestId=quest, ItemTemplate=template, BotRoles=roles))

    # Captured Digital Puzzle recipe, with its explicit owned quest unlock restored.
    result['Crafts'] = [dict(_id='6a673d7a4da15f1334018588', areaType=11, productionTime=900,
        endProduct='6a6692d4beccfa243d0bb575', count=1, locked=True, continuous=False, needFuelForAllProductionTime=False,
        requirements=[dict(type='QuestComplete', questId='6a4f7a63e41198323708563b'),
                      dict(type='Area', areaType=11, requiredLevel=1),
                      dict(type='Item', templateId='62a0a16d0b9d3c46de5b6e97', count=1),
                      dict(type='Item', templateId='6389c70ca33d8c4cdf4932c6', count=2),
                      dict(type='Item', templateId='6a5f9c25e8c84d8b0602f22a', count=1)])]
    output = json.dumps(result, indent=2, ensure_ascii=False) + '\n'
    path = ROOT / 'data/kord-copy.json'
    if check:
        if path.read_text(encoding='utf-8') != output:
            raise ValueError('The packaged KORD template does not match its source compiler.')
    else:
        path.write_text(output, encoding='utf-8')
    print(f'Compiled {len(quests)} released quests, {len(result["Zones"])} zones, {len(result["QuestLoot"])} bot loot rules and one craft.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    main(parser.parse_args().check)
