"""Offline regression contracts for quest progression import; no server or profiles used."""
import copy
import unittest

from import_trader_progression import compile_data


class ProgressionImportTests(unittest.TestCase):
    def setUp(self):
        self.native_start = [
            dict(id='level', conditionType='Level', compareMethod='>=', value=5),
            dict(id='previous', conditionType='Quest', target='previous-quest',
                 status=[4, 5], availableAfter=3600, dispersion=0),
        ]
        self.beta = {'task': dict(traderId='trader', conditions=dict(AvailableForStart=self.native_start))}
        self.quest = dict(_id='task', traderId='trader', tierAccessory=1,
                          conditions=dict(AvailableForStart=[]), rewards={})
        self.traders = [dict(_id='trader', loyaltyLevels=[dict(minLevel=1, minStanding=0)])]

    def compile(self):
        return compile_data([self.quest], self.traders, self.beta, {'trader'})[0]['Quests']['task']

    def test_tiered_tasks_keep_native_gates_with_incomplete_capture(self):
        for tier in range(1, 5):
            for captured in [
                [],
                [dict(id='live', conditionType='GlobalVariableValue', target='live-variable', value=3)],
                [dict(id='missing', conditionType='Quest', target='live-only-quest', status=[4])],
                [dict(id='live-level', conditionType='Level', compareMethod='>=', value=1)],
            ]:
                with self.subTest(tier=tier, captured=captured):
                    self.quest['tierAccessory'] = tier
                    self.quest['conditions']['AvailableForStart'] = captured
                    spec = self.compile()
                    self.assertFalse(spec['UseBetaStart'])
                    self.assertEqual(spec['Start'][:-1], self.native_start)
                    gate = spec['Start'][-1]
                    self.assertEqual((gate['conditionType'], gate['target'], gate['value'], gate['compareMethod']),
                                     ('TraderLoyalty', 'trader', tier, '>='))

    def test_genuine_starter_only_needs_loyalty(self):
        self.beta['task']['conditions']['AvailableForStart'] = []
        self.assertEqual([c['conditionType'] for c in self.compile()['Start']], ['TraderLoyalty'])

    def test_native_loyalty_gate_is_not_weakened_or_duplicated(self):
        gate = dict(id='native-loyalty', conditionType='TraderLoyalty', target='trader', value=3, compareMethod='>=')
        self.native_start.append(gate)
        self.assertEqual(self.compile()['Start'], self.native_start)

    def test_import_does_not_mutate_native_conditions(self):
        original = copy.deepcopy(self.beta)
        spec = self.compile()
        self.assertEqual(self.beta, original)
        spec['Start'][0]['value'] = 100
        self.assertEqual(self.beta, original)

    def test_essential_fallback_is_unchanged(self):
        self.quest['tierAccessory'] = 0
        self.quest['conditions']['AvailableForStart'] = [dict(id='live', conditionType='GlobalVariableValue')]
        spec = self.compile()
        self.assertTrue(spec['UseBetaStart'])
        self.assertEqual(spec['Start'], [])

    def test_essential_tasks_always_preserve_native_requirements(self):
        self.quest['tierAccessory'] = 0
        for captured in [[], [dict(id='live-level', conditionType='Level', compareMethod='>=', value=1)]]:
            with self.subTest(captured=captured):
                self.quest['conditions']['AvailableForStart'] = captured
                spec = self.compile()
                self.assertTrue(spec['UseBetaStart'], 'Empty or supported capture must not erase native essential gates')
                self.assertEqual(spec['Start'], [])


if __name__ == '__main__':
    unittest.main()
