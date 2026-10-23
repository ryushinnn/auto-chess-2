"""Offline validation of scenario and campaign files (no Unity needed).

`py -m auto_touch validate <file>` and the runner's pre-run check both call
validate_path(). Errors make the file invalid (exit 3); warnings are printed only.
Includes are resolved and validated recursively; `${name}` placeholders must be
declared somewhere: a matrix parameter, `params` (defaults), a `saveAs`, a
`repeat.as`, an include's `params`, or a parameter supplied from outside (campaign
`params`, `run --param`).
"""
from __future__ import annotations

import difflib
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Callable, Iterable

from . import expr, matrix
from .config import SERVER_DIR, ConfigError, load_jsonc_file

UI_STATES = ("exists", "active", "interactable", "inactive", "absent")
LOG_LEVELS = ("error", "warning", "all")
SEVERITIES = ("high", "medium", "low")
REFLECT_OPS = ("types", "members", "get", "set", "call")
NAME_RX = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")
MAX_INCLUDE_DEPTH = 8

TOP_KEYS = {"note", "tags", "requiresPlay", "scene", "timescale", "setup", "actions",
            "teardown", "matrix", "matrixSkip", "params", "$schema"}
CAMPAIGN_KEYS = {"note", "tags", "scenarios", "stopOnFail", "freshPlay", "params", "$schema"}
COMMON_KEYS = {"action", "label", "optional", "knownIssue", "allowRedLogs", "timeoutMs"}


class SchemaError(ValueError):
    pass


# ---- type checkers -----------------------------------------------------------

def _num(v): return expr.is_num(v)
def _int(v): return expr.is_num(v) and float(v).is_integer()
def _bool(v): return isinstance(v, bool)
def _str(v): return isinstance(v, str)
def _nonempty_str(v): return isinstance(v, str) and v.strip() != ""
def _dict(v): return isinstance(v, dict)
def _list(v): return isinstance(v, list)
def _any(v): return True
def _name(v): return isinstance(v, str) and NAME_RX.match(v) is not None
def _bool_or_str(v): return isinstance(v, (bool, str))
def _str_or_list(v): return isinstance(v, str) or (isinstance(v, list) and all(isinstance(x, str) for x in v))
def _bool_or_list(v): return isinstance(v, bool) or (isinstance(v, list) and all(isinstance(x, str) for x in v))


TYPE_NAMES: dict[Callable, str] = {
    _num: "a number", _int: "an integer", _bool: "true/false", _str: "a string",
    _nonempty_str: "a non-empty string", _dict: "an object", _list: "a list", _any: "any value",
    _name: "an identifier (letters, digits, _)", _bool_or_str: "true/false or a string",
    _str_or_list: "a string or a list of strings", _bool_or_list: "true/false or a list of regexes",
}

KEY_TYPES: dict[str, Callable] = {
    "label": _str, "optional": _bool, "knownIssue": _nonempty_str, "allowRedLogs": _bool_or_list,
    "timeoutMs": _num, "ms": _num, "on": _bool, "scene": _str, "value": _num, "exitPlay": _bool,
    "op": _nonempty_str, "params": _dict, "expect": _any, "expectError": _bool_or_str,
    "saveAs": _name, "path": _str, "tolerance": _num, "pollMs": _num, "target": _str,
    "text": _str, "x": _num, "y": _num, "x0": _num, "y0": _num, "x1": _num, "y1": _num,
    "from": _str, "to": _str, "fromTarget": _str, "toTarget": _str, "steps": _int, "frames": _int,
    "expectExecuted": _bool, "match": _str, "state": _str, "name": _str, "superSize": _int,
    "level": _str, "failOnRed": _bool, "minSeverity": _str, "rules": _str_or_list, "type": _str,
    "member": _str, "method": _str, "args": _any, "static": _bool, "expectThrows": _bool_or_str,
    "item": _nonempty_str, "times": _int, "actions": _list, "until": _dict, "untilRequired": _bool,
    "as": _name, "scenario": _nonempty_str,
}
# (action, key) -> checker, where one key means different things in different actions
TYPE_OVERRIDES: dict[tuple[str, str], Callable] = {
    ("reflect", "value"): _any, ("assert_ui", "expect"): _dict,
}
NO_TEMPLATE = {"saveAs", "as", "action", "actions"}


@dataclass
class ActionSpec:
    req: frozenset = frozenset()
    opt: frozenset = frozenset()
    operator: bool = False
    groups: tuple = ()            # tuple of alternative tuples - exactly one must be present
    groups2: tuple = ()           # a second independent alternative set (drag "to")
    readonly: bool = False

    def keys(self) -> set:
        out = set(COMMON_KEYS) | set(self.req) | set(self.opt)
        for g in self.groups + self.groups2:
            out |= set(g)
        if self.operator:
            out |= set(expr.OPERATORS) | set(expr.OPERATOR_EXTRAS)
        return out


def _spec(req=(), opt=(), operator=False, groups=(), groups2=(), readonly=False) -> ActionSpec:
    return ActionSpec(frozenset(req), frozenset(opt), operator, tuple(groups), tuple(groups2), readonly)


UI_SELECTOR = (("path",), ("text",), ("match",))
ACTIONS: dict[str, ActionSpec] = {
    "play": _spec(opt=("on", "scene")),
    "wait": _spec(req=("ms",)),
    "timescale": _spec(req=("value",)),
    "refresh": _spec(opt=("exitPlay",)),
    "op": _spec(req=("op",), opt=("params", "expect", "expectError", "saveAs")),
    "state": _spec(opt=("path", "saveAs"), readonly=True),
    "assert": _spec(req=("path",), operator=True, readonly=True),
    "wait_state": _spec(req=("path",), opt=("pollMs",), operator=True),
    "click": _spec(opt=("expectExecuted",), groups=(("path",), ("text",), ("target",), ("x", "y"))),
    "drag": _spec(opt=("steps", "frames", "expect", "expectExecuted"),
                  groups=(("from",), ("fromTarget",), ("x0", "y0")),
                  groups2=(("to",), ("toTarget",), ("x1", "y1"))),
    "wait_ui": _spec(opt=("state", "pollMs"), groups=UI_SELECTOR),
    "assert_ui": _spec(req=("expect",), groups=UI_SELECTOR, readonly=True),
    "screenshot": _spec(opt=("name", "superSize"), readonly=True),
    "logs": _spec(opt=("level", "failOnRed", "match"), readonly=True),
    "invariants": _spec(opt=("minSeverity", "rules"), readonly=True),
    "reflect": _spec(req=("op",), opt=("type", "member", "method", "args", "value", "static", "path",
                                       "expect", "expectThrows", "saveAs")),
    "menu": _spec(req=("item",)),
    "repeat": _spec(req=("times", "actions"), opt=("until", "untilRequired", "as"), readonly=True),
    "include": _spec(req=("scenario",), opt=("params",), readonly=True),
}


def is_readonly(step: dict) -> bool:
    """Steps after which the red-log / invariant watchdogs do not run."""
    action = step.get("action")
    spec = ACTIONS.get(action)
    if spec is None:
        return True
    if action == "reflect":
        return step.get("op") in ("types", "members", "get")
    return spec.readonly


# ---- results -----------------------------------------------------------------

@dataclass
class Validation:
    path: str = ""
    kind: str = "scenario"
    errors: list = field(default_factory=list)
    warnings: list = field(default_factory=list)
    declared: set = field(default_factory=set)      # every name a placeholder may use
    saved: set = field(default_factory=set)         # saveAs / repeat.as names (runtime variables)
    includes: list = field(default_factory=list)
    cases: int = 1

    @property
    def ok(self) -> bool:
        return not self.errors

    def to_dict(self) -> dict:
        return {"path": self.path, "kind": self.kind, "ok": self.ok, "errors": self.errors,
                "warnings": self.warnings, "cases": self.cases,
                "includes": [str(p) for p in self.includes]}


# ---- file helpers ------------------------------------------------------------

def load_doc(path: str | Path) -> Any:
    try:
        return load_jsonc_file(path)
    except ConfigError as e:
        raise SchemaError(str(e)) from None


def kind_of(doc: Any) -> str:
    if isinstance(doc, dict) and "scenarios" in doc and "actions" not in doc:
        return "campaign"
    return "scenario"


def resolve_ref(ref: str, base_dir: Path | None, search_dirs: Iterable[Path]) -> Path | None:
    p = Path(ref)
    if p.is_absolute():
        return p if p.is_file() else None
    candidates = ([base_dir / p] if base_dir else []) + [Path(d) / p for d in search_dirs]
    for c in candidates:
        if c.is_file():
            return c.resolve()
    if not p.suffix:
        return resolve_ref(ref + ".json", base_dir, search_dirs)
    return None


def default_search_dirs(cfg=None) -> list[Path]:
    dirs: list[Path] = []
    if cfg is not None:
        dirs.append(cfg.scenarios_path)
    dirs.append(SERVER_DIR)
    return dirs


def _suggest(word: str, choices: Iterable[str]) -> str:
    close = difflib.get_close_matches(word, list(choices), n=1, cutoff=0.6)
    return f" (did you mean {close[0]!r}?)" if close else ""


# ---- scenario validation -----------------------------------------------------

def _collect_saved(steps: Any, out: set) -> None:
    if not isinstance(steps, list):
        return
    for s in steps:
        if not isinstance(s, dict):
            continue
        for k in ("saveAs", "as"):
            if isinstance(s.get(k), str):
                out.add(s[k])
        if s.get("action") == "repeat":
            _collect_saved(s.get("actions"), out)


def validate_scenario(doc: Any, path: str | Path = "<scenario>", search_dirs: Iterable[Path] = (),
                      external: Iterable[str] = (), _stack: tuple = ()) -> Validation:
    path = Path(path) if not isinstance(path, Path) else path
    v = Validation(path=str(path), kind="scenario")
    if not isinstance(doc, dict):
        v.errors.append("a scenario must be a JSON object with 'note' and 'actions'")
        return v
    search_dirs = list(search_dirs)
    err = v.errors.append

    for k in doc:
        if k not in TOP_KEYS and not _is_comment_key(k):
            err(f"unknown top-level key {k!r}{_suggest(k, TOP_KEYS)}")
    if not _nonempty_str(doc.get("note")):
        err("'note' is required: a non-empty string saying what the scenario proves")
    if "tags" in doc and not _str_or_list(doc["tags"]):
        err("'tags' must be a list of strings")
    if "requiresPlay" in doc and not _bool(doc["requiresPlay"]):
        err("'requiresPlay' must be true/false")
    if "scene" in doc and not (_str(doc["scene"])):
        err("'scene' must be an asset path string")
    if doc.get("scene") and doc.get("requiresPlay") is False:
        v.warnings.append("'scene' is only used when requiresPlay is true (it is opened on entering Play)")
    if "timescale" in doc and not expr.is_template(doc["timescale"]):
        ts = doc["timescale"]
        if not _num(ts) or not 0 <= ts <= 100:
            err("'timescale' must be a number between 0 and 100")
    if "params" in doc and not _dict(doc["params"]):
        err("'params' must be an object of default parameter values")
    actions = doc.get("actions")
    if not isinstance(actions, list) or not actions:
        err("'actions' is required: a non-empty list of steps")
    for phase in ("setup", "teardown"):
        if phase in doc and not _list(doc[phase]):
            err(f"'{phase}' must be a list of steps")
    try:
        v.cases = len(matrix.cases_of(doc))
    except matrix.MatrixError as e:
        err(str(e))

    own_saved: set = set()
    for phase in ("setup", "actions", "teardown"):
        _collect_saved(doc.get(phase), own_saved)
    defaults = doc.get("params") if _dict(doc.get("params")) else {}
    declared = set(external) | matrix.declared_params(doc) | set(defaults) | own_saved
    used: dict[str, str] = {}
    me = str(path.resolve()) if path.exists() else str(path)
    state = {"declared": declared, "saved": set(own_saved), "used": used, "stack": _stack + (me,),
             "base": path.parent if path.exists() else None, "dirs": search_dirs, "v": v}
    for phase in ("setup", "actions", "teardown"):
        steps = doc.get(phase)
        if isinstance(steps, list):
            _walk(steps, phase, state)
    for k in ("scene", "timescale"):
        for name in expr.placeholder_names(doc.get(k)):
            used.setdefault(name, k)

    for name, where in sorted(used.items()):
        if name not in state["declared"]:
            err(f"{where}: placeholder ${{{name}}} is never declared (matrix parameter, params, "
                "saveAs, repeat.as or include params)")
    unused = matrix.declared_params(doc) - set(used)
    if unused:
        v.warnings.append("matrix parameters no step uses: " + ", ".join(sorted(unused)))
    v.declared = state["declared"]
    v.saved = state["saved"]
    return v


def _is_comment_key(k: str) -> bool:
    return k.startswith("_") or k.startswith("//")


def _walk(steps: list, where: str, st: dict) -> None:
    for i, step in enumerate(steps):
        _check_step(step, f"{where}[{i}]", st)


def _check_step(step: Any, where: str, st: dict) -> None:
    v: Validation = st["v"]
    err = v.errors.append
    if not isinstance(step, dict):
        err(f"{where}: a step must be an object with an 'action'")
        return
    action = step.get("action")
    if not isinstance(action, str) or action not in ACTIONS:
        err(f"{where}: unknown action {action!r}{_suggest(str(action), ACTIONS)}; "
            "known: " + ", ".join(ACTIONS))
        return
    spec = ACTIONS[action]
    where = f"{where} {action}"
    allowed = spec.keys()
    for k in step:
        if k not in allowed and not _is_comment_key(k):
            err(f"{where}: unknown key {k!r}{_suggest(k, allowed)}")
    for k in spec.req:
        if k not in step:
            err(f"{where}: missing required key {k!r}")
    for k, value in step.items():
        if k in ("action",) or _is_comment_key(k):
            continue
        checker = TYPE_OVERRIDES.get((action, k)) or KEY_TYPES.get(k)
        if checker is None or (k not in NO_TEMPLATE and expr.is_template(value)):
            continue
        if not checker(value):
            err(f"{where}: {k!r} must be {TYPE_NAMES.get(checker, 'valid')}, got {expr.short(value)}")
    for groups, what in ((spec.groups, "target"), (spec.groups2, "destination")):
        if not groups:
            continue
        present = [g for g in groups if all(k in step for k in g)]
        partial = [g for g in groups if any(k in step for k in g) and not all(k in step for k in g)]
        names = " | ".join("+".join(g) for g in groups)
        if partial:
            err(f"{where}: incomplete {what}: {'+'.join(partial[0])} must be given together")
        elif len(present) != 1:
            err(f"{where}: give exactly one {what}: {names}")
    if spec.operator:
        _check_operator(step, where, err)
    _check_specifics(action, step, where, st)
    for name in expr.placeholder_names({k: val for k, val in step.items() if k not in ("actions",)}):
        st["used"].setdefault(name, where)


def _check_operator(spec: dict, where: str, err) -> None:
    found = [k for k in expr.OPERATORS if k in spec]
    if len(found) != 1:
        err(f"{where}: needs exactly one operator ({', '.join(expr.OPERATORS)}), got "
            + (", ".join(found) if found else "none"))
        return
    op, operand = found[0], spec[found[0]]
    if expr.is_template(operand):
        return
    if op == "length":
        if isinstance(operand, dict):
            inner = [k for k in expr.OPERATORS if k in operand]
            bad = [k for k in operand if k not in expr.OPERATORS and k not in expr.OPERATOR_EXTRAS]
            if len(inner) != 1 or bad or inner[0] in ("length", "exists"):
                err(f"{where}: 'length' takes an integer or one comparison like {{\"gte\": 2}}")
        elif not _int(operand):
            err(f"{where}: 'length' takes an integer or one comparison like {{\"gte\": 2}}")
    elif op == "matches":
        if not isinstance(operand, str):
            err(f"{where}: 'matches' takes a regex string")
        else:
            try:
                re.compile(operand)
            except re.error as e:
                err(f"{where}: bad regex {operand!r}: {e}")
    elif op == "in" and not isinstance(operand, (list, str)):
        err(f"{where}: 'in' takes a list of values")
    elif op == "approx" and not _num(operand):
        err(f"{where}: 'approx' takes a number (plus optional 'tolerance')")
    elif op in ("gt", "gte", "lt", "lte") and not (_num(operand) or isinstance(operand, str)):
        err(f"{where}: {op!r} takes a number or a string")
    elif op == "exists" and not _bool(operand):
        err(f"{where}: 'exists' takes true/false")
    if "tolerance" in spec and op != "approx":
        err(f"{where}: 'tolerance' only applies to 'approx'")


def _check_specifics(action: str, step: dict, where: str, st: dict) -> None:
    v: Validation = st["v"]
    err = v.errors.append

    def enum(key, choices):
        val = step.get(key)
        if val is not None and not expr.is_template(val) and val not in choices:
            err(f"{where}: {key!r} must be one of {', '.join(choices)}, got {val!r}")

    if action == "wait" and _num(step.get("ms")) and step["ms"] < 0:
        err(f"{where}: 'ms' must be >= 0")
    if action == "timescale" and _num(step.get("value")) and not 0 <= step["value"] <= 100:
        err(f"{where}: 'value' must be between 0 and 100")
    if "timeoutMs" in step and _num(step["timeoutMs"]) and step["timeoutMs"] < 0:
        err(f"{where}: 'timeoutMs' must be >= 0")
    if action == "wait_ui":
        enum("state", UI_STATES)
    if action == "logs":
        enum("level", LOG_LEVELS)
        if isinstance(step.get("match"), str):
            try:
                re.compile(step["match"])
            except re.error as e:
                err(f"{where}: bad regex in 'match': {e}")
    if action == "invariants":
        enum("minSeverity", SEVERITIES)
    if action == "reflect":
        enum("op", REFLECT_OPS)
        op = step.get("op")
        if op in ("members", "get", "set", "call") and "type" not in step:
            err(f"{where}: reflect {op} needs 'type'")
        if op in ("get", "set") and "member" not in step:
            err(f"{where}: reflect {op} needs 'member'")
        if op == "set" and "value" not in step:
            err(f"{where}: reflect set needs 'value'")
        if op == "call" and "method" not in step:
            err(f"{where}: reflect call needs 'method'")
    if action == "assert_ui" and isinstance(step.get("expect"), dict):
        allowed = {"found", "active", "interactable", "caption"}
        for k in step["expect"]:
            if k not in allowed:
                err(f"{where}: expect key {k!r} not supported (use {', '.join(sorted(allowed))})")
        if not step["expect"]:
            err(f"{where}: 'expect' must name at least one of found, active, interactable, caption")
    if action == "op" and step.get("expectError") is not None and step.get("expect") is not None:
        err(f"{where}: 'expect' and 'expectError' exclude each other")
    if isinstance(step.get("allowRedLogs"), list):
        for pattern in step["allowRedLogs"]:
            try:
                re.compile(pattern)
            except re.error as e:
                err(f"{where}: bad regex in allowRedLogs {pattern!r}: {e}")
    if action == "repeat":
        if _int(step.get("times")) and not 0 <= step["times"] <= 10000:
            err(f"{where}: 'times' must be between 0 and 10000")
        if isinstance(step.get("actions"), list):
            if not step["actions"]:
                err(f"{where}: 'actions' must not be empty")
            _walk(step["actions"], f"{where}.actions", st)
        until = step.get("until")
        if isinstance(until, dict):
            for k in until:
                if k != "path" and k not in expr.OPERATORS and k not in expr.OPERATOR_EXTRAS:
                    err(f"{where}.until: unknown key {k!r}")
            if not isinstance(until.get("path"), str):
                err(f"{where}.until: needs 'path'")
            _check_operator(until, f"{where}.until", err)
            for name in expr.placeholder_names(until):
                st["used"].setdefault(name, f"{where}.until")
        if step.get("untilRequired") and "until" not in step:
            err(f"{where}: 'untilRequired' needs 'until'")
    if action == "include":
        _check_include(step, where, st)


def _check_include(step: dict, where: str, st: dict) -> None:
    v: Validation = st["v"]
    ref = step.get("scenario")
    if not isinstance(ref, str) or not ref:
        return
    if expr.is_template(ref):
        v.warnings.append(f"{where}: templated include path {ref!r} cannot be checked offline")
        return
    target = resolve_ref(ref, st["base"], st["dirs"])
    if target is None:
        v.errors.append(f"{where}: included scenario not found: {ref}")
        return
    if str(target) in st["stack"]:
        v.errors.append(f"{where}: include cycle through {target.name}")
        return
    if len(st["stack"]) >= MAX_INCLUDE_DEPTH:
        v.errors.append(f"{where}: includes nested deeper than {MAX_INCLUDE_DEPTH}")
        return
    try:
        sub = load_doc(target)
    except SchemaError as e:
        v.errors.append(f"{where}: {e}")
        return
    if kind_of(sub) == "campaign":
        v.errors.append(f"{where}: {target.name} is a campaign file, not a scenario")
        return
    params = step.get("params") if isinstance(step.get("params"), dict) else {}
    sv = validate_scenario(sub, target, st["dirs"], external=st["declared"] | set(params), _stack=st["stack"])
    v.includes.append(target)
    v.includes.extend(p for p in sv.includes if p not in v.includes)
    for e in sv.errors:
        v.errors.append(f"{where} -> {target.name}: {e}")
    for w in sv.warnings:
        v.warnings.append(f"{where} -> {target.name}: {w}")
    st["declared"] |= sv.saved
    st["saved"] |= sv.saved


# ---- campaign validation -----------------------------------------------------

def campaign_entries(doc: dict) -> list[dict]:
    """Normalised entries: {"ref", "params", "case"}."""
    out = []
    for e in doc.get("scenarios") or []:
        if isinstance(e, str):
            out.append({"ref": e, "params": {}, "case": None})
        elif isinstance(e, dict):
            out.append({"ref": e.get("path") or e.get("scenario"), "params": dict(e.get("params") or {}),
                        "case": e.get("case")})
        else:
            out.append({"ref": None, "params": {}, "case": None})
    return out


def validate_campaign(doc: Any, path: str | Path, search_dirs: Iterable[Path] = (),
                      external: Iterable[str] = ()) -> Validation:
    path = Path(path)
    v = Validation(path=str(path), kind="campaign", cases=0)
    if not isinstance(doc, dict):
        v.errors.append("a campaign must be a JSON object with 'note' and 'scenarios'")
        return v
    for k in doc:
        if k not in CAMPAIGN_KEYS and not _is_comment_key(k):
            v.errors.append(f"unknown campaign key {k!r}{_suggest(k, CAMPAIGN_KEYS)}")
    if not _nonempty_str(doc.get("note")):
        v.errors.append("'note' is required: a non-empty string saying what the campaign covers")
    for k in ("stopOnFail", "freshPlay"):
        if k in doc and not _bool(doc[k]):
            v.errors.append(f"'{k}' must be true/false")
    if "params" in doc and not _dict(doc["params"]):
        v.errors.append("'params' must be an object")
    scen = doc.get("scenarios")
    if not isinstance(scen, list) or not scen:
        v.errors.append("'scenarios' must be a non-empty list of scenario paths")
        return v
    base_params = set(doc.get("params") or {}) if _dict(doc.get("params")) else set()
    for i, entry in enumerate(campaign_entries(doc)):
        ref = entry["ref"]
        where = f"scenarios[{i}]"
        if not isinstance(ref, str) or not ref:
            v.errors.append(f"{where}: must be a path string or an object with 'path'")
            continue
        target = resolve_ref(ref, path.parent, search_dirs)
        if target is None:
            v.errors.append(f"{where}: scenario not found: {ref}")
            continue
        try:
            sub = load_doc(target)
        except SchemaError as e:
            v.errors.append(f"{where}: {e}")
            continue
        if kind_of(sub) == "campaign":
            v.errors.append(f"{where}: {target.name} is a campaign; campaigns do not nest")
            continue
        sv = validate_scenario(sub, target, search_dirs, external=set(external) | base_params | set(entry["params"]))
        v.includes.append(target)
        v.cases += sv.cases
        for e in sv.errors:
            v.errors.append(f"{where} {target.name}: {e}")
        for w in sv.warnings:
            v.warnings.append(f"{where} {target.name}: {w}")
    return v


def validate_path(path: str | Path, search_dirs: Iterable[Path] = (), external: Iterable[str] = ()) -> Validation:
    p = Path(path)
    try:
        doc = load_doc(p)
    except SchemaError as e:
        v = Validation(path=str(p))
        v.errors.append(str(e))
        return v
    if kind_of(doc) == "campaign":
        return validate_campaign(doc, p, search_dirs, external)
    return validate_scenario(doc, p, search_dirs, external)
