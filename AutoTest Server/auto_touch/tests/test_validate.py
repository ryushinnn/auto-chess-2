from __future__ import annotations

import json
import shutil
import tempfile
import unittest
from pathlib import Path

from auto_touch import schema
from auto_touch.config import PACKAGE_DIR


class ValidateBase(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp, True)

    def write(self, name, doc) -> Path:
        p = self.tmp / name
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(doc if isinstance(doc, str) else json.dumps(doc), encoding="utf-8")
        return p

    def errors(self, doc, name="s.json", external=()):
        return schema.validate_path(self.write(name, doc), [self.tmp], external).errors

    def assertError(self, doc, fragment, **kw):
        errs = self.errors(doc, **kw)
        self.assertTrue(any(fragment in e for e in errs), f"{fragment!r} not in {errs}")


def s(*actions, **top):
    return {"note": "n", **top, "actions": list(actions)}


class TestScenarioSchema(ValidateBase):
    def test_valid_file(self):
        doc = s({"action": "play"}, {"action": "wait", "ms": 10},
                {"action": "op", "op": "add-gold", "params": {"amount": 2}, "saveAs": "r", "expect": {"gold": 12}},
                {"action": "assert", "path": "$r.gold", "gte": 1, "label": "x", "optional": True},
                {"action": "wait_state", "path": "phase", "in": ["shop"], "timeoutMs": 50, "pollMs": 5},
                {"action": "click", "target": "shop:1"}, {"action": "click", "x": 1, "y": 2},
                {"action": "drag", "fromTarget": "bench:0", "to": "Canvas/Board"},
                {"action": "wait_ui", "text": "OK", "state": "interactable"},
                {"action": "assert_ui", "path": "A/B", "expect": {"active": True, "caption": "x"}},
                {"action": "screenshot", "name": "a"}, {"action": "logs", "level": "error", "failOnRed": False},
                {"action": "invariants", "minSeverity": "medium", "rules": ["a"]},
                {"action": "reflect", "op": "call", "type": "T", "method": "M", "args": [1], "expectThrows": True},
                {"action": "menu", "item": "Assets/Refresh"}, {"action": "timescale", "value": 2},
                {"action": "refresh"}, {"action": "state", "path": "gold", "saveAs": "g"},
                {"action": "repeat", "times": 3, "as": "i", "until": {"path": "gold", "gt": "${g}"},
                 "actions": [{"action": "op", "op": "add-item", "params": {"id": "u${i}"}}]},
                setup=[{"action": "op", "op": "x"}], teardown=[{"action": "op", "op": "y", "knownIssue": "B-1"}],
                tags=["a"], requiresPlay=True, timescale=1)
        self.assertEqual(self.errors(doc), [])

    def test_top_level(self):
        self.assertError({"actions": [{"action": "wait", "ms": 1}]}, "'note' is required")
        self.assertError({"note": "n"}, "'actions' is required")
        self.assertError({"note": "n", "actions": []}, "'actions' is required")
        self.assertError(s({"action": "wait", "ms": 1}, actons=[]), "unknown top-level key 'actons'")
        self.assertError(s({"action": "wait", "ms": 1}, timescale=500), "between 0 and 100")
        self.assertError([1, 2], "JSON object")
        self.assertError("{ not json", "invalid JSON")

    def test_comment_keys_and_jsonc_are_allowed(self):
        text = '{\n // comment\n "note": "n", "_todo": "later",\n "actions": [{"action": "wait", "ms": 1, "_why": "x"},],\n}'
        self.assertEqual(self.errors(text), [])

    def test_steps(self):
        self.assertError(s({"action": "clik", "path": "x"}), "did you mean 'click'")
        self.assertError(s({"action": "wait"}), "missing required key 'ms'")
        self.assertError(s({"action": "wait", "ms": "soon"}), "'ms' must be a number")
        self.assertError(s({"action": "click", "path": "x", "expectExcuted": True}), "did you mean 'expectExecuted'")
        self.assertError(s({"action": "click"}), "give exactly one target")
        self.assertError(s({"action": "click", "path": "a", "text": "b"}), "give exactly one target")
        self.assertError(s({"action": "click", "x": 1}), "incomplete target")
        self.assertError(s({"action": "drag", "from": "a"}), "give exactly one destination")
        self.assertError(s({"action": "wait_ui", "path": "a", "state": "visible"}), "'state' must be one of")
        self.assertError(s({"action": "assert_ui", "path": "a", "expect": {"color": 1}}), "not supported")
        self.assertError(s({"action": "reflect", "op": "call", "type": "T"}), "needs 'method'")
        self.assertError(s({"action": "reflect", "op": "zap"}), "'op' must be one of")
        self.assertError(s({"action": "op", "op": "x", "saveAs": "bad name"}), "'saveAs' must be an identifier")
        self.assertError(s({"action": "logs", "allowRedLogs": ["("]}), "bad regex")
        self.assertError(s("not a step"), "must be an object")

    def test_operators(self):
        self.assertError(s({"action": "assert", "path": "a"}), "exactly one operator")
        self.assertError(s({"action": "assert", "path": "a", "gt": 1, "lt": 3}), "exactly one operator")
        self.assertError(s({"action": "assert", "path": "a", "equal": 1}), "did you mean 'equals'")
        self.assertError(s({"action": "assert", "path": "a", "length": "2"}), "'length' takes")
        self.assertError(s({"action": "assert", "path": "a", "length": {"gte": 1, "lte": 3}}), "'length' takes")
        self.assertError(s({"action": "assert", "path": "a", "matches": "("}), "bad regex")
        self.assertError(s({"action": "assert", "path": "a", "in": 3}), "'in' takes")
        self.assertError(s({"action": "assert", "path": "a", "approx": "x"}), "'approx' takes")
        self.assertError(s({"action": "assert", "path": "a", "exists": "yes"}), "'exists' takes")
        self.assertError(s({"action": "assert", "path": "a", "equals": 1, "tolerance": 1}), "only applies to 'approx'")
        self.assertError(s({"action": "repeat", "times": 2, "actions": [{"action": "wait", "ms": 1}],
                            "until": {"path": "a"}}), "exactly one operator")
        self.assertEqual(self.errors(s({"action": "assert", "path": "a", "length": {"gte": 1}})), [])

    def test_placeholders(self):
        self.assertError(s({"action": "wait", "ms": "${delay}"}), "${delay} is never declared")
        self.assertEqual(self.errors(s({"action": "wait", "ms": "${delay}"}, params={"delay": 5})), [])
        self.assertEqual(self.errors(s({"action": "wait", "ms": "${delay}"}), external=["delay"]), [])
        self.assertEqual(self.errors(s({"action": "wait", "ms": "${d}"}, matrix={"d": [1, 2]})), [])
        v = schema.validate_path(self.write("m.json", s({"action": "wait", "ms": 1}, matrix={"d": [1]})), [self.tmp])
        self.assertTrue(any("no step uses: d" in w for w in v.warnings))
        self.assertEqual(v.cases, 1)

    def test_includes(self):
        self.write("sub/give.json", {"note": "sub", "params": {"amount": 1},
                                     "actions": [{"action": "op", "op": "g", "params": {"a": "${amount}"}, "saveAs": "out"}]})
        doc = s({"action": "include", "scenario": "sub/give.json", "params": {"amount": 3}},
                {"action": "assert", "path": "$out.gold", "equals": "${out.gold}"})
        self.assertEqual(self.errors(doc), [])
        self.assertError(s({"action": "include", "scenario": "nope.json"}), "not found")
        self.write("loop_a.json", s({"action": "include", "scenario": "loop_b.json"}))
        self.write("loop_b.json", s({"action": "include", "scenario": "loop_a.json"}))
        self.assertError(json.loads((self.tmp / "loop_a.json").read_text()), "include cycle", name="loop_a.json")
        self.write("broken.json", s({"action": "nope"}))
        self.assertError(s({"action": "include", "scenario": "broken.json"}), "-> broken.json: actions[0]")

    def test_readonly_classification(self):
        self.assertTrue(schema.is_readonly({"action": "assert"}))
        self.assertTrue(schema.is_readonly({"action": "reflect", "op": "get"}))
        self.assertFalse(schema.is_readonly({"action": "reflect", "op": "call"}))
        self.assertFalse(schema.is_readonly({"action": "click"}))
        self.assertFalse(schema.is_readonly({"action": "wait"}))


class TestCampaignSchema(ValidateBase):
    def test_campaign(self):
        self.write("a.json", s({"action": "wait", "ms": 1}))
        self.write("p.json", s({"action": "wait", "ms": "${d}"}))
        ok = {"note": "c", "scenarios": ["a.json", {"path": "p.json", "params": {"d": 1}}], "stopOnFail": True}
        v = schema.validate_path(self.write("c.json", ok), [self.tmp])
        self.assertEqual((v.kind, v.errors, v.cases), ("campaign", [], 2))
        self.assertError({"note": "c", "scenarios": ["missing.json"]}, "scenario not found", name="c.json")
        self.assertError({"note": "c", "scenarios": []}, "non-empty list", name="c.json")
        self.write("c2.json", {"note": "c2", "scenarios": ["a.json"]})
        self.assertError({"note": "c", "scenarios": ["c2.json"]}, "do not nest", name="c.json")
        self.assertError({"note": "c", "scenarios": ["p.json"]}, "${d} is never declared", name="c.json")
        self.assertError({"note": "c", "scenarios": ["a.json"], "freshplay": True}, "did you mean 'freshPlay'", name="c.json")

    def test_shipped_scenarios_are_valid(self):
        folder = PACKAGE_DIR / "scenarios"
        files = sorted(folder.glob("*.json"))
        self.assertTrue(files)
        for f in files:
            v = schema.validate_path(f, [folder])
            self.assertEqual(v.errors, [], f.name)


if __name__ == "__main__":
    unittest.main()
