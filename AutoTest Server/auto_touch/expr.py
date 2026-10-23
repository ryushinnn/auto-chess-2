"""Paths, operators, `${...}` templating and subset matching for scenarios.

Path   `player.bench.0.unitId`, `units[3].hp`, `items.-1` (negative = from the end).
       Prefix `$name` reads a saveAs variable / parameter, `@last` the last op /
       click / drag / reflect result; anything else is read from /state.
Ops    exactly one of OPERATORS per assert / wait_state / until.
Templ. `${name}` or `${name.path}` / `${name[0].x}`; a string that is exactly one
       placeholder keeps the value's type, otherwise the text is spliced in.
"""
from __future__ import annotations

import json
import math
import re
from typing import Any

OPERATORS = ("equals", "notEquals", "gt", "gte", "lt", "lte", "in", "contains",
             "exists", "length", "approx", "matches")
OPERATOR_EXTRAS = ("tolerance",)
DEFAULT_TOLERANCE = 1e-6

MISSING = object()


class ExprError(ValueError):
    """The scenario author wrote something that cannot be evaluated (bad operator,
    bad regex, wrong operand type) - an invalid scenario, not a failed check."""


class Unresolved(KeyError):
    def __init__(self, name: str, expr: str, reason: str = "not defined"):
        super().__init__(name)
        self.name = name
        self.expr = expr
        self.reason = reason

    def __str__(self) -> str:
        return f"unresolved placeholder {self.expr}: {self.reason}"


# ---- paths -------------------------------------------------------------------

_SEG = re.compile(r"\[(-?\d+)\]|([^.\[\]]+)")


def split_path(path: str) -> list:
    parts: list = []
    for idx, name in _SEG.findall(path or ""):
        parts.append(int(idx) if idx else name)
    return parts


def resolve(root: Any, parts) -> Any:
    """Walk `parts` into root; MISSING when any step does not exist."""
    if isinstance(parts, str):
        parts = split_path(parts)
    cur = root
    for p in parts:
        if isinstance(cur, dict):
            key = p if isinstance(p, str) else str(p)
            if key not in cur:
                return MISSING
            cur = cur[key]
        elif isinstance(cur, (list, tuple)):
            try:
                i = int(p)
            except (TypeError, ValueError):
                return MISSING
            if not -len(cur) <= i < len(cur):
                return MISSING
            cur = cur[i]
        else:
            return MISSING
    return cur


def path_root(path: str) -> tuple[str, str | None, list]:
    """("state", None, parts) | ("var", name, parts) | ("last", None, parts)."""
    path = (path or "").strip()
    if path.startswith("@last"):
        return "last", None, split_path(path[5:].lstrip("."))
    if path.startswith("$"):
        parts = split_path(path[1:])
        if not parts or not isinstance(parts[0], str):
            raise ExprError(f"bad variable path {path!r}")
        return "var", parts[0], parts[1:]
    return "state", None, split_path(path)


# ---- values ------------------------------------------------------------------

def is_num(v: Any) -> bool:
    return isinstance(v, (int, float)) and not isinstance(v, bool)


def deep_equal(a: Any, b: Any) -> bool:
    if isinstance(a, bool) or isinstance(b, bool):
        return isinstance(a, bool) and isinstance(b, bool) and a == b
    if is_num(a) and is_num(b):
        return a == b
    if isinstance(a, dict) and isinstance(b, dict):
        return a.keys() == b.keys() and all(deep_equal(a[k], b[k]) for k in a)
    if isinstance(a, (list, tuple)) and isinstance(b, (list, tuple)):
        return len(a) == len(b) and all(deep_equal(x, y) for x, y in zip(a, b))
    return type(a) is type(b) and a == b


def short(v: Any, limit: int = 120) -> str:
    if v is MISSING:
        return "<missing>"
    try:
        s = json.dumps(v, ensure_ascii=False, separators=(",", ":"), default=str)
    except (TypeError, ValueError):
        s = repr(v)
    return s if len(s) <= limit else s[: limit - 3] + "..."


# ---- operators ---------------------------------------------------------------

def pick_operator(spec: dict) -> tuple[str, Any]:
    found = [k for k in OPERATORS if k in spec]
    if len(found) != 1:
        raise ExprError("needs exactly one operator (" + ", ".join(OPERATORS) + "), got "
                        + (", ".join(found) if found else "none"))
    return found[0], spec[found[0]]


def describe(op: str, expected: Any) -> str:
    return f"{op} {short(expected)}"


def apply_operator(op: str, expected: Any, value: Any, found: bool = True,
                   tolerance: Any = None) -> tuple[bool, str]:
    """(ok, detail). `found` False means the path did not exist."""
    if op == "exists":
        want = bool(expected)
        ok = found == want
        return ok, ("exists" if found else "does not exist") + ("" if ok else f", expected exists {str(want).lower()}")
    if not found or value is MISSING:
        return False, f"path not found (expected {describe(op, expected)})"
    got = short(value)
    if op == "equals":
        ok = deep_equal(value, expected)
    elif op == "notEquals":
        ok = not deep_equal(value, expected)
    elif op in ("gt", "gte", "lt", "lte"):
        comparable = (is_num(value) and is_num(expected)) or (isinstance(value, str) and isinstance(expected, str))
        if not comparable:
            if not (is_num(expected) or isinstance(expected, str)):
                raise ExprError(f"{op} needs a number or string operand, got {short(expected)}")
            return False, f"= {got}: cannot compare with {short(expected)}"
        ok = {"gt": value > expected, "gte": value >= expected,
              "lt": value < expected, "lte": value <= expected}[op]
    elif op == "in":
        if isinstance(expected, list):
            ok = any(deep_equal(value, e) for e in expected)
        elif isinstance(expected, str):
            ok = isinstance(value, str) and value in expected
        else:
            raise ExprError(f"'in' needs a list (or string), got {short(expected)}")
    elif op == "contains":
        if isinstance(value, (list, tuple)):
            ok = any(deep_equal(e, expected) for e in value)
        elif isinstance(value, str):
            ok = isinstance(expected, str) and expected in value
        elif isinstance(value, dict):
            ok = isinstance(expected, str) and expected in value
        else:
            return False, f"= {got}: not a list, string or object"
    elif op == "length":
        if not isinstance(value, (list, tuple, str, dict)):
            return False, f"= {got}: has no length"
        n = len(value)
        if isinstance(expected, dict):
            sub_op, sub_exp = pick_operator(expected)
            if sub_op in ("length", "exists"):
                raise ExprError(f"length cannot nest {sub_op}")
            ok, detail = apply_operator(sub_op, sub_exp, n, True, expected.get("tolerance"))
            return ok, f"length {n}" + ("" if ok else f", expected length {describe(sub_op, sub_exp)}")
        if not is_num(expected) or int(expected) != expected:
            raise ExprError(f"length needs an integer or an operator object, got {short(expected)}")
        ok = n == int(expected)
        return ok, f"length {n}" + ("" if ok else f", expected {int(expected)}")
    elif op == "approx":
        if not is_num(expected):
            raise ExprError(f"approx needs a number, got {short(expected)}")
        tol = DEFAULT_TOLERANCE if tolerance is None else tolerance
        if not is_num(tol) or tol < 0:
            raise ExprError(f"tolerance must be a non-negative number, got {short(tol)}")
        if not is_num(value):
            return False, f"= {got}: not a number"
        ok = math.isfinite(value) and abs(value - expected) <= tol
        return ok, f"= {got}" + ("" if ok else f", expected {expected} +/- {tol}")
    elif op == "matches":
        if not isinstance(expected, str):
            raise ExprError(f"matches needs a regex string, got {short(expected)}")
        try:
            rx = re.compile(expected)
        except re.error as e:
            raise ExprError(f"bad regex {expected!r}: {e}") from None
        text = value if isinstance(value, str) else short(value, 100000)
        ok = rx.search(text) is not None
    else:
        raise ExprError(f"unknown operator {op!r}")
    return ok, f"= {got}" + ("" if ok else f", expected {describe(op, expected)}")


def check(spec: dict, value: Any, found: bool = True) -> tuple[bool, str]:
    op, expected = pick_operator(spec)
    return apply_operator(op, expected, value, found, spec.get("tolerance"))


def is_operator_dict(d: Any) -> bool:
    return (isinstance(d, dict) and bool(d)
            and all(k in OPERATORS or k in OPERATOR_EXTRAS for k in d)
            and any(k in OPERATORS for k in d))


def subset_match(expected: Any, actual: Any, where: str = "result") -> list[str]:
    """Mismatch messages (empty = match). Objects: every expected key must match
    (extra actual keys are fine). Lists: same length, element-wise. An object made
    only of operator keys ({"gte": 3}) is applied as an operator."""
    if is_operator_dict(expected):
        ok, detail = check(expected, actual, actual is not MISSING)
        return [] if ok else [f"{where} {detail}"]
    if isinstance(expected, dict):
        if not isinstance(actual, dict):
            return [f"{where}: expected an object, got {short(actual)}"]
        out: list[str] = []
        for k, v in expected.items():
            if k not in actual:
                out.append(f"{where}.{k}: missing")
            else:
                out.extend(subset_match(v, actual[k], f"{where}.{k}"))
        return out
    if isinstance(expected, list):
        if not isinstance(actual, list):
            return [f"{where}: expected a list, got {short(actual)}"]
        if len(actual) != len(expected):
            return [f"{where}: length {len(actual)}, expected {len(expected)}"]
        out = []
        for i, (e, a) in enumerate(zip(expected, actual)):
            out.extend(subset_match(e, a, f"{where}[{i}]"))
        return out
    return [] if deep_equal(expected, actual) else [f"{where} = {short(actual)}, expected {short(expected)}"]


# ---- templating --------------------------------------------------------------

PLACEHOLDER = re.compile(r"\$\{([A-Za-z_][A-Za-z0-9_]*)((?:\.[^.{}\[\]]+|\[-?\d+\])*)\}")


def placeholder_names(value: Any) -> set[str]:
    out: set[str] = set()
    if isinstance(value, str):
        out.update(m.group(1) for m in PLACEHOLDER.finditer(value))
    elif isinstance(value, list):
        for v in value:
            out |= placeholder_names(v)
    elif isinstance(value, dict):
        for v in value.values():
            out |= placeholder_names(v)
    return out


def _lookup(ctx: dict, m: re.Match) -> Any:
    name, rest = m.group(1), m.group(2)
    if name not in ctx:
        raise Unresolved(name, m.group(0))
    v = ctx[name]
    if rest:
        v = resolve(v, split_path(rest.lstrip(".")))
        if v is MISSING:
            raise Unresolved(name, m.group(0), f"path {rest.lstrip('.')!r} not found in ${{{name}}}")
    return v


def to_text(v: Any) -> str:
    if isinstance(v, bool):
        return "true" if v else "false"
    if v is None:
        return "null"
    if isinstance(v, (dict, list)):
        return json.dumps(v, ensure_ascii=False, separators=(",", ":"))
    return str(v)


def render(value: Any, ctx: dict) -> Any:
    """Deep copy of value with placeholders resolved from ctx (raises Unresolved)."""
    if isinstance(value, str):
        if "${" not in value:
            return value
        m = PLACEHOLDER.fullmatch(value)
        if m:
            return _lookup(ctx, m)
        return PLACEHOLDER.sub(lambda mm: to_text(_lookup(ctx, mm)), value)
    if isinstance(value, list):
        return [render(v, ctx) for v in value]
    if isinstance(value, dict):
        return {k: render(v, ctx) for k, v in value.items()}
    return value


def is_template(v: Any) -> bool:
    return isinstance(v, str) and PLACEHOLDER.search(v) is not None
