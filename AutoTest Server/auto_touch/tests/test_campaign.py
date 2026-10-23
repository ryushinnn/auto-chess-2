from __future__ import annotations

import csv
import unittest
from pathlib import Path

from auto_touch import EXIT_FAIL, EXIT_INVALID, EXIT_OK, EXIT_UNREACHABLE
from auto_touch.campaign import run_campaign
from auto_touch.scenario import RunOptions
from auto_touch.tests.helpers import StubTestCase, fast_config, free_port

GOOD = {"note": "adds gold", "actions": [{"action": "op", "op": "add-gold", "params": {"amount": 1}}]}
BAD = {"note": "fails", "actions": [{"action": "assert", "path": "gold", "equals": -1}]}
MATRIX = {"note": "matrix", "matrix": {"n": [1, 2]},
          "actions": [{"action": "op", "op": "add-gold", "params": {"amount": "${n}"}}]}
PARAM = {"note": "needs bonus", "params": {"bonus": 0},
         "actions": [{"action": "op", "op": "add-gold", "params": {"amount": "${bonus}"}},
                     {"action": "assert", "path": "gold", "gte": "${bonus}"}]}


class TestCampaign(StubTestCase):
    def setUp(self):
        super().setUp()
        for name, doc in (("good.json", GOOD), ("bad.json", BAD), ("matrix.json", MATRIX), ("param.json", PARAM)):
            self.write(name, doc)

    def run_camp(self, doc, cfg=None, **opts):
        path = self.write("c_campaign.json", doc)
        return run_campaign(cfg or self.cfg, path, RunOptions(**opts), out=self.lines.append)

    def test_aggregates_every_case(self):
        res = self.run_camp({"note": "all", "scenarios": ["good.json", "matrix.json", "bad.json", "good.json"]})
        self.assertEqual(res["exitCode"], EXIT_FAIL)
        self.assertEqual([r["verdict"] for r in res["rows"]], ["PASS", "PASS", "PASS", "FAIL", "PASS"])
        self.assertEqual([r["case"] for r in res["rows"]][1:3], ["n=1", "n=2"])
        self.assertEqual(self.game.game["gold"], 10 + 1 + 1 + 2 + 1)  # one Play session: state carried over
        self.assertEqual(len(self.requests("/play", "POST")), 0)
        md = Path(res["reportPath"]).read_text(encoding="utf-8")
        self.assertIn("FAIL - campaign c_campaign", md)
        self.assertIn("4/5 passed", md)
        with open(res["csvPath"], encoding="utf-8") as fh:
            rows = list(csv.DictReader(fh))
        self.assertEqual(len(rows), 5)
        self.assertEqual(rows[3]["verdict"], "FAIL")
        self.assertIn("#1 actions[0] assert", rows[3]["failingStep"])
        self.assertTrue(Path(res["reportPath"]).name.startswith("c_campaign_campaign_"))

    def test_all_pass_is_exit_0(self):
        res = self.run_camp({"note": "ok", "scenarios": ["good.json", {"path": "matrix.json", "case": "n=2"}]})
        self.assertEqual(res["exitCode"], EXIT_OK, self.lines)
        self.assertEqual(len(res["rows"]), 2)

    def test_stop_on_fail(self):
        res = self.run_camp({"note": "stop", "stopOnFail": True, "scenarios": ["bad.json", "good.json"]})
        self.assertEqual(res["exitCode"], EXIT_FAIL)
        self.assertEqual([r["verdict"] for r in res["rows"]], ["FAIL", "NOT RUN"])
        self.assertEqual(self.game.game["gold"], 10)

    def test_params_reach_each_scenario(self):
        res = self.run_camp({"note": "params", "params": {"bonus": 5},
                             "scenarios": ["param.json", {"path": "param.json", "params": {"bonus": 7}}]})
        self.assertEqual(res["exitCode"], EXIT_OK, self.lines)
        self.assertEqual(self.game.game["gold"], 22)

    def test_fresh_play(self):
        res = self.run_camp({"note": "fresh", "freshPlay": True, "scenarios": ["good.json", "good.json"]})
        self.assertEqual(res["exitCode"], EXIT_OK, self.lines)
        ons = [r[2]["on"] for r in self.requests("/play", "POST")]
        self.assertEqual(ons, ["0", "1"])
        self.assertEqual(self.game.game["gold"], 11)  # new game after re-entering Play

    def test_invalid_campaign(self):
        res = self.run_camp({"note": "x", "scenarios": ["missing.json"]})
        self.assertEqual(res["exitCode"], EXIT_INVALID)
        res = self.run_camp({"scenarios": ["good.json"]})
        self.assertEqual(res["exitCode"], EXIT_INVALID)

    def test_unreachable(self):
        cfg = fast_config(free_port(), self.tmp)
        res = self.run_camp({"note": "x", "scenarios": ["good.json"]}, cfg=cfg)
        self.assertEqual(res["exitCode"], EXIT_UNREACHABLE)

    def test_exit_play(self):
        res = self.run_camp({"note": "x", "scenarios": ["good.json"]}, exit_play=True)
        self.assertEqual(res["exitCode"], EXIT_OK)
        self.assertFalse(self.game.playing)


if __name__ == "__main__":
    unittest.main()
