"""Construct stateful offline quest routes and repair regular-trader reputation bottlenecks."""
import copy
import hashlib
from decimal import Decimal, ROUND_CEILING
from import_trader_progression import read, PROJECT
from import_quest_backports import nodes, strings

REGULAR = ['54cb50c76803fa8b248b4571', '54cb57776803fa99248b456e', '58330581ace78e27b8b10cee',
           '5935c25fb3acc3127c3d8cd9', '5a7c2eca46aef81a7ca2145d', '5ac3b934156ae10c4430e83c', '5c0647fdd443bc2504c2d371']


def dec(value):
    return Decimal(str(value))


def compare(current, required, method):
    a, b = dec(current), dec(required)
    return {'>=': a >= b, '>': a > b, '<=': a <= b, '<': a < b, '=': a == b,
            '==': a == b, '!=': a != b}.get(method, False)


def overlay_quests(native, overlay):
    result = copy.deepcopy(native)
    for qid, spec in overlay['Quests'].items():
        q = result[qid]
        q['traderId'] = spec['TraderId']
        if spec['Tier']:
            q['conditions']['AvailableForStart'] = copy.deepcopy(spec['Start'])
        for stage in q['rewards'].keys() | spec['Reputation'].keys():
            q['rewards'][stage] = [r for r in q['rewards'].get(stage, []) if r['type'] != 'TraderStanding'] + copy.deepcopy(spec['Reputation'].get(stage, []))
    return result


class Route:
    def __init__(self, quests, thresholds, side, initially_unlocked, config=None):
        self.quests, self.thresholds, self.side = quests, thresholds, side
        self.standing = {t: Decimal(0) for t in thresholds}
        self.status, self.timers, self.steps = {}, {}, []
        self.unlocked = set(initially_unlocked)
        self.level, self.now = 1, 0
        self.config = config or {}

    def loyalty(self, tid):
        return max([1] + [i+1 for i, level in enumerate(self.thresholds.get(tid, []))
                          if self.level >= level['Level'] and self.standing.get(tid, 0) >= dec(level['Standing'])])

    def condition(self, c):
        kind, targets = c['conditionType'], strings(c.get('target'))
        if kind == 'Level':
            return compare(self.level, c['value'], c['compareMethod'])
        if kind == 'Quest':
            return any(self.status.get(t, 0) in c.get('status', []) and
                       self.now >= self.timers.get((t, self.status.get(t, 0)), 0) + c.get('availableAfter', 0)
                       for t in targets)
        if kind in ('TraderStanding', 'TraderLoyalty') and len(targets) == 1:
            return compare(self.standing.get(targets[0], 0) if kind == 'TraderStanding' else self.loyalty(targets[0]),
                           c['value'], c['compareMethod'])
        return False

    def fail_triggered(self, quest):
        # SPT Fail entries are alternatives. Non-quest raid failures are avoided by a successful route.
        return any(self.condition(c) for c in quest['conditions'].get('Fail', [])
                   if c['conditionType'] in ('Quest', 'TraderStanding', 'TraderLoyalty', 'Level'))

    def supported(self, q):
        qid = q['_id']
        if qid in self.config.get('eventQuests', {}) or qid in self.config.get('profileBlacklist', {}).get('Standard', []):
            return False
        if qid in self.config.get('profileWhitelist', {}) and 'standard' not in [v.lower() for v in self.config['profileWhitelist'][qid]]:
            return False
        if qid in self.config.get('bearOnlyQuests' if self.side == 'Usec' else 'usecOnlyQuests', []):
            return False
        if q.get('side', 'Pmc').lower() not in ('pmc', self.side.lower()):
            return False
        if q.get('wttSeasonalOnly') or q.get('isStoryQuest') or q.get('notDisplayedQuest') or q.get('inBufferZoneOnly') or not q['conditions'].get('AvailableForFinish'):
            return False
        for _, c in nodes(q['conditions']):
            if c.get('conditionType', '').startswith('Arena') or c.get('conditionType') in ('GlobalVariableValue', 'CompletableItem', 'LocationTrigger', 'SearchAndDestroy'):
                return False
        return True

    def grant(self, qid, stage, status):
        assert self.status.get(qid, 0) not in (4, 5), 'Terminal quests cannot grant twice'
        self.status[qid] = status
        self.timers[qid, status] = self.now
        delta = {}
        for r in self.quests[qid]['rewards'].get(stage, []):
            if r['type'] == 'TraderStanding':
                tid = r['target']
                amount = dec(r['value'])
                self.standing[tid] = self.standing.get(tid, Decimal(0)) + amount
                delta[tid] = delta.get(tid, Decimal(0)) + amount
            if r['type'] == 'TraderUnlock':
                self.unlocked.add(r['target'])
        self.steps.append(dict(QuestId=qid, Stage=stage, Level=self.level, Time=self.now,
                              Reputation={t: float(v) for t, v in sorted(delta.items())}))

    def run(self):
        eligible = {id:q for id,q in sorted(self.quests.items()) if self.supported(q)}
        # Level is earned through ordinary play, not inferred from quest XP. Each wait honors delays.
        delay = max([1] + [int(c.get('availableAfter', 0)) + int(c.get('dispersion', 0))
                          for q in eligible.values() for _,c in nodes(q['conditions'])]) + 1
        for level in range(1, 80):
            self.level = level
            changed = True
            while changed:
                changed = False
                self.now += delay
                for qid, q in eligible.items():
                    state = self.status.get(qid, 0)
                    if state in (4, 5):
                        continue
                    if self.fail_triggered(q):
                        if state == 2:
                            self.grant(qid, 'Fail', 5)
                            changed = True
                        continue
                    if state == 0 and q['traderId'] in self.unlocked and all(self.condition(c) for c in q['conditions']['AvailableForStart']):
                        self.grant(qid, 'Started', 2)
                        changed = True
                        state = 2
                    if state == 2:
                        # Native raid/item objectives are assumed successfully performed. Quest-state
                        # finish conditions are still evaluated and can never be short-circuited.
                        gates = [c for c in q['conditions']['AvailableForFinish'] if c['conditionType'] in ('Quest', 'Level', 'TraderLoyalty', 'TraderStanding')]
                        if all(self.condition(c) for c in gates):
                            self.grant(qid, 'Success', 4)
                            changed = True
        return self

    def report(self):
        return dict(Side=self.side, Steps=self.steps, Traders={t: dict(Standing=float(self.standing.get(t, 0)),
                    Loyalty=self.loyalty(t), Required=self.thresholds[t][-1]['Standing']) for t in REGULAR})


def balance(native, overlay, initially_unlocked, config=None):
    # Explicitly approved bridge: Ragman's entire normal chain begins behind LL2.
    supplier = '596b36c586f77450d6045ad2'
    ragman = '5ac3b934156ae10c4430e83c'
    original = copy.deepcopy(overlay)
    adjustments = []
    if supplier in overlay['Quests']:
        reward_id = hashlib.sha256(b'wtt-reputation:supplier-ragman').hexdigest()[:24]
        overlay['Quests'][supplier]['Reputation'].setdefault('Success', []).append(
            dict(id=reward_id, type='TraderStanding', target=ragman, value=0.5))
        adjustments.append(dict(QuestId=supplier, RewardId=reward_id, TraderId=ragman, Before=0,
                                After=0.5, UnlockLoyalty=2, WitnessSide='Both', Reason='Approved Supplier bridge for Only Business'))
    def routes(source=None):
        qs = overlay_quests(native, overlay if source is None else source)
        return [Route(qs, overlay['Traders'], side, initially_unlocked, config).run() for side in ('Usec', 'Bear')]
    before = [r.report() for r in routes(original)]
    for iteration in range(100):
        current = routes()
        blocked = [(t,r) for t in REGULAR for r in current if r.loyalty(t) < len(overlay['Traders'][t])]
        if not blocked:
            break
        tid, route = blocked[0]
        level = route.loyalty(tid)
        required = dec(overlay['Traders'][tid][level]['Standing'])
        gap = required - route.standing.get(tid, Decimal(0))
        if gap <= 0:
            raise ValueError('Non-reputation blocker for ' + tid)
        rewards = [(qid,r) for qid,spec in sorted(overlay['Quests'].items())
                   if spec['TraderId'] == tid and route.status.get(qid) == 4
                   for r in spec['Reputation'].get('Success', []) if r['target'] == tid and dec(r['value']) > 0]
        total = sum((dec(r['value']) for _,r in rewards), Decimal(0))
        if not total:
            # Native quests absent from the capture can still fund progression. Add only
            # a reputation overlay, keeping their original trader and entire start chain.
            for qid,q in sorted(native.items()):
                if qid in overlay['Quests'] or q['traderId'] != tid or route.status.get(qid) != 4:
                    continue
                if not any(r['type'] == 'TraderStanding' and r['target'] == tid and dec(r['value']) > 0 for r in q['rewards'].get('Success', [])):
                    continue
                spec = dict(TraderId=tid, Tier=0, UseBetaStart=True, Start=[],
                            Reputation={s:copy.deepcopy([r for r in rs if r['type'] == 'TraderStanding']) for s,rs in q['rewards'].items()})
                original['Quests'][qid] = copy.deepcopy(spec)
                overlay['Quests'][qid] = spec
            rewards = [(qid,r) for qid,spec in sorted(overlay['Quests'].items())
                       if spec['TraderId'] == tid and route.status.get(qid) == 4
                       for r in spec['Reputation'].get('Success', []) if r['target'] == tid and dec(r['value']) > 0]
            total = sum((dec(r['value']) for _,r in rewards), Decimal(0))
            if not total:
                raise ValueError('No reachable positive completion rewards for ' + tid)
        for qid, reward in rewards:
            old = dec(reward['value'])
            added = (gap * old / total).quantize(Decimal('.01'), rounding=ROUND_CEILING)
            reward['value'] = float(old + added)
            adjustments.append(dict(QuestId=qid, RewardId=reward['id'], TraderId=tid, Before=float(old),
                                    After=reward['value'], UnlockLoyalty=level+1, WitnessSide=route.side))
    else:
        raise ValueError('Reputation balancing did not converge')
    after = [r.report() for r in routes()]
    for route in after:
        assert all(t['Loyalty'] == 4 for t in route['Traders'].values()), 'Regular trader unreachable'
    # The only permitted differences are positive own-trader Success reputation amounts.
    for qid,spec in overlay['Quests'].items():
        old = original['Quests'][qid]
        for stage, rewards in spec['Reputation'].items():
            for i,r in enumerate(rewards):
                if qid == supplier and r['target'] == ragman and r['id'] == reward_id:
                    assert r['value'] == 0.5
                    continue
                previous = old['Reputation'][stage][i]
                if r != previous:
                    assert stage == 'Success' and spec['TraderId'] == r['target'] and r['target'] in REGULAR
                    assert dec(r['value']) >= dec(previous['value']) > 0
                    assert {k:v for k,v in r.items() if k != 'value'} == {k:v for k,v in previous.items() if k != 'value'}
    return dict(Version=1, Assumptions='Separate USEC and BEAR routes; zero initial standing; native trader unlocks; '
                'levels earned through play through 79; unlock delays elapsed; native raid/item objectives completed successfully; '
                'deterministic quest order and real branch statuses with failure penalties. No Arena, repeatables, seasonal quests, '
                'edition bonuses. Offline feasibility evidence, not a live playthrough. Older profiles receive only positive '
                'adjustment differences for completed quests, once per character, with a verified backup and persisted receipt.',
                Adjustments=adjustments, Before=before, After=after)
