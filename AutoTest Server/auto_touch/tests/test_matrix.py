from __future__ import annotations

import unittest

from auto_touch import matrix
from auto_touch.matrix import MatrixError


class TestMatrix(unittest.TestCase):
    def test_plain_file_is_one_case(self):
        cases = matrix.expand({"actions": []})
        self.assertEqual(len(cases), 1)
        self.assertEqual(cases[0].label, "")
        self.assertEqual(cases[0].name("x"), "x")
        self.assertEqual(cases[0].file_suffix, "")

    def test_cartesian_product(self):
        cases = matrix.expand({"matrix": {"a": [1, 2], "b": ["x", "y", "z"]}})
        self.assertEqual(len(cases), 6)
        self.assertEqual(cases[0].params, {"a": 1, "b": "x"})
        self.assertEqual(cases[0].label, "a=1,b=x")
        self.assertEqual(cases[0].name("s"), "s[a=1,b=x]")
        self.assertEqual(cases[0].file_suffix, "_a=1_b=x")
        self.assertEqual(cases[-1].params, {"a": 2, "b": "z"})
        self.assertTrue(all(c.total == 6 for c in cases))

    def test_explicit_cases_and_labels(self):
        cases = matrix.expand({"matrix": [{"label": "rich", "gold": 99}, {"gold": 0}]})
        self.assertEqual([c.label for c in cases], ["rich", "gold=0"])
        self.assertEqual(cases[0].params, {"gold": 99})

    def test_skip(self):
        doc = {"matrix": {"a": [1, 2], "b": [1, 2]}, "matrixSkip": [{"a": 2, "b": 2}, {"a": 1, "b": 1}]}
        self.assertEqual([c.params for c in matrix.expand(doc)], [{"a": 1, "b": 2}, {"a": 2, "b": 1}])
        with self.assertRaises(MatrixError):
            matrix.expand({"matrix": {"a": [1]}, "matrixSkip": [{"a": 1}]})

    def test_select(self):
        cases = matrix.expand({"matrix": {"a": [1, 2], "on": [True, False]}})
        self.assertEqual(len(matrix.select(cases, "a=2")), 2)
        self.assertEqual(len(matrix.select(cases, "a=2,on=true")), 1)
        self.assertEqual(len(matrix.select(cases, "on=false")), 2)
        self.assertEqual(len(matrix.select(cases, None)), 4)
        with self.assertRaises(MatrixError):
            matrix.select(cases, "a=9")

    def test_errors(self):
        for bad in ({"matrix": {}}, {"matrix": {"a": []}}, {"matrix": {"a": 1}}, {"matrix": 5},
                    {"matrix": [{}]}, {"matrix": {"label": [1]}}, {"matrix": {"a": list(range(300))}}):
            with self.assertRaises(MatrixError, msg=str(bad)):
                matrix.cases_of(bad)

    def test_declared_params(self):
        self.assertEqual(matrix.declared_params({"matrix": [{"label": "x", "a": 1}, {"b": 2}]}), {"a", "b"})


if __name__ == "__main__":
    unittest.main()
