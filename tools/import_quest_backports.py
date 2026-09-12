"""Offline, conservative quest compatibility compiler. Never accesses player captures or saves."""
import argparse
import copy
import hashlib
import json
import re
import struct
from pathlib import Path
from import_trader_progression import PROJECT, read, write, latest

CAMPAIGN = '69e232a764dfe95549003f0f'
SUPPORTED = {'Quest', 'Level', 'TraderLoyalty', 'TraderStanding', 'CounterCreator', 'Kills',
             'Location', 'Equipment', 'FindItem', 'HandoverItem', 'CompleteCondition',
             'VisitPlace', 'InZone', 'LeaveItemAtLocation', 'PlaceBeacon', 'ExitStatus', 'ExitName', 'LaunchFlare'}
SPATIAL = {'VisitPlace', 'InZone', 'LeaveItemAtLocation', 'PlaceBeacon', 'LaunchFlare', 'ExitName'}
STAGES = {'conditions': {'AvailableForStart', 'AvailableForFinish', 'Fail'},
          'rewards': {'Started', 'Success', 'Fail'}}


def normalize_stages(quest):
    """Omit empty capture-only stages; never discard conditions or rewards."""
    for name, supported in STAGES.items():
        for stage, entries in list(quest[name].items()):
            if not isinstance(entries, list) or stage not in supported and entries:
                raise ValueError('Unsupported quest stage: ' + name + '/' + stage)
            if stage not in supported:
                del quest[name][stage]


def nodes(value, path=''):
    if isinstance(value, dict):
        yield path, value
        for key, child in value.items():
            # props duplicates the condition's display metadata, not another objective.
            if key != 'props':
                yield from nodes(child, path + '/' + key)
    elif isinstance(value, list):
        for i, child in enumerate(value):
            yield from nodes(child, path + '/' + str(i))


def strings(value):
    if isinstance(value, str):
        return [value]
    if isinstance(value, list):
        return [s for v in value for s in strings(v)]
    return []


def signature(items, root):
    item = next(i for i in items if i['_id'] == root)
    return (item['_tpl'], tuple(sorted((i.get('slotId', ''), signature(items, i['_id']))
            for i in items if i.get('parentId') == root)))


def dependencies(quest):
    return sorted({s for _, n in nodes(quest.get('conditions', {}))
                   if n.get('conditionType') == 'Quest' for s in strings(n.get('target'))})


def propagate(candidates, native, records):
    """All quest references require an available definition, including Fail/finish references."""
    changed = True
    while changed:
        changed = False
        for qid in sorted(candidates):
            record = records[qid]
            if record['Blockers']:
                continue
            for dep in dependencies(candidates[qid]):
                if dep in native:
                    continue
                blocked = records.get(dep)
                if blocked and blocked.get('CampaignId') and blocked.get('CampaignId') != record.get('CampaignId'):
                    record['Blockers'].append(dict(Code='campaign-dependency', Detail='Dependency belongs to another campaign: ' + dep, Path=[qid, dep]))
                    changed = True
                    break
                if blocked is None or blocked['Blockers']:
                    tail = blocked['Blockers'][0] if blocked else {'Code': 'missing-definition', 'Path': [dep]}
                    record['Blockers'].append(dict(Code='dependency', Detail=dep + ': ' + tail['Code'],
                                                   Path=[qid] + tail.get('Path', [dep])))
                    changed = True
                    break


class Inventory:
    def __init__(self, database, game):
        self.database, self.game = database, game
        self.sources = {}
        self.native = self.load(database / 'templates/quests.json')
        self.items = self.load(database / 'templates/items.json')
        self.locale = self.load(database / 'locales/global/en.json')
        self.traders = {p.parent.name: self.load(p) for p in sorted((database / 'traders').glob('*/assort.json'))}
        self.maps = set()
        for p in sorted((database / 'locations').glob('*/base.json')):
            base = self.load(p)
            if base.get('Enabled'):
                self.maps.update([base.get('Id', ''), base.get('_Id', ''), p.parent.name])
        self.obtainable = {i['_tpl'] for a in self.traders.values() for i in a['items']}
        self.lootable = set()
        for p in sorted((database / 'locations').glob('*/looseLoot.json')):
            for _, n in nodes(self.load(p)):
                if '_tpl' in n:
                    self.lootable.add(n['_tpl'])
        static = database / 'loot/staticLoot.json'
        if static.exists():
            for _, n in nodes(self.load(static)):
                if 'tpl' in n:
                    self.lootable.add(n['tpl'])
        self.obtainable |= self.lootable
        self.roles = {'assault', 'marksman', 'pmcBEAR', 'pmcUSEC'}
        for p in sorted((database / 'locations').glob('*/base.json')):
            for _, n in nodes(read(p)):
                if n.get('BossChance', 0) > 0:
                    self.roles.update(strings(n.get('BossName')))
                    self.roles.update(strings(n.get('BossEscortType')))

    def load(self, path):
        self.sources[path.resolve().as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
        return read(path)

    def icon(self, route):
        stem = Path(route).stem
        for suffix in ('.png', '.jpg'):
            path = self.database.parent / 'images/quest/icon' / (stem + suffix)
            if path.is_file():
                self.sources[path.resolve().as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
                return path
        return None


def inspect(quest, inv, zones=None):
    zones = zones or {}
    blockers, evidence, remaps = [], [], []
    qid = quest['_id']
    def fail(code, detail):
        entry = dict(Code=code, Detail=detail, Path=[qid])
        if entry not in blockers:
            blockers.append(entry)
    if quest['traderId'] not in inv.traders:
        fail('trader', quest['traderId'])
    for name, supported in STAGES.items():
        for stage, entries in quest.get(name, {}).items():
            if not isinstance(entries, list) or stage not in supported and entries:
                fail('quest-stage', name + '/' + stage + ': unsupported or malformed stage')
            elif stage not in supported:
                evidence.append(dict(OmittedEmptyStage=name + '/' + stage,
                                     Reason='Empty capture-only stage is not supported by the installed client'))
    if quest.get('isStoryQuest') or quest.get('inBufferZoneOnly') or quest.get('dialogueId'):
        fail('story-runtime', 'Requires captured story/dialogue state or buffer-zone interaction')
    if quest.get('acceptanceAndFinishingSource', 'eft') != 'eft' or quest.get('progressSource', 'eft') != 'eft':
        fail('external-progression', 'Progress or hand-in occurs outside EFT')
    if not quest.get('conditions', {}).get('AvailableForFinish'):
        fail('empty-objectives', 'No independently completable objective')
    locale = quest.get('localization', {}).get('en', {})
    if not locale.get(quest.get('name')) or not locale.get(quest.get('description')):
        fail('localization', 'Missing English quest title or description')
    icon = inv.icon(quest.get('image', ''))
    if not icon:
        fail('icon', 'No verified installed icon for ' + quest.get('image', ''))
    condition_ids = {n['id'] for _, n in nodes(quest['conditions']) if 'conditionType' in n and 'id' in n}
    for path, n in nodes(quest['conditions']):
        kind = n.get('conditionType')
        if not kind:
            continue
        if kind not in SUPPORTED:
            fail('condition', path + ': unsupported ' + kind)
            continue
        if kind == 'CompleteCondition' and any(t not in condition_ids for t in strings(n.get('target'))):
            fail('condition-reference', path + ': dangling visibility target')
        if kind in SPATIAL:
            targets = strings(n.get('zoneIds') or n.get('zoneId') or n.get('target'))
            for target in targets:
                if (kind, target) not in zones:
                    fail('unverified-client-zone', path + ': ' + kind + ' ' + target)
                else:
                    evidence.append(zones[kind, target])
        if kind == 'Location' and any(t not in inv.maps for t in strings(n.get('target'))):
            fail('map', path + ': ' + str(n.get('target')))
        if kind == 'Kills':
            if n.get('target') not in ('Any', 'AnyPmc', 'Savage', 'Usec', 'Bear'):
                fail('kill-target', path + ': ' + str(n.get('target')))
            if any(r not in inv.roles for r in n.get('savageRole', [])):
                fail('encounter', path + ': ' + str(n['savageRole']))
            for field in ('weapon', 'weaponCaliber', 'bodyPart'):
                choices = n.get(field, [])
                if field == 'weapon' and choices:
                    available = sorted(set(choices) & inv.obtainable)
                    if not available:
                        fail('equipment-acquisition', path + ': no obtainable weapon alternative')
                    else:
                        evidence.append(dict(Condition=n['id'], ObtainableWeaponAlternatives=available))
        if kind in ('FindItem', 'HandoverItem', 'LeaveItemAtLocation', 'PlaceBeacon'):
            targets = strings(n.get('target'))
            available = inv.lootable if n.get('onlyFoundInRaid') else inv.obtainable
            if any(inv.items.get(t, {}).get('_props', {}).get('QuestItem') for t in targets):
                fail('quest-item-placement', path + ': original placement unverified')
            if not set(targets) & available:
                fail('item-acquisition', path + ': no verified obtainable target: ' + ', '.join(targets))
        if kind == 'Equipment':
            # Inclusive outer list is OR; each inner list is one complete outfit.
            outfits = n.get('equipmentInclusive', [])
            if outfits and not any(all(t in inv.obtainable for t in group) for group in outfits):
                fail('equipment-acquisition', path + ': no complete obtainable outfit')
        if kind in ('TraderLoyalty', 'TraderStanding') and n.get('target') not in inv.traders:
            fail('trader', path + ': ' + str(n.get('target')))
        # New non-neutral semantics must not be silently discarded by the old client.
        for field in ('isEncoded', 'isNecessary', 'isNotGroupProgress', 'isResetOnConditionFailed', 'completeInSeconds'):
            if n.get(field):
                fail('condition-semantics', path + ': ' + field)
    for stage, rewards in quest['rewards'].items():
        if not isinstance(rewards, list):
            continue
        for r in rewards:
            kind = r['type']
            if kind not in ('Experience', 'Item', 'TraderStanding', 'Skill', 'AssortmentUnlock'):
                fail('reward', stage + ': unsupported ' + kind)
                continue
            if r.get('unknown') or r.get('isEncoded'):
                fail('reward-semantics', r['id'])
            for item in r.get('items', []):
                if item['_tpl'] not in inv.items:
                    fail('reward-item', item['_tpl'])
            if kind == 'TraderStanding' and r['target'] not in inv.traders:
                fail('reward-trader', r['target'])
            if kind == 'Skill':
                known = {reward['target'] for q in inv.native.values() for rs in q['rewards'].values()
                         for reward in rs if reward['type'] == 'Skill'}
                if r['target'] not in known:
                    fail('reward-skill', r['target'])
            if kind == 'AssortmentUnlock':
                assort = inv.traders.get(r.get('traderId'), {})
                try:
                    wanted = signature(r['items'], r['target'])
                    matches = [i['_id'] for i in assort.get('items', [])
                               if i['_id'] in assort.get('barter_scheme', {})
                               and assort['loyal_level_items'].get(i['_id']) == r['loyaltyLevel']
                               and signature(assort['items'], i['_id']) == wanted]
                except (StopIteration, KeyError):
                    matches = []
                # Existing purchase gates must not be made stricter or overwritten by a new quest.
                if len(matches) != 1:
                    fail('reward-offer', r['id'] + ': missing or ambiguous matching installed offer')
                else:
                    # A captured unlock must also have an existing matching questassort binding.
                    qa = inv.load(inv.database / 'traders' / r['traderId'] / 'questassort.json')
                    if qa.get('success', {}).get(matches[0]) != qid:
                        fail('reward-offer-gate', r['id'] + ': installed offer has no matching quest unlock')
                    else:
                        remaps.append(dict(Reward=r['id'], Target=matches[0]))
    return blockers, evidence, icon, remaps


def verify_client_zones(candidates, records, inv):
    """Inspect actual scene components, never treat text in an asset as proof of a trigger."""
    spatial = {qid:q for qid,q in candidates.items() if records[qid]['Blockers'] and
               all(b['Code'] == 'unverified-client-zone' for b in records[qid]['Blockers'])}
    targets = {target for q in spatial.values() for _,n in nodes(q['conditions']) if n.get('conditionType') in SPATIAL
               for target in strings(n.get('zoneIds') or n.get('zoneId') or n.get('target'))}
    if not targets:
        return {}, dict(CheckedTargets=[], Verified=[], Unverified=[])
    import UnityPy
    from recover_ui import Generator
    pattern = re.compile(b'|'.join(re.escape(t.encode()) for t in sorted(targets)))
    data = inv.game / 'EscapeFromTarkov_Data'
    scripts_path = data / 'globalgamemanagers.assets'
    scripts = next(iter(UnityPy.load(str(scripts_path)).files.values())).objects
    inv.sources[scripts_path.resolve().as_posix()] = hashlib.sha256(scripts_path.read_bytes()).hexdigest()
    found, references, scene_hashes = {}, {}, []
    generator = None
    for path in sorted(data.glob('level*')):
        if path.suffix or not path.is_file():
            continue
        raw = path.read_bytes()
        scene_hashes.append((path.name, hashlib.sha256(raw).hexdigest()))
        if not pattern.search(raw):
            continue
        env = UnityPy.load(str(path))
        asset = next(iter(env.files.values()))
        for obj in env.objects:
            if obj.type.name != 'MonoBehaviour':
                continue
            raw_object = obj.get_raw_data()
            matches = {m.group().decode() for m in pattern.finditer(raw_object)}
            if not matches:
                continue
            for target in matches:
                references.setdefault(target, []).append(dict(Scene=path.name, ObjectId=obj.path_id))
            try:
                file_id, script_id = struct.unpack_from('<iq', raw_object, 16)
                if file_id < 1 or Path(asset.externals[file_id-1].path).name != scripts_path.name:
                    continue
                script = scripts[script_id].read()
                # This serialized component has a verified native event producer. Other
                # trigger classes require their own field/event verification before acceptance.
                if script.m_ClassName != 'FlareShootDetectorZone':
                    continue
                if generator is None:
                    generator = Generator(asset.unity_version)
                    for dll in [data/'Managed'/name for name in ('mscorlib.dll','UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll')] + [inv.game/'BepInEx/DumpedAssemblies/EscapeFromTarkov/Assembly-CSharp.dll']:
                        contents = dll.read_bytes()
                        inv.sources[dll.resolve().as_posix()] = hashlib.sha256(contents).hexdigest()
                        generator.load_dll(contents)
                fields = obj.read_typetree(generator.get_nodes_up(script.m_AssemblyName, script.m_Namespace+'.'+script.m_ClassName))
                target = fields.get('zoneID')
                if target not in matches or fields.get('m_Enabled') != 1 or not fields.get('_triggerHandlers'):
                    continue
                go = fields['m_GameObject']
                if go['m_FileID'] != 0 or not asset.objects[go['m_PathID']].read().m_IsActive:
                    continue
                proof = dict(ConditionType='LaunchFlare', Target=target, Scene=path.name,
                             SceneSha256=hashlib.sha256(raw).hexdigest(), ObjectId=obj.path_id,
                             Component=script.m_Namespace+'.'+script.m_ClassName, Field='zoneID')
                found['LaunchFlare', target] = proof
            except (ValueError, KeyError, IndexError, TypeError):
                # A failed component decode is an unverified zone, never a permissive fallback.
                continue
    scan = dict(CheckedTargets=sorted(targets), SceneInventorySha256=hashlib.sha256(json.dumps(scene_hashes).encode()).hexdigest(),
                SceneCount=len(scene_hashes), References=references, Verified=list(found.values()),
                Unverified=sorted(targets-{key[1] for key in found}))
    return found, scan


def compile_backports(dump, database, game):
    inv = Inventory(database, game)
    pve_file = latest(dump / 'gw-pve.escapefromtarkov.com/client', 'quest/list')
    season_file = latest(dump / 'gw-pvp-season.escapefromtarkov.com/client', 'quest/list')
    pve, seasonal = inv.load(pve_file), inv.load(season_file)
    captures = {q['_id']: q for q in seasonal}
    captures.update({q['_id']: q for q in pve})
    ordinary = {q['_id'] for q in pve}
    candidates = {qid: q for qid, q in captures.items() if qid not in inv.native}
    records, icons = {}, {}
    for qid, q in sorted(candidates.items()):
        blocked, evidence, icon, remaps = inspect(q, inv)
        records[qid] = dict(Name=q.get('localization', {}).get('en', {}).get(q['name'], '') or qid,
            CampaignId=None if qid in ordinary else CAMPAIGN, Source=(pve_file if qid in ordinary else season_file).name,
            Dependencies=dependencies(q), Blockers=blocked, Evidence=evidence, RewardRemaps=remaps)
        if icon:
            icons[qid] = icon
    zones, zone_audit = verify_client_zones(candidates, records, inv)
    if zones:
        for qid,q in candidates.items():
            if any(b['Code'] == 'unverified-client-zone' for b in records[qid]['Blockers']):
                blocked, evidence, _, remaps = inspect(q, inv, zones)
                records[qid].update(Blockers=blocked, Evidence=evidence, RewardRemaps=remaps)
    propagate(candidates, inv.native, records)
    # A remaining dependency cycle needs a state-aware adapter; do not invent one.
    def cycle(qid, stack):
        if qid in stack:
            return stack[stack.index(qid):] + [qid]
        for dep in dependencies(candidates[qid]):
            if dep in candidates and not records[dep]['Blockers']:
                found = cycle(dep, stack + [qid])
                if found:
                    return found
        return None
    for qid in candidates:
        if not records[qid]['Blockers']:
            found = cycle(qid, [])
            if found:
                records[qid]['Blockers'].append(dict(Code='dependency-cycle', Detail=' -> '.join(found), Path=found))
    propagate(candidates, inv.native, records)
    accepted = []
    for qid, record in records.items():
        record['Status'] = 'skipped' if record['Blockers'] else 'accepted'
        if record['Blockers']:
            continue
        definition = copy.deepcopy(candidates[qid])
        definition.pop('status', None)
        normalize_stages(definition)
        definition['image'] = '/wtt-campaigns/quest-icons/' + qid + icons[qid].suffix
        for remap in record['RewardRemaps']:
            for rewards in definition['rewards'].values():
                for reward in rewards:
                    if reward['id'] == remap['Reward']:
                        old = reward['target']
                        reward['target'] = remap['Target']
                        for item in reward['items']:
                            if item['_id'] == old:
                                item['_id'] = remap['Target']
                            if item.get('parentId') == old:
                                item['parentId'] = remap['Target']
        accepted.append(dict(CampaignId=record['CampaignId'], Quest=definition,
                             Icon='data/quest-icons/' + qid + icons[qid].suffix,
                             IconSha256=hashlib.sha256(icons[qid].read_bytes()).hexdigest()))
    manifest = dict(Version=1, Sources=[dict(File=k, Sha256=v) for k, v in sorted(inv.sources.items())],
        Counts=dict(Candidates=len(candidates), Accepted=len(accepted), Skipped=len(candidates)-len(accepted)),
        Quests=accepted, Audit=records, ClientZones=zone_audit)
    return manifest, icons


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    manifest, icons = compile_backports(PROJECT.parent / '1.0 Dump', PROJECT.parents[1] / 'SPT_Runtime/SPT_Data/database', PROJECT.parents[1])
    output = PROJECT / 'data/quest-backports.json'
    if args.check:
        assert read(output) == manifest, 'Quest backport manifest is not reproducible'
        for entry in manifest['Quests']:
            assert hashlib.sha256((PROJECT / entry['Icon']).read_bytes()).hexdigest() == entry['IconSha256']
    else:
        write(output, manifest)
        for entry in manifest['Quests']:
            path = PROJECT / entry['Icon']
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(icons[entry['Quest']['_id']].read_bytes())
    print(json.dumps(manifest['Counts']))
    print('Accepted:', [(e['Quest']['_id'], manifest['Audit'][e['Quest']['_id']]['Name']) for e in manifest['Quests']])


if __name__ == '__main__':
    main()
