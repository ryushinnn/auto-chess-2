from __future__ import annotations

import unittest

from auto_touch import expr
from auto_touch.expr import MISSING, ExprError, Unresolved, apply_operator, check, render, resolve, subset_match

STATE = {"player": {"gold": 7, "name": "Ann", "bench": [{"unitId": "knight", "hp": 10.5}, {"unitId": "mage"}]},
         "units": [1, 2, 3, 4], "flag": True, "empty": []}


class TestPaths(unittest.TestCase):
    def test_forms(self):
        self.assertEqual(resolve(STATE, "player.gold"), 7)
        self.assertEqual(resolve(STATE, "player.bench.0.unitId"), "knight")
        self.assertEqual(resolve(STATE, "player.bench[1].unitId"), "mage")
        self.assertEqual(resolve(STATE, "units[3]"), 4)
        self.assertEqual(resolve(STATE, "units.-1"), 4)
        self.assertIs(resolve(STATE, "units[9]"), MISSING)
        self.assertIs(resolve(STATE, "player.nope"), MISSING)
        self.assertIs(resolve(STATE, "player.gold.x"), MISSING)

    def test_roots(self):
        self.assertEqual(expr.path_root("$saved.a[0]"), ("var", "saved", ["a", 0]))
        self.assertEqual(expr.path_root("@last.gold"), ("last", None, ["gold"]))
        self.assertEqual(expr.path_root("@last"), ("last", None, []))
        self.assertEqual(expr.path_root("player.gold"), ("state", None, ["player", "gold"]))


class TestOperators(unittest.TestCase):
    def ok(self, op, expected, value, **kw):
        res, detail = apply_operator(op, expected, value, **kw)
        self.assertTrue(res, f"{op} {expected!r} on {value!r}: {detail}")

    def ko(self, op, expected, value, **kw):
        res, detail = apply_operator(op, expected, value, **kw)
        self.assertFalse(res, f"{op} {expected!r} on {value!r} should fail")
        return detail

    def test_equals_family(self):
        self.ok("equals", 7, 7)
        self.ok("equals", 7, 7.0)
        self.ok("equals", {"a": [1, 2]}, {"a": [1, 2]})
        self.ko("equals", 1, True)
        self.ko("equals", "7", 7)
        self.ok("notEquals", "a", "b")
        self.ko("notEquals", 3, 3)

    def test_comparisons(self):
        self.ok("gt", 5, 7)
        self.ko("gt", 7, 7)
        self.ok("gte", 7, 7)
        self.ok("lt", 8, 7)
        self.ok("lte", 7, 7)
        self.ko("lte", 6, 7)
        self.ok("gt", "a", "b")
        self.assertIn("cannot compare", self.ko("gt", 5, "x"))

    def test_in_contains(self):
        self.ok("in", ["shop", "combat"], "shop")
        self.ko("in", ["shop"], "result")
        self.ok("in", "shop,combat", "shop")
        self.ok("contains", "mage", ["knight", "mage"])
        self.ok("contains", {"unitId": "mage"}, [{"unitId": "mage"}])
        self.ok("contains", "ni", "knight")
        self.ok("contains", "gold", {"gold": 1})
        self.ko("contains", "x", ["a"])
        with self.assertRaises(ExprError):
            apply_operator("in", 5, 5)

    def test_exists(self):
        self.ok("exists", True, 1, found=True)
        self.ok("exists", False, MISSING, found=False)
        self.ko("exists", True, MISSING, found=False)
        self.ko("exists", False, None, found=True)

    def test_length(self):
        self.ok("length", 4, [1, 2, 3, 4])
        self.ok("length", 3, "abc")
        self.ok("length", {"gte": 2}, [1, 2])
        self.ok("length", {"lt": 1}, [])
        self.ko("length", {"gt": 2}, [1, 2])
        self.ko("length", 1, 5)
        with self.assertRaises(ExprError):
            apply_operator("length", "two", [1, 2])

    def test_approx_and_matches(self):
        self.ok("approx", 10.5, 10.5000001)
        self.ok("approx", 10, 10.4, tolerance=0.5)
        self.ko("approx", 10, 10.6, tolerance=0.5)
        self.ko("approx", 10, "10")
        self.ok("matches", r"^kn", "knight")
        self.ok("matches", r"mage", ["knight", "mage"])
        self.ko("matches", r"^z", "knight")
        with self.assertRaises(ExprError):
            apply_operator("matches", "(", "x")

    def test_missing_path_fails_non_exists_operators(self):
        detail = self.ko("equals", 1, MISSING, found=False)
        self.assertIn("not found", detail)

    def test_check_needs_exactly_one_operator(self):
        self.assertTrue(check({"path": "x", "gte": 1}, 3)[0])
        with self.assertRaises(ExprError):
            check({"path": "x"}, 3)
        with self.assertRaises(ExprError):
            check({"path": "x", "gte": 1, "lte": 5}, 3)


class TestSubset(unittest.TestCase):
    def test_subset(self):
        actual = {"gold": 12, "items": ["a", "b"], "meta": {"x": 1, "y": 2}}
        self.assertEqual(subset_match({"gold": 12}, actual), [])
        self.assertEqual(subset_match({"meta": {"x": 1}}, actual), [])
        self.assertEqual(subset_match({"items": ["a", "b"]}, actual), [])
        self.assertEqual(subset_match({"gold": {"gte": 10}}, actual), [])
        self.assertTrue(subset_match({"gold": 11}, actual))
        self.assertTrue(subset_match({"nope": 1}, actual))
        self.assertTrue(subset_match({"items": ["a"]}, actual))
        self.assertTrue(subset_match({"meta": 1}, actual))
        self.assertEqual(subset_match(5, 5), [])


class TestTemplating(unittest.TestCase):
    CTX = {"n": 3, "flag": True, "name": "Ann", "res": {"gold": 12, "items": ["a", "b"]}, "none": None}

    def test_exact_placeholder_keeps_type(self):
        self.assertEqual(render("${n}", self.CTX), 3)
        self.assertIs(render("${flag}", self.CTX), True)
        self.assertEqual(render("${res}", self.CTX), {"gold": 12, "items": ["a", "b"]})
        self.assertEqual(render("${res.gold}", self.CTX), 12)
        self.assertEqual(render("${res.items[1]}", self.CTX), "b")
        self.assertIsNone(render("${none}", self.CTX))

    def test_embedded_placeholders_become_text(self):
        self.assertEqual(render("gold=${res.gold}, on=${flag}", self.CTX), "gold=12, on=true")
        self.assertEqual(render({"a": ["x${n}", {"b": "${name}"}]}, self.CTX), {"a": ["x3", {"b": "Ann"}]})
        self.assertEqual(render("no placeholders", self.CTX), "no placeholders")

    def test_unresolved(self):
        with self.assertRaises(Unresolved) as cm:
            render("${missing}", self.CTX)
        self.assertEqual(cm.exception.name, "missing")
        with self.assertRaises(Unresolved):
            render("x ${res.nope}", self.CTX)

    def test_placeholder_names(self):
        self.assertEqual(expr.placeholder_names({"a": "${x} ${y.z}", "b": ["${w[0]}"]}), {"x", "y", "w"})


if __name__ == "__main__":
    unittest.main()
