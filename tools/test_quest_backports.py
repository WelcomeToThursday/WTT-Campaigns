"""Offline compatibility and route contracts; no installed profiles or server processes."""
import copy
import unittest
from pathlib import Path
from import_quest_backports import inspect, propagate, dependencies, normalize_stages
from balance_quest_reputation import Route

TRADER = '54cb50c76803fa8b248b4571'


def quest(qid='a', start=None, finish=None):
    return dict(_id=qid, traderId=TRADER, name=qid+' name', description=qid+' description',
        side='Pmc', image='/icon.jpg', localization={'en':{qid+' name': 'Task', qid+' description': 'Description'}},
        conditions={'AvailableForStart':start or [], 'AvailableForFinish':finish or [dict(id='item', conditionType='HandoverItem', target=['item'])], 'Fail':[]},
        rewards={'Started':[], 'Success':[], 'Fail':[]})


def prerequisite(target, status=4):
    return dict(id='prerequisite-'+target, conditionType='Quest', target=target, status=[status], availableAfter=0)


class Inventory:
    items = {'item':{'_props':{}}, 'quest-item':{'_props':{'QuestItem':True}}}
    traders = {TRADER:{'items':[]}}
    obtainable = {'item', 'quest-item'}
    lootable = {'item'}
    maps = {'bigmap'}
    roles = {'assault'}
    native = {}
    def icon(self, route):
        return Path(__file__)


class CompatibilityTests(unittest.TestCase):
    def codes(self, q):
        return {b['Code'] for b in inspect(q, Inventory())[0]}

    def test_complete_inventory_task(self):
        self.assertFalse(self.codes(quest()))

    def test_nested_unknown_condition(self):
        q = quest(finish=[dict(id='counter', conditionType='CounterCreator', counter={'conditions':[dict(id='unsupported', conditionType='SearchAndDestroy')]})])
        self.assertIn('condition', self.codes(q))

    def test_empty_capture_stage_is_omitted_without_changing_objectives(self):
        q = quest()
        original = copy.deepcopy(q)
        q['conditions']['AutoStart'] = []
        q['rewards']['FutureStage'] = []
        blockers, evidence, _, _ = inspect(q, Inventory())
        self.assertFalse(blockers)
        self.assertTrue(any(e.get('OmittedEmptyStage') == 'conditions/AutoStart' for e in evidence))
        normalize_stages(q)
        self.assertEqual(q, original)
        normalize_stages(q)
        self.assertEqual(q, original)

    def test_nonempty_capture_stage_is_excluded_with_dependents(self):
        for name in ('conditions', 'rewards'):
            q = quest()
            q[name]['AutoStart'] = [dict(id='new-stage', type='Experience', value=1)]
            self.assertIn('quest-stage', self.codes(q))
            with self.assertRaises(ValueError):
                normalize_stages(q)
            records = {'a':dict(Blockers=inspect(q, Inventory())[0], CampaignId=None),
                       'b':dict(Blockers=[], CampaignId=None)}
            propagate({'a':q, 'b':quest('b', [prerequisite('a')])}, {}, records)
            self.assertEqual(records['b']['Blockers'][0]['Path'], ['b', 'a'])

    def test_unverified_and_verified_zone(self):
        q = quest(finish=[dict(id='visit', conditionType='VisitPlace', target='old-zone')])
        self.assertIn('unverified-client-zone', self.codes(q))
        blockers, evidence, _, _ = inspect(q, Inventory(), {('VisitPlace','old-zone'):{'Component':'PlaceForCheck', 'Source':'verified scene'}})
        self.assertFalse(blockers)
        self.assertEqual(evidence[0]['Component'], 'PlaceForCheck')

    def test_unknown_item_placement_or_acquisition(self):
        q = quest(finish=[dict(id='find', conditionType='FindItem', target=['quest-item'])])
        self.assertIn('quest-item-placement', self.codes(q))
        q['conditions']['AvailableForFinish'][0]['target'] = ['absent']
        self.assertIn('item-acquisition', self.codes(q))

    def test_new_reward_and_encounter(self):
        q = quest()
        q['rewards']['Success'] = [dict(type='LocationUnlock',id='reward')]
        self.assertIn('reward',self.codes(q))
        q = quest(finish=[dict(id='kill',conditionType='Kills',target='Savage',savageRole=['newBoss'])])
        self.assertIn('encounter',self.codes(q))

    def test_skip_transitive_and_finish_dependencies(self):
        candidates = {'a':quest('a'), 'b':quest('b', [prerequisite('a')]), 'c':quest('c', finish=[prerequisite('b')])}
        records = {q:dict(Blockers=[],CampaignId=None) for q in candidates}
        records['a']['Blockers'] = [dict(Code='zone',Path=['a'])]
        propagate(candidates,{},records)
        self.assertEqual(records['c']['Blockers'][0]['Path'], ['c','b','a'])

    def test_visibility_is_not_quest_dependency(self):
        q = quest()
        q['conditions']['AvailableForFinish'][0]['visibilityConditions'] = [dict(id='visible',conditionType='CompleteCondition',target='item')]
        self.assertEqual(dependencies(q),[])
        self.assertFalse(self.codes(q))

    def test_campaign_dependency(self):
        qs = {'a':quest('a'), 'b':quest('b',[prerequisite('a')])}
        records = {'a':dict(Blockers=[],CampaignId='season'), 'b':dict(Blockers=[],CampaignId=None)}
        propagate(qs,{},records)
        self.assertEqual(records['b']['Blockers'][0]['Code'],'campaign-dependency')


class RouteTests(unittest.TestCase):
    def route(self, quests, config=None):
        return Route(quests,{TRADER:[dict(Level=1,Standing=0),dict(Level=12,Standing=.5)]},'Usec',{TRADER},config).run()

    def test_real_terminal_branch_outcomes(self):
        a,b=quest('a'),quest('b')
        a['conditions']['Fail']=[prerequisite('b')]
        b['conditions']['Fail']=[prerequisite('a')]
        a['rewards']['Success']=[dict(type='TraderStanding',target=TRADER,value=.1)]
        b['rewards']['Success']=[dict(type='TraderStanding',target=TRADER,value=.8)]
        result=self.route({'a':a,'b':b})
        self.assertEqual(result.status.get('a'),4)
        self.assertNotEqual(result.status.get('b'),4)
        self.assertEqual(float(result.standing[TRADER]),.1)

    def test_finish_loyalty_cannot_be_assumed_complete(self):
        q=quest('a',finish=[dict(conditionType='TraderLoyalty',target=TRADER,compareMethod='>=',value=2)])
        self.assertEqual(self.route({'a':q}).status['a'],2)

    def test_delays_levels_and_started_prerequisite(self):
        a=quest('a',start=[dict(conditionType='Level',compareMethod='>=',value=5)],finish=[dict(conditionType='TraderLoyalty',target=TRADER,compareMethod='>=',value=2)])
        c=prerequisite('a',2);c['availableAfter']=3600
        b=quest('b',[c]);b['rewards']['Success']=[dict(type='TraderStanding',target=TRADER,value=.5)]
        result=self.route({'a':a,'b':b})
        starts={s['QuestId']:s for s in result.steps if s['Stage']=='Started'}
        self.assertGreaterEqual(starts['a']['Level'],5)
        self.assertGreaterEqual(starts['b']['Time']-starts['a']['Time'],3600)
        self.assertEqual(result.status['a'],4)

    def test_event_edition_side_and_season_exclusions(self):
        quests={id:quest(id) for id in ('event','edition','bear','season')}
        quests['season']['wttSeasonalOnly']=True
        cfg=dict(eventQuests={'event':{}},profileWhitelist={'edition':['unheard_edition']},bearOnlyQuests=['bear'])
        self.assertFalse(self.route(quests,cfg).steps)

    def test_rewards_once_with_actual_failure_penalty(self):
        a=quest('a',finish=[prerequisite('c')]);b=quest('b');c=quest('c',[prerequisite('b')])
        a['conditions']['Fail']=[prerequisite('b')]
        a['rewards']['Fail']=[dict(type='TraderStanding',target=TRADER,value=-.1)]
        b['rewards']['Success']=[dict(type='TraderStanding',target=TRADER,value=.3)]
        result=self.route({'a':a,'b':b,'c':c})
        self.assertEqual(result.status['a'],5)
        self.assertEqual(float(result.standing[TRADER]),.2)
        self.assertEqual(len([s for s in result.steps if s['QuestId']=='a' and s['Stage']=='Fail']),1)


if __name__ == '__main__':
    unittest.main()
