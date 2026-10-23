from __future__ import annotations

import json
import unittest
from pathlib import Path

from auto_touch import EXIT_COMPILE, EXIT_FAIL, EXIT_INVALID, EXIT_OK, EXIT_UNREACHABLE
from auto_touch.scenario import Runner, RunOptions
from auto_touch.tests.helpers import StubTestCase, fast_config, free_port


def scen(actions, **top):
    return {"note": "test scenario", **top, "actions": actions}


def op(name, **params):
    step = {"action": "op", "op": name}
    if params:
        step["params"] = params
    return step


class TestBasics(StubTestCase):
    def test_pass_writes_json_and_markdown(self):
        res = self.run_doc(scen([op("add-gold", amount=5), {"action": "assert", "path": "gold", "equals": 15}]))
        self.assertEqual(res["exitCode"], EXIT_OK)
        rep = self.case(res)
        self.assertEqual(rep["verdict"], "PASS")
        self.assertEqual(rep["counts"]["pass"], 2)
        diag = json.loads(Path(rep["reportPath"]).read_text(encoding="utf-8"))
        self.assertEqual(diag["steps"][0]["action"], "op")
        for key in ("index", "action", "label", "ok", "status", "message", "durationMs", "data"):
            self.assertIn(key, diag["steps"][0])
        self.assertIn("finalState", diag)
        self.assertEqual(diag["finalState"]["gold"], 15)
        self.assertTrue(Path(rep["reportPath"]).parent.name == "diagnostics")
        md = Path(rep["markdownPath"]).read_text(encoding="utf-8")
        self.assertIn("# PASS - t", md)
        self.assertTrue(Path(rep["markdownPath"]).parent.name == "reports")

    def test_quiet_console(self):
        self.run_doc(scen([op("add-gold")]))
        self.assertEqual(len(self.lines), 1)
        self.assertTrue(self.lines[0].startswith("PASS t ("))
        self.assertIn("report: ", self.lines[0])
        self.lines.clear()
        res = self.run_doc(scen([op("add-gold"), op("add-gold"), op("add-gold"),
                                 {"action": "assert", "path": "gold", "equals": 0}, op("add-gold")]))
        self.assertEqual(res["exitCode"], EXIT_FAIL)
        self.assertTrue(self.lines[0].startswith("FAIL t"))
        self.assertIn("step #4", self.lines[0])
        step_lines = [l for l in self.lines if l.strip().startswith("[")]
        self.assertEqual(len(step_lines), 3)  # two before + the failing one
        self.assertIn("[FAIL] #4", step_lines[-1])

    def test_verbose_prints_every_step(self):
        self.run_doc(scen([op("add-gold"), op("add-gold")]), verbose=True)
        self.assertEqual(sum(1 for l in self.lines if "[pass]" in l), 2)

    def test_assert_failure_stops_and_skips(self):
        res = self.run_doc(scen([{"action": "assert", "path": "gold", "gt": 100, "label": "rich"}, op("add-gold")]))
        rep = self.case(res)
        self.assertEqual(rep["exitCode"], EXIT_FAIL)
        self.assertEqual(rep["failure"]["label"], "rich")
        self.assertIn("gold = 10", rep["failure"]["message"])
        self.assertEqual([s["status"] for s in rep["steps"]], ["fail", "skipped"])
        self.assertEqual(self.game.game["gold"], 10)

    def test_no_stop_runs_everything(self):
        res = self.run_doc(scen([{"action": "assert", "path": "gold", "gt": 100}, op("add-gold")]), no_stop=True)
        self.assertEqual(res["exitCode"], EXIT_FAIL)
        self.assertEqual([s["status"] for s in self.steps(res)], ["fail", "pass"])
        self.assertEqual(self.game.game["gold"], 11)

    def test_teardown_always_runs(self):
        doc = scen([{"action": "assert", "path": "gold", "equals": -1}, op("add-gold", amount=1)],
                   setup=[op("add-gold", amount=2)], teardown=[op("add-gold", amount=100)])
        res = self.run_doc(doc)
        steps = self.steps(res)
        self.assertEqual([(s["phase"], s["status"]) for s in steps],
                         [("setup", "pass"), ("actions", "fail"), ("actions", "skipped"), ("teardown", "pass")])
        self.assertEqual(self.game.game["gold"], 112)

    def test_setup_failure_skips_actions_but_not_teardown(self):
        doc = scen([op("add-gold")], setup=[op("boom")], teardown=[op("add-gold", amount=50)])
        steps = self.steps(self.run_doc(doc))
        self.assertEqual([s["status"] for s in steps], ["fail", "skipped", "pass"])
        self.assertEqual(self.game.game["gold"], 60)

    def test_teardown_failure_fails_the_run(self):
        res = self.run_doc(scen([op("add-gold")], teardown=[op("boom"), op("add-gold")]))
        self.assertEqual(res["exitCode"], EXIT_FAIL)
        self.assertEqual([s["status"] for s in self.steps(res)], ["pass", "fail", "pass"])


class TestSteps(StubTestCase):
    def test_save_as_and_templating(self):
        doc = scen([{**op("add-gold", amount=4), "saveAs": "r"},
                    {"action": "assert", "path": "gold", "equals": "${r.gold}"},
                    {"action": "assert", "path": "$r.gold", "equals": 14},
                    {"action": "state", "path": "items", "saveAs": "items"},
                    {**op("add-item", id="unit-${r.gold}")},
                    {"action": "assert", "path": "items[0]", "equals": "unit-14"},
                    {"action": "assert", "path": "$items", "length": 0}])
        res = self.run_doc(doc)
        self.assertEqual(res["exitCode"], EXIT_OK, self.lines)
        rep = self.case(res)
        self.assertEqual(rep["snapshots"][0]["path"], "items")
        self.assertEqual(self.requests("/game-op")[0][2]["amount"], "4")

    def test_unresolved_placeholder_is_invalid_before_running(self):
        res = self.run_doc(scen([op("add-gold", amount="${nope}")]))
        self.assertEqual(res["exitCode"], EXIT_INVALID)
        self.assertIn("nope", " ".join(res["problems"]))
        self.assertEqual(self.requests("/game-op"), [])

    def test_declared_but_unset_variable_fails_the_step(self):
        doc = scen([{**op("boom"), "saveAs": "x", "optional": True},
                    {"action": "assert", "path": "gold", "equals": "${x.gold}"}])
        res = self.run_doc(doc)
        self.assertEqual(res["exitCode"], EXIT_FAIL)
        self.assertIn("did not run or failed", self.case(res)["failure"]["message"])

    def test_op_expect_and_expect_error(self):
        res = self.run_doc(scen([{**op("add-gold", amount=2), "expect": {"gold": {"gte": 12}}},
                                 {**op("boom"), "expectError": True},
                                 {**op("add-gold", amount="x"), "expectError": "bad_request"}]))
        self.assertEqual(res["exitCode"], EXIT_OK, self.lines)
        res = self.run_doc(scen([{**op("add-gold"), "expect": {"gold": 999}}]))
        self.assertIn("result.gold = 13, expected 999", self.case(res)["failure"]["message"])
        res = self.run_doc(scen([op("boom")]))
        rep = self.case(res)
        self.assertIn("HTTP 500", rep["failure"]["message"])
        self.assertIn("stack", rep["steps"][0]["data"])

    def test_every_operator_in_a_scenario(self):
        doc = scen([op("add-item", id="knight"), op("add-item", id="mage"),
                    {"action": "assert", "path": "gold", "equals": 10},
                    {"action": "assert", "path": "gold", "notEquals": 11},
                    {"action": "assert", "path": "gold", "gt": 9},
                    {"action": "assert", "path": "gold", "gte": 10},
                    {"action": "assert", "path": "gold", "lt": 11},
                    {"action": "assert", "path": "gold", "lte": 10},
                    {"action": "assert", "path": "phase", "in": ["shop", "combat"]},
                    {"action": "assert", "path": "items", "contains": "mage"},
                    {"action": "assert", "path": "items.1", "exists": True},
                    {"action": "assert", "path": "items.5", "exists": False},
                    {"action": "assert", "path": "items", "length": 2},
                    {"action": "assert", "path": "items", "length": {"gte": 2}},
                    {"action": "assert", "path": "gold", "approx": 10.2, "tolerance": 0.5},
                    {"action": "assert", "path": "items[0]", "matches": "^kn"}])
        res = self.run_doc(doc)
        self.assertEqual(res["exitCode"], EXIT_OK, self.lines)

    def test_wait_state(self):
        res = self.run_doc(scen([op("count-to", target=3),
                                 {"action": "wait_state", "path": "counter", "gte": 3, "timeoutMs": 2000}]))
        self.assertEqual(res["exitCode"], EXIT_OK, self.lines)
        self.assertEqual(self.steps(res)[1]["data"]["polls"], 3)
        res = self.run_doc(scen([{"action": "wait_state", "path": "phase", "equals": "combat", "timeoutMs": 100}]))
        self.assertIn("timed out after 100 ms", self.case(res)["failure"]["message"])

    def test_click_variants(self):
        doc = scen([{"action": "click", "text": "Next"},
                    {"action": "assert", "path": "phase", "equals": "combat"},
                    {"action": "click", "path": "Canvas/HUD/GoldButton"},
                    {"action": "click", "target": "shop:2"},
                    {"action": "click", "x": 640, "y": 560},
                    {"action": "click", "path": "Canvas/HUD/LockedButton", "expectExecuted": False},
                    {"action": "assert", "path": "gold", "equals": 9},
                    {"action": "assert", "path": "@last.executed", "equals": False}])
        res = self.run_doc(doc)
        self.assertEqual(res["exitCode"], EXIT_OK, self.lines)
        self.assertEqual(self.game.game["items"], ["unit2"])
        res = self.run_doc(scen([{"action": "click", "path": "Canvas/HUD/LockedButton", "timeoutMs": 50}]))
        self.assertIn("not interactable", self.case(res)["failure"]["message"])
        res = self.run_doc(scen([{"action": "click", "target": "shop:9", "timeoutMs": 50}]))
        self.assertIn("available: shop:0", self.case(res)["failure"]["message"])

    def test_drag(self):
        doc = scen([{"action": "click", "target": "shop:0"},
                    {"action": "drag", "fromTarget": "bench:0", "toTarget": "board:0",
                     "expect": {"dropTarget": "World/board:0"}},
                    {"action": "assert", "path": "board", "equals": ["unit0"]},
                    {"action": "drag", "x0": 400, "y0": 300, "x1": 100, "y1": 50, "expectExecuted": False}])
        res = self.run_doc(doc)
        self.assertEqual(res["exitCode"], EXIT_OK, self.lines)

    def test_wait_ui_and_assert_ui(self):
        doc = scen([{"action": "assert_ui", "path": "Canvas/Popup/CloseButton", "expect": {"active": False}},
                    {"action": "assert_ui", "path": "Canvas/HUD/LockedButton", "expect": {"interactable": False}},
                    {"action": "assert_ui", "text": "Gold:", "expect": {"caption": "Gold: 10"}},
                    {"action": "assert_ui", "path": "Canvas/Nope", "expect": {"found": False}},
                    {"action": "click", "text": "Settings"},
                    {"action": "wait_ui", "path": "Canvas/Popup/CloseButton", "state": "interactable"},
                    {"action": "click", "text": "Close"},
                    {"action": "wait_ui", "match": "CloseButton", "state": "inactive"},
                    {"action": "wait_ui", "path": "Canvas/Nope", "state": "absent"},
                    {"action": "wait_ui", "path": "Canvas/HUD/GoldText", "state": "exists"}])
        res = self.run_doc(doc)
        self.assertEqual(res["exitCode"], EXIT_OK, self.lines)
        res = self.run_doc(scen([{"action": "wait_ui", "path": "Canvas/Popup/CloseButton", "timeoutMs": 60}]))
        self.assertIn("not active after 60 ms", self.case(res)["failure"]["message"])
        res = self.run_doc(scen([{"action": "assert_ui", "text": "Gold", "expect": {"caption": "Silver"}}]))
        self.assertEqual(res["exitCode"], EXIT_FAIL)

    def test_screenshot(self):
        res = self.run_doc(scen([{"action": "screenshot", "name": "evidence"}, {"action": "screenshot"}]))
        rep = self.case(res)
        self.assertEqual(res["exitCode"], EXIT_OK, self.lines)
        self.assertEqual([s["name"] for s in rep["screenshots"]], ["evidence", "t_002"])
        self.assertTrue(all(Path(s["path"]).is_file() for s in rep["screenshots"]))

    def test_reflect(self):
        doc = scen([{"action": "reflect", "op": "types", "type": "AutoChess", "expect": [{"name": "AutoChess.GameManager"},
                                                                                         {"name": "AutoChess.Shop"}]},
                    {"action": "reflect", "op": "call", "type": "GameManager", "method": "AddGold", "args": [5],
                     "saveAs": "g"},
                    {"action": "assert", "path": "$g", "equals": 15},
                    {"action": "reflect", "op": "set", "type": "GameManager", "member": "Gold", "value": 3},
                    {"action": "reflect", "op": "get", "type": "GameManager", "member": "Gold", "expect": 3},
                    {"action": "reflect", "op": "call", "type": "GameManager", "method": "Boom",
                     "expectThrows": "InvalidOperation"}])
        res = self.run_doc(doc)
        self.assertEqual(res["exitCode"], EXIT_OK, self.lines)
        res = self.run_doc(scen([{"action": "reflect", "op": "call", "type": "GameManager", "method": "Boom"}]))
        self.assertIn("threw System.InvalidOperationException", self.case(res)["failure"]["message"])

    def test_menu_and_edit_mode_scenario(self):
        self.game.playing = False
        res = self.run_doc(scen([{"action": "menu", "item": "Assets/Refresh"}], requiresPlay=False))
        self.assertEqual(res["exitCode"], EXIT_OK, self.lines)
        self.assertEqual(self.requests("/play"), [])
        res = self.run_doc(scen([{"action": "menu", "item": "No/Such"}], requiresPlay=False))
        self.assertEqual(res["exitCode"], EXIT_FAIL)

    def test_play_step_and_timescale(self):
        doc = scen([{"action": "timescale", "value": 4}, {"action": "play", "on": False},
                    {"action": "play", "on": True}, {"action": "play", "on": True}], timescale=2)
        res = self.run_doc(doc)
        self.assertEqual(res["exitCode"], EXIT_OK, self.lines)
        sets = [r[2]["value"] for r in self.requests("/timescale", "POST")]
        self.assertEqual(sets, ["2.0", "4.0", "4.0", "1.0"])  # run, step, re-applied after Play, restored
        self.assertEqual(self.game.time_scale, 1.0)
        self.assertIn("already playing", self.steps(res)[3]["message"])

    def test_refresh_with_compile_errors_aborts_with_5(self):
        self.game.compile_errors = [{"assembly": "Assembly-CSharp", "file": "Assets/X.cs", "line": 1, "column": 2,
                                     "message": "CS0103: nope"}]
        res = self.run_doc(scen([{"action": "refresh", "exitPlay": True}, op("add-gold")], requiresPlay=False))
        self.assertEqual(res["exitCode"], EXIT_COMPILE)
        steps = self.steps(res)
        self.assertEqual(steps[0]["status"], "fail")
        self.assertIn("Assets/X.cs(1,2): CS0103", steps[0]["data"]["compileErrors"][0])
        self.assertEqual(steps[1]["status"], "skipped")


class TestContainers(StubTestCase):
    def test_include_with_params(self):
        self.write("sub/give_gold.json", {"note": "give gold", "params": {"amount": 1},
                                          "actions": [{**op("add-gold", amount="${amount}"), "saveAs": "given"}]})
        doc = scen([{"action": "include", "scenario": "sub/give_gold.json", "params": {"amount": 7}},
                    {"action": "assert", "path": "gold", "equals": 17},
                    {"action": "assert", "path": "$given.gold", "equals": 17},
                    {"action": "include", "scenario": "sub/give_gold"},
                    {"action": "assert", "path": "gold", "equals": 18}])
        res = self.run_doc(doc)
        self.assertEqual(res["exitCode"], EXIT_OK, self.lines)
        where = [s["where"] for s in self.steps(res)]
        self.assertIn("actions[0]<give_gold>.actions[0]", where)

    def test_include_failure_and_missing_file(self):
        self.write("bad.json", {"note": "fails", "actions": [op("boom")], "teardown": [op("add-gold", amount=5)]})
        res = self.run_doc(scen([{"action": "include", "scenario": "bad.json"}, op("add-gold")]))
        self.assertEqual(res["exitCode"], EXIT_FAIL)
        self.assertEqual(self.game.game["gold"], 15)  # the included teardown ran, the outer next step did not
        res = self.run_doc(scen([{"action": "include", "scenario": "nope.json"}]))
        self.assertEqual(res["exitCode"], EXIT_INVALID)

    def test_optional_include_turns_failures_into_warnings(self):
        self.write("bad.json", {"note": "fails", "actions": [op("boom")]})
        res = self.run_doc(scen([{"action": "include", "scenario": "bad.json", "optional": True}, op("add-gold")]))
        self.assertEqual(res["exitCode"], EXIT_OK, self.lines)
        self.assertEqual([s["status"] for s in self.steps(res)], ["warn", "warn", "pass"])

    def test_repeat_until_stops_early(self):
        doc = scen([{"action": "repeat", "times": 10, "until": {"path": "phase", "equals": "result"},
                     "actions": [op("next-phase")]}])
        res = self.run_doc(doc)
        self.assertEqual(res["exitCode"], EXIT_OK, self.lines)
        self.assertEqual(self.steps(res)[0]["data"]["iterations"], 2)
        self.assertEqual(len(self.requests("/game-op")), 2)

    def test_repeat_until_required(self):
        doc = scen([{"action": "repeat", "times": 2, "until": {"path": "gold", "gt": 100}, "untilRequired": True,
                     "actions": [op("add-gold")]}])
        res = self.run_doc(doc)
        self.assertIn("never held", self.case(res)["failure"]["message"])
        doc["actions"][0]["untilRequired"] = False
        self.assertEqual(self.run_doc(doc)["exitCode"], EXIT_OK)

    def test_repeat_as_variable_and_child_failure(self):
        doc = scen([{"action": "repeat", "times": 3, "as": "i", "actions": [op("add-item", id="u${i}")]},
                    {"action": "assert", "path": "items", "equals": ["u0", "u1", "u2"]}])
        self.assertEqual(self.run_doc(doc)["exitCode"], EXIT_OK, self.lines)
        doc = scen([{"action": "repeat", "times": 3, "actions": [op("add-gold"), op("boom")]}])
        res = self.run_doc(doc)
        self.assertEqual(res["exitCode"], EXIT_FAIL)
        self.assertEqual(self.game.game["gold"], 11)


class TestVerdictModifiers(StubTestCase):
    def test_known_issue_failing_is_reported_not_failed(self):
        res = self.run_doc(scen([{"action": "assert", "path": "gold", "equals": 0, "knownIssue": "BUG-42"},
                                 op("add-gold")]))
        self.assertEqual(res["exitCode"], EXIT_OK)
        rep = self.case(res)
        self.assertEqual(rep["steps"][0]["status"], "known_issue")
        self.assertEqual(rep["counts"]["known_issue"], 1)
        self.assertTrue(any(a["type"] == "known_issue" and a["knownIssue"] == "BUG-42" for a in rep["anomalies"]))

    def test_known_issue_passing_is_resolved(self):
        res = self.run_doc(scen([{"action": "assert", "path": "gold", "equals": 10, "knownIssue": "BUG-7"}]))
        rep = self.case(res)
        self.assertEqual(rep["steps"][0]["status"], "pass")
        self.assertTrue(rep["steps"][0]["knownIssueResolved"])
        self.assertTrue(any(a["type"] == "known_issue_resolved" for a in rep["anomalies"]))
        self.assertTrue(any("knownIssue 'BUG-7'" in l for l in self.lines))

    def test_optional_is_a_warning(self):
        res = self.run_doc(scen([{"action": "assert", "path": "gold", "equals": 0, "optional": True}, op("add-gold")]))
        self.assertEqual(res["exitCode"], EXIT_OK)
        self.assertEqual([s["status"] for s in self.steps(res)], ["warn", "pass"])


class TestWatchdogs(StubTestCase):
    def log_op(self, msg, type_="Error", **extra):
        return {**op("log", type=type_, message=msg), **extra}

    def test_red_log_fails_the_step(self):
        res = self.run_doc(scen([self.log_op("NullReferenceException: x", "Exception"), op("add-gold")]))
        rep = self.case(res)
        self.assertEqual(res["exitCode"], EXIT_FAIL)
        self.assertIn("1 red log(s): Exception: NullReferenceException", rep["failure"]["message"])
        self.assertTrue(any(a["type"] == "red_logs" for a in rep["anomalies"]))
        self.assertEqual(rep["steps"][1]["status"], "skipped")

    def test_warnings_are_not_red(self):
        self.assertEqual(self.run_doc(scen([self.log_op("meh", "Warning")]))["exitCode"], EXIT_OK)

    def test_allow_red_logs(self):
        self.assertEqual(self.run_doc(scen([self.log_op("boom", allowRedLogs=True)]))["exitCode"], EXIT_OK)
        res = self.run_doc(scen([self.log_op("expected: bad input", allowRedLogs=["^expected"])]))
        self.assertEqual(res["exitCode"], EXIT_OK)
        self.assertEqual(self.steps(res)[0]["data"]["allowedRedLogs"], 1)
        res = self.run_doc(scen([self.log_op("other", allowRedLogs=["^expected"])]))
        self.assertEqual(res["exitCode"], EXIT_FAIL)

    def test_config_red_log_ignore(self):
        cfg = fast_config(self.srv.port, self.tmp, red_log_ignore=["^trace:"])
        self.assertEqual(self.run_doc(scen([self.log_op("trace: hello")]), cfg=cfg)["exitCode"], EXIT_OK)

    def test_fail_on_red_logs_off_keeps_the_anomaly(self):
        cfg = fast_config(self.srv.port, self.tmp, fail_on_red_logs=False)
        res = self.run_doc(scen([self.log_op("bad")]), cfg=cfg)
        self.assertEqual(res["exitCode"], EXIT_OK)
        self.assertTrue(any(a["type"] == "red_logs" for a in self.case(res)["anomalies"]))
        # ...and an explicit logs step still fails on it once
        res = self.run_doc(scen([self.log_op("bad"), {"action": "logs", "failOnRed": True}]), cfg=cfg)
        self.assertEqual(res["exitCode"], EXIT_OK)  # already handled by the watchdog
        res = self.run_doc(scen([{"action": "logs", "match": "Game booted"}]), cfg=cfg)
        self.assertEqual(res["exitCode"], EXIT_FAIL)  # nothing logged this run

    def test_logs_step(self):
        self.game.add_log("Error", "before the run")  # baseline excludes it
        res = self.run_doc(scen([self.log_op("hello", "Log"), {"action": "logs", "match": "^hel", "level": "all"}]))
        self.assertEqual(res["exitCode"], EXIT_OK, self.lines)
        self.assertEqual(self.steps(res)[1]["data"]["count"], 1)

    def test_known_issue_red_log(self):
        res = self.run_doc(scen([self.log_op("known crash", knownIssue="BUG-9")]))
        self.assertEqual(res["exitCode"], EXIT_OK)
        self.assertEqual(self.steps(res)[0]["status"], "known_issue")

    def test_red_logs_after_play_reentry_are_seen(self):
        self.game.add_log("Log", "x")
        self.game.add_log("Log", "y")
        res = self.run_doc(scen([op("add-gold"), {"action": "play", "on": False}, {"action": "play", "on": True},
                                 self.log_op("after reload")]))
        self.assertEqual(res["exitCode"], EXIT_FAIL)
        rep = self.case(res)
        self.assertIn("after reload", rep["failure"]["message"])

    def test_invariant_watchdog(self):
        res = self.run_doc(scen([op("add-gold", amount=-20), op("add-gold")]))
        rep = self.case(res)
        self.assertEqual(res["exitCode"], EXIT_FAIL)
        self.assertIn("invariant gold-non-negative [high]", rep["failure"]["message"])
        cfg = fast_config(self.srv.port, self.tmp, fail_on_invariants="never")
        res = self.run_doc(scen([op("add-gold", amount=-20)]), cfg=cfg)
        self.assertEqual(res["exitCode"], EXIT_OK)
        self.assertTrue(any(a["type"] == "invariants" for a in self.case(res)["anomalies"]))
        cfg = fast_config(self.srv.port, self.tmp, invariants_every_step=False)
        self.assertEqual(self.run_doc(scen([op("add-gold", amount=-20)]), cfg=cfg)["exitCode"], EXIT_OK)

    def test_invariant_seen_once_and_medium_threshold(self):
        doc = scen([op("add-gold", amount=-20), op("add-gold")])
        res = self.run_doc(doc, no_stop=True)
        self.assertEqual([s["status"] for s in self.steps(res)], ["fail", "pass"])
        cfg = fast_config(self.srv.port, self.tmp, fail_on_invariants="high")
        self.game.reset()
        doc = scen([{"action": "repeat", "times": 9, "actions": [op("add-item")]}])
        self.assertEqual(self.run_doc(doc, cfg=cfg)["exitCode"], EXIT_OK)
        cfg = fast_config(self.srv.port, self.tmp, fail_on_invariants="medium")
        self.game.reset()
        self.assertEqual(self.run_doc(doc, cfg=cfg)["exitCode"], EXIT_FAIL)

    def test_invariants_step_and_unsupported(self):
        res = self.run_doc(scen([op("add-gold", amount=-20, ), {"action": "invariants", "minSeverity": "low"}],
                                ), no_stop=True)
        self.assertEqual([s["status"] for s in self.steps(res)], ["fail", "fail"])
        self.game.reset()
        self.game.invariants_supported = False
        res = self.run_doc(scen([op("add-gold", amount=-20)]))
        self.assertEqual(res["exitCode"], EXIT_OK)
        self.assertTrue(any(a["type"] == "invariants_unsupported" for a in self.case(res)["anomalies"]))
        res = self.run_doc(scen([{"action": "invariants"}]))
        self.assertEqual(res["exitCode"], EXIT_FAIL)


class TestPreflightAndModes(StubTestCase):
    def test_unreachable_exit_2(self):
        cfg = fast_config(free_port(), self.tmp)
        res = self.run_doc(scen([op("add-gold")]), cfg=cfg)
        self.assertEqual(res["exitCode"], EXIT_UNREACHABLE)

    def test_compile_errors_exit_5(self):
        self.game.last_compile = {"errorCount": 1, "warningCount": 0, "finishedAtUtc": "x",
                                  "errors": [{"file": "Assets/A.cs", "line": 1, "column": 1, "message": "CS1"}]}
        res = self.run_doc(scen([op("add-gold")]))
        self.assertEqual(res["exitCode"], EXIT_COMPILE)
        self.assertIn("Assets/A.cs(1,1): CS1", res["problems"][0])
        self.assertEqual(self.requests("/game-op"), [])

    def test_script_compilation_failed_flag_is_a_compile_error(self):
        orig = self.game.status_json
        self.game.status_json = lambda: {**orig(), "scriptCompilationFailed": True}
        try:
            res = self.run_doc(scen([op("add-gold")]))
        finally:
            self.game.status_json = orig
        self.assertEqual(res["exitCode"], EXIT_COMPILE)

    def test_enters_play_when_needed(self):
        self.game.playing = False
        res = self.run_doc(scen([op("add-gold")], scene="Assets/Scenes/Battle.unity"))
        self.assertEqual(res["exitCode"], EXIT_OK, self.lines)
        steps = self.steps(res)
        self.assertEqual(steps[0]["phase"], "preflight")
        self.assertEqual(self.requests("/play", "POST")[0][2], {"on": "1", "scene": "Assets/Scenes/Battle.unity"})
        self.assertEqual(self.game.active_scene, "Assets/Scenes/Battle.unity")

    def test_does_not_enter_play_when_not_allowed(self):
        self.game.playing = False
        cfg = fast_config(self.srv.port, self.tmp, auto_enter_play=False)
        res = self.run_doc(scen([op("add-gold")], teardown=[op("add-gold")]), cfg=cfg)
        self.assertEqual(res["exitCode"], EXIT_FAIL)
        self.assertIn("--enter-play", self.case(res)["failure"]["message"])
        self.assertEqual(self.requests("/play"), [])
        res = self.run_doc(scen([op("next-phase")]), cfg=cfg, enter_play=True)
        self.assertEqual(res["exitCode"], EXIT_OK, self.lines)

    def test_exit_play_option(self):
        res = self.run_doc(scen([op("add-gold")]), exit_play=True)
        self.assertEqual(res["exitCode"], EXIT_OK)
        self.assertFalse(self.game.playing)

    def test_server_dies_mid_run(self):
        self.game.drop_next = 0
        doc = scen([op("add-gold"), {"action": "wait", "ms": 1}, op("add-gold")], teardown=[op("add-gold")])
        path = self.write("die.json", doc)
        runner = Runner(self.cfg, opts=RunOptions(), out=self.lines.append)
        orig = runner.bridge.game_op
        calls = {"n": 0}

        def flaky(*a, **kw):
            calls["n"] += 1
            if calls["n"] == 2:
                self.game.drop_next = 100
            return orig(*a, **kw)
        runner.bridge.game_op = flaky
        res = runner.run_path(path)
        self.game.drop_next = 0
        self.assertEqual(res["exitCode"], EXIT_UNREACHABLE)
        statuses = [(s["phase"], s["status"]) for s in self.steps(res)]
        self.assertEqual(statuses[-1], ("teardown", "skipped"))

    def test_matrix_cases_and_case_filter(self):
        doc = scen([op("add-gold", amount="${amount}"), {"action": "assert", "path": "gold", "gte": "${amount}"}],
                   matrix={"amount": [1, 2, 3]})
        res = self.run_doc(doc)
        self.assertEqual(res["exitCode"], EXIT_OK, self.lines)
        self.assertEqual([c["name"] for c in res["cases"]], ["t[amount=1]", "t[amount=2]", "t[amount=3]"])
        self.assertIn("t_amount=2_", Path(res["cases"][1]["reportPath"]).name)
        res = self.run_doc(doc, case="amount=2")
        self.assertEqual(len(res["cases"]), 1)
        res = self.run_doc(doc, case="amount=9")
        self.assertEqual(res["exitCode"], EXIT_INVALID)

    def test_external_params(self):
        doc = scen([op("add-gold", amount="${bonus}")])
        self.assertEqual(self.run_doc(doc)["exitCode"], EXIT_INVALID)
        self.assertEqual(self.run_doc(doc, params={"bonus": 5})["exitCode"], EXIT_OK)
        self.assertEqual(self.game.game["gold"], 15)


if __name__ == "__main__":
    unittest.main()
