"""Scenario matrix: one file, many cases.

    "matrix": {"amount": [1, 5], "phase": ["shop", "combat"]}   cartesian product (4 cases)
    "matrix": [{"label": "rich", "gold": 99}, {"gold": 0}]      explicit cases
    "matrixSkip": [{"amount": 5, "phase": "combat"}]            drop matching combinations

Each case's parameters become `${name}` placeholders for the whole run (they are
rendered step by step, together with saveAs variables, see expr.render). A file
without `matrix` is a single case with an empty label.
"""
from __future__ import annotations

import itertools
import re
from dataclasses import dataclass, field

MAX_CASES = 256


class MatrixError(ValueError):
    pass


@dataclass
class Case:
    params: dict = field(default_factory=dict)
    label: str = ""
    index: int = 0
    total: int = 1

    def name(self, stem: str) -> str:
        return f"{stem}[{self.label}]" if self.label else stem

    @property
    def file_suffix(self) -> str:
        if not self.label:
            return ""
        s = re.sub(r"[^A-Za-z0-9=._-]+", "_", self.label.replace(",", "_")).strip("_")
        return "_" + s[:60]


def _short(v) -> str:
    s = str(v).lower() if isinstance(v, bool) else str(v)
    return s if len(s) <= 24 else s[:21] + "..."


def case_label(params: dict) -> str:
    return ",".join(f"{k}={_short(v)}" for k, v in params.items() if k != "label")


def cases_of(doc: dict) -> list[dict]:
    """The parameter dicts of a scenario ([{}] when it has no matrix)."""
    matrix = doc.get("matrix") if isinstance(doc, dict) else None
    if matrix is None:
        return [{}]
    skip = doc.get("matrixSkip") or []
    if not isinstance(skip, list) or not all(isinstance(s, dict) and s for s in skip):
        raise MatrixError("'matrixSkip' must be a list of non-empty {param: value} objects")
    if isinstance(matrix, dict):
        if not matrix:
            raise MatrixError("'matrix' must not be empty")
        for k, values in matrix.items():
            if k == "label":
                raise MatrixError("'label' cannot be a matrix parameter (use the list form)")
            if not isinstance(values, list) or not values:
                raise MatrixError(f"matrix.{k} must be a non-empty list of values")
        keys = list(matrix)
        cases = [dict(zip(keys, combo)) for combo in itertools.product(*(matrix[k] for k in keys))]
    elif isinstance(matrix, list):
        if not matrix or not all(isinstance(c, dict) and [k for k in c if k != "label"] for c in matrix):
            raise MatrixError("'matrix' as a list must hold {param: value} objects (plus optional 'label')")
        cases = [dict(c) for c in matrix]
    else:
        raise MatrixError("'matrix' must be an object of lists or a list of case objects")
    if skip:
        from .expr import deep_equal
        cases = [c for c in cases
                 if not any(all(k in c and deep_equal(c[k], v) for k, v in s.items()) for s in skip)]
        if not cases:
            raise MatrixError("matrixSkip removed every case")
    if len(cases) > MAX_CASES:
        raise MatrixError(f"matrix expands to {len(cases)} cases (max {MAX_CASES}) - split the file")
    return cases


def declared_params(doc: dict) -> set[str]:
    names: set[str] = set()
    try:
        for c in cases_of(doc):
            names |= {k for k in c if k != "label"}
    except MatrixError:
        pass
    return names


def expand(doc: dict) -> list[Case]:
    params_list = cases_of(doc)
    if params_list == [{}]:
        return [Case()]
    out: list[Case] = []
    labels: set[str] = set()
    for i, p in enumerate(params_list):
        label = str(p.get("label") or case_label(p))
        if label in labels:
            label = f"{label}#{i}"
        labels.add(label)
        out.append(Case(params={k: v for k, v in p.items() if k != "label"}, label=label,
                        index=i, total=len(params_list)))
    return out


def select(cases: list[Case], expr: str | None) -> list[Case]:
    """--case filter: `k=v,k2=v2` (all must match, compared as text) or a substring
    of the case label."""
    if not expr:
        return cases
    if "=" in expr:
        wanted = dict(part.split("=", 1) for part in expr.split(",") if "=" in part)
        picked = [c for c in cases
                  if all(_short(c.params.get(k)) == v or str(c.params.get(k)) == v for k, v in wanted.items())]
    else:
        picked = [c for c in cases if expr in c.label]
    if not picked:
        raise MatrixError(f"--case {expr!r} matches none of: "
                          + ", ".join(c.label or "<single case>" for c in cases))
    return picked
