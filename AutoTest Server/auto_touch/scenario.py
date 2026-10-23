"""Scenario runner: setup -> actions -> teardown against the Unity bridge.

One `Runner` runs files (every selected matrix case in the same Play session);
one `CaseRun` runs one case and writes its report. The scenario format is
documented in references/scenarios.md; validation lives in schema.py.

Step outcome model
* `ok`      raw result of the check (watchdogs included).
* `status`  verdict: pass | fail | warn (optional) | known_issue (knownIssue) | skipped.
* The run FAILS when any step ends `fail`. By default the first `fail` stops the
  run (remaining steps become `skipped`); teardown always runs.
* After every step that is not read-only: red-log watchdog (Error / Assert /
  Exception since the last step, minus config `red_log_ignore` and the step's
  `allowRedLogs`) and, while playing, the invariants watchdog.
"""
from __future__ import annotations

import re
import time
import traceback
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any, Callable

from . import EXIT_COMPILE, EXIT_FAIL, EXIT_INVALID, EXIT_OK, EXIT_UNREACHABLE, expr, matrix, report, schema, worst_exit
from .bridge import Bridge, BridgeError, ServerRejected, ServerUnreachable, WaitTimeout
from .config import SERVER_DIR, Config

RED_TYPES = ("Error", "Assert", "Exception")
SEVERITY_RANK = {"low": 1, "medium": 2, "high": 3}
VERDICT = {EXIT_OK: "PASS", EXIT_FAIL: "FAIL", EXIT_INVALID: "INVALID",
           EXIT_UNREACHABLE: "ERROR", EXIT_COMPILE: "ERROR"}


class ScenarioInvalid(Exception):
    def __init__(self, problems):
        self.problems = [problems] if isinstance(problems, str) else list(problems)
        super().__init__("; ".join(self.problems))


class StepFail(Exception):
    def __init__(self, message: str, data: dict | None = None):
        super().__init__(message)
        self.message = message
        self.data = data or {}


class Abort(Exception):
    """Stops the whole case: server gone, compile errors, failed precondition."""

    def __init__(self, code: int, message: str, teardown: bool = True):
        super().__init__(message)
        self.code = code
        self.message = message
        self.teardown = teardown


@dataclass
class RunOptions:
    enter_play: bool | None = None      # None -> config auto_enter_play
    exit_play: bool = False
    timescale: float | None = None
    no_stop: bool = False
    verbose: bool = False
    case: str | None = None             # matrix filter (k=v,... or label substring)
    params: dict = field(default_factory=dict)  # external ${...} values (campaign / --param)


@dataclass
class _Cx:
    phase: str
    where: str
    soft: tuple | None
    depth: int
    always: bool
    row: dict


# ---- shared helpers ------------------------------------------------------------

def compile_error_lines(cs: dict) -> list[str]:
    out = []
    for e in (cs.get("lastCompile") or {}).get("errors") or []:
        loc = e.get("file") or "?"
        if e.get("line") is not None:
            loc += f"({e.get('line')},{e.get('column', 0)})"
        asm = f" [{e['assembly']}]" if e.get("assembly") else ""
        out.append(f"{loc}: {e.get('message', '')}{asm}")
    return out


def preflight(bridge: Bridge) -> tuple[int | None, dict, str]:
    """(exit code or None, status, message): unreachable -> 2, compile errors -> 5."""
    try:
        st = bridge.status()
        if st.get("isCompiling") or st.get("isUpdating"):
            cs = bridge.wait_compile(None, start_grace=0)
            st = bridge.status()
        else:
            cs = bridge.compile_status()
    except ServerUnreachable as e:
        return EXIT_UNREACHABLE, {}, f"server unreachable: {e}"
    except WaitTimeout as e:
        return EXIT_COMPILE, {}, f"Unity is still compiling: {e}"
    count = max(int(st.get("lastCompileErrors") or 0),
                int((cs.get("lastCompile") or {}).get("errorCount") or 0),
                1 if st.get("scriptCompilationFailed") else 0)
    if count > 0:
        lines = compile_error_lines(cs)
        return EXIT_COMPILE, st, (f"Unity has {count} compile error(s); fix them before testing:\n"
                                  + "\n".join("  " + line for line in lines[:20]))
    return None, st, ""


def resolve_scenario(cfg: Config, ref: str | Path) -> Path | None:
    p = Path(ref)
    candidates = [p] if p.is_absolute() else [Path.cwd() / p, SERVER_DIR / p, cfg.scenarios_path / p]
    for c in list(candidates):
        if not c.suffix:
            candidates.append(c.with_name(c.name + ".json"))
    for c in candidates:
        if c.is_file():
            return c.resolve()
    return None


def scene_matches(active: Any, wanted: str | None) -> bool:
    if not wanted:
        return True
    if not active:
        return False

    def norm(s: str) -> str:
        s = str(s).replace("\\", "/").strip().lower()
        return s[:-6] if s.endswith(".unity") else s
    a, w = norm(active), norm(wanted)
    return a == w or a.rsplit("/", 1)[-1] == w.rsplit("/", 1)[-1]


def ui_state_ok(r: dict, want: str) -> bool:
    found = bool(r.get("found"))
    active = found and bool(r.get("activeInHierarchy", r.get("active")))
    interactable = active and bool(r.get("interactable"))
    return {"exists": found, "active": active, "interactable": interactable,
            "inactive": not active, "absent": not found}[want]


def _sanitize(name: str) -> str:
    return re.sub(r"[^A-Za-z0-9._=-]+", "_", str(name)).strip("_")[:120] or "shot"


# ---- runner --------------------------------------------------------------------

class Runner:
    def __init__(self, cfg: Config, bridge: Bridge | None = None, opts: RunOptions | None = None,
                 out: Callable[[str], None] | None = None):
        self.cfg = cfg
        self.bridge = bridge or Bridge(cfg)
        self.opts = opts or RunOptions()
        self.out = out or (lambda s: print(s, flush=True))
        self.search_dirs = schema.default_search_dirs(cfg)

    def set_play(self, on: bool, scene: str | None = None, timeout: float | None = None) -> dict:
        """Request the mode change and poll until it is over. The POST's reply may be
        cut by the domain reload; a retried /status then tells a dead editor apart."""
        try:
            self.bridge.play(on, scene)
        except ServerUnreachable:
            self.bridge.status()
        return self.bridge.wait_play(on, timeout)

    def _invalid(self, ref, problems: list[str]) -> dict:
        self.out(f"INVALID {ref}")
        for p in problems:
            self.out(f"  - {p}")
        return {"scenario": str(ref), "exitCode": EXIT_INVALID, "verdict": "INVALID",
                "problems": problems, "cases": []}

    def load(self, ref) -> tuple[Path, dict, schema.Validation, list[matrix.Case]]:
        path = resolve_scenario(self.cfg, ref)
        if path is None:
            raise ScenarioInvalid(f"scenario not found: {ref}")
        try:
            doc = schema.load_doc(path)
        except schema.SchemaError as e:
            raise ScenarioInvalid(str(e)) from None
        if schema.kind_of(doc) == "campaign":
            raise ScenarioInvalid(f"{path.name} is a campaign file; run it with `campaign`")
        v = schema.validate_scenario(doc, path, self.search_dirs, external=set(self.opts.params))
        if v.errors:
            raise ScenarioInvalid(v.errors)
        try:
            cases = matrix.select(matrix.expand(doc), self.opts.case)
        except matrix.MatrixError as e:
            raise ScenarioInvalid(str(e)) from None
        return path, doc, v, cases

    def run_path(self, ref, do_preflight: bool = True) -> dict:
        try:
            path, doc, v, cases = self.load(ref)
        except ScenarioInvalid as e:
            return self._invalid(ref, e.problems)
        if self.opts.verbose:
            for w in v.warnings:
                self.out(f"warning: {w}")
        if do_preflight:
            code, _st, msg = preflight(self.bridge)
            if code is not None:
                self.out(f"{VERDICT.get(code, 'ERROR')} {path.stem}: {msg}")
                return {"scenario": str(path), "exitCode": code, "verdict": VERDICT.get(code, "ERROR"),
                        "problems": [msg], "cases": []}
        results = []
        for case in cases:
            rep = CaseRun(self, doc, path, case, v).run()
            results.append(rep)
            if rep["exitCode"] == EXIT_UNREACHABLE:
                break
        if self.opts.exit_play:
            try:
                if self.bridge.status().get("isPlaying"):
                    self.set_play(False)
            except BridgeError as e:
                self.out(f"warning: could not exit Play mode: {e}")
        code = worst_exit(r["exitCode"] for r in results)
        return {"scenario": str(path), "exitCode": code, "verdict": VERDICT.get(code, "FAIL"),
                "problems": [], "cases": results}


class CaseRun:
    def __init__(self, runner: Runner, doc: dict, path: Path, case: matrix.Case, validation: schema.Validation):
        self.runner = runner
        self.cfg = runner.cfg
        self.b = runner.bridge
        self.opts = runner.opts
        self.out = runner.out
        self.doc = doc
        self.path = path
        self.case = case
        self.stem = path.stem
        self.name = case.name(self.stem)
        self.declared = set(validation.declared) | set(self.opts.params)
        self.params = {**(doc.get("params") or {}), **self.opts.params, **case.params}
        self.vars: dict = {}
        self.defaults_stack: list[dict] = []
        self.scope_stack: list[dict] = []
        self.last: Any = None
        self.rows: list[dict] = []
        self.anomalies: list[dict] = []
        self.screenshots: list[dict] = []
        self.snapshots: list[dict] = []
        self.seq = 0
        self.halt = False
        self.failed = False
        self.abort: Abort | None = None
        self._abort_after: Abort | None = None
        self.log_cursor = 0
        self.run_log_start = 0
        self.handled: set = set()
        self._dropped_noted = False
        self.inv_supported = True
        self.firing_rules: set = set()
        self.playing = False
        self.ts_original: float | None = None
        self.ts_applied: float | None = None
        self.ts_changed = False
        self.include_stack = [path.resolve()]
        self.server: dict = {}
        self.ignore_rx = [re.compile(p) for p in self.cfg.red_log_ignore]
        self.poll = max(0.01, self.cfg.poll_ms / 1000.0)

    # ---- orchestration --------------------------------------------------------

    def run(self) -> dict:
        t0 = time.monotonic()
        started = datetime.now()
        try:
            self._prepare()
            self._run_list(self.doc.get("setup") or [], "setup", "setup")
            actions = self.doc.get("actions") or []
            if self.halt:
                self._skip(actions, "actions", "actions", 0, 0, "skipped after a failure")
            else:
                self._run_list(actions, "actions", "actions")
        except Abort as a:
            self.abort = a
        except ServerUnreachable as e:
            self.abort = Abort(EXIT_UNREACHABLE, f"server unreachable: {e}", teardown=False)
        except BridgeError as e:
            self.abort = Abort(EXIT_FAIL, f"preparation failed: {e}", teardown=False)
        except ScenarioInvalid as e:
            self.abort = Abort(EXIT_INVALID, "invalid scenario: " + "; ".join(e.problems))
        except Exception as e:  # a runner bug must still leave a report behind
            self.abort = Abort(EXIT_FAIL, f"internal error: {e!r}")
            self.anomalies.append({"type": "internal_error", "trace": traceback.format_exc()[-4000:]})
        teardown = self.doc.get("teardown") or []
        if self.abort is not None and not self.abort.teardown:
            self._skip(teardown, "teardown", "teardown", 0, 0, f"skipped: {self.abort.message[:80]}")
        else:
            try:
                self._run_list(teardown, "teardown", "teardown", always=True)
            except Abort as a:
                self.abort = self.abort or a
            except ScenarioInvalid as e:
                self.abort = self.abort or Abort(EXIT_INVALID, "invalid scenario: " + "; ".join(e.problems))
        return self._finish(started, t0)

    def _prepare(self) -> None:
        try:
            st = self.b.status()
        except ServerUnreachable as e:
            raise Abort(EXIT_UNREACHABLE, f"server unreachable: {e}", teardown=False) from None
        self.server = {k: st.get(k) for k in ("serverVersion", "project", "unityVersion", "activeScene")}
        self.server["adapter"] = st.get("adapter")
        self.playing = bool(st.get("isPlaying"))
        try:
            self.log_cursor = self.run_log_start = self._log_tail()
        except ServerUnreachable as e:
            raise Abort(EXIT_UNREACHABLE, f"server unreachable: {e}", teardown=False) from None
        try:
            scene = expr.render(self.doc.get("scene"), self._ctx())
        except expr.Unresolved as u:
            raise ScenarioInvalid(f"scene: {u}") from None
        if self.doc.get("requiresPlay", True):
            need = not self.playing or (scene and not scene_matches(st.get("activeScene"), scene))
            if need:
                allowed = self.opts.enter_play if self.opts.enter_play is not None else self.cfg.auto_enter_play
                if not allowed:
                    row = self._new_row("play", "preflight", "preflight", 0, "enter Play (preflight)")
                    msg = ("scenario requires Play mode and the editor is not playing "
                           "(pass --enter-play or set auto_enter_play)")
                    self._finalize(row, False, msg, {}, {}, None, time.monotonic())
                    raise Abort(EXIT_FAIL, msg, teardown=False)
                step = {"action": "play", "on": True, "label": "enter Play (preflight)"}
                if scene:
                    step["scene"] = scene
                row = self._execute(step, "preflight", "preflight", None, 0, False)
                if not row["ok"] and not self.playing:
                    raise Abort(EXIT_FAIL, f"could not enter Play mode: {row['message']}", teardown=False)
        try:
            self.ts_original = self.b.timescale()
        except ServerRejected:
            self.ts_original = None
        ts = self.opts.timescale
        if ts is None and self.doc.get("timescale") is not None:
            try:
                ts = float(expr.render(self.doc["timescale"], self._ctx()))
            except (expr.Unresolved, TypeError, ValueError) as e:
                raise ScenarioInvalid(f"timescale: {e}") from None
        if ts is None:
            ts = self.cfg.default_timescale
        if ts is not None:
            self.b.timescale(float(ts))
            self.ts_applied = float(ts)
            self.ts_changed = True

    def _finish(self, started: datetime, t0: float) -> dict:
        unreachable = self.abort is not None and self.abort.code == EXIT_UNREACHABLE
        if not unreachable:
            if self.ts_changed and self.ts_original is not None:
                try:
                    self.b.timescale(self.ts_original)
                except BridgeError as e:
                    self.anomalies.append({"type": "timescale_not_restored", "message": str(e)})
        final_state = None
        if not unreachable and self.playing:
            try:
                final_state = report.clip(self.b.state(retries=0).get("state"), 200_000)
            except BridgeError:
                final_state = None
        if self.abort is not None:
            code = self.abort.code
        else:
            code = EXIT_FAIL if self.failed else EXIT_OK
        counts = {s: 0 for s in ("pass", "fail", "warn", "known_issue", "skipped")}
        for r in self.rows:
            counts[r["status"]] = counts.get(r["status"], 0) + 1
        counts["total"] = len(self.rows)
        fail_row = next((r for r in self.rows if r["status"] == "fail"), None)
        failure = None
        if fail_row is not None:
            failure = {k: fail_row[k] for k in ("index", "phase", "where", "action", "label", "message")}
        elif self.abort is not None:
            failure = {"index": None, "phase": "run", "where": "", "action": "", "label": "",
                       "message": self.abort.message}
        rep = {
            "scenario": str(self.path), "stem": self.stem, "name": self.name, "case": self.case.label,
            "params": self.case.params, "note": self.doc.get("note", ""), "tags": self.doc.get("tags") or [],
            "verdict": VERDICT.get(code, "FAIL"), "exitCode": code,
            "abort": self.abort.message if self.abort else None,
            "startedAt": started.isoformat(timespec="seconds"),
            "durationMs": int((time.monotonic() - t0) * 1000),
            "counts": counts, "failure": failure, "steps": self.rows, "anomalies": self.anomalies,
            "screenshots": self.screenshots, "snapshots": self.snapshots, "finalState": final_state,
            "vars": report.clip(self.vars, 20_000), "server": self.server,
            "config": {k: getattr(self.cfg, k) for k in ("fail_on_red_logs", "invariants_every_step",
                                                         "fail_on_invariants", "red_log_ignore")},
        }
        try:
            json_path, md_path = report.write_case(self.cfg, rep, self.case.file_suffix)
            rep["reportPath"], rep["markdownPath"] = str(json_path), str(md_path)
        except OSError as e:
            rep["reportPath"] = rep["markdownPath"] = None
            self.out(f"warning: could not write the report: {e}")
        for line in report.console_summary(rep, verbose=self.opts.verbose):
            self.out(line)
        return rep

    # ---- step plumbing ------------------------------------------------------------

    def _ctx(self) -> dict:
        ctx: dict = {}
        for d in self.defaults_stack:
            ctx.update(d)
        ctx.update(self.params)
        ctx.update(self.vars)
        for d in self.scope_stack:
            ctx.update(d)
        return ctx

    def _new_row(self, action, phase, where, depth, label="") -> dict:
        self.seq += 1
        row = {"index": self.seq, "phase": phase, "where": where, "depth": depth, "action": action,
               "label": label, "ok": False, "status": "fail", "message": "", "durationMs": 0, "data": {}}
        self.rows.append(row)
        return row

    def _skip(self, steps: list, phase: str, where: str, start: int, depth: int, reason: str) -> None:
        for i, step in enumerate(steps[start:], start):
            action = step.get("action") if isinstance(step, dict) else "?"
            label = step.get("label", "") if isinstance(step, dict) else ""
            row = self._new_row(action, phase, f"{where}[{i}]", depth, label)
            row.update(status="skipped", message=reason)

    def _run_list(self, steps: list, phase: str, where: str, soft: tuple | None = None,
                  depth: int = 0, always: bool = False) -> bool:
        """Run steps; True when any of them raw-failed."""
        failed = False
        for i, step in enumerate(steps):
            if self.halt and not always:
                self._skip(steps, phase, where, i, depth, "skipped after a failure")
                break
            try:
                row = self._execute(step, phase, f"{where}[{i}]", soft, depth, always)
            except (Abort, ScenarioInvalid) as e:
                reason = "skipped: run aborted" if isinstance(e, Abort) else "skipped: invalid scenario"
                self._skip(steps, phase, where, i + 1, depth, reason)
                raise
            if not row["ok"]:
                failed = True
                if depth > 0 and not self.opts.no_stop:
                    self._skip(steps, phase, where, i + 1, depth, "skipped after a failure in this block")
                    break
        return failed

    def _execute(self, step: dict, phase: str, where: str, soft: tuple | None, depth: int,
                 always: bool) -> dict:
        row = self._new_row(step.get("action"), phase, where, depth, step.get("label", ""))
        t0 = time.monotonic()
        ok, message, data = False, "", {}
        rendered = None
        raise_after: BaseException | None = None
        try:
            rendered = self._render_step(step)
            fn = getattr(self, "_a_" + str(step.get("action")))
            message, data = fn(rendered, step, _Cx(phase, where, soft, depth, always, row))
            ok = True
        except StepFail as f:
            message, data = f.message, f.data
        except expr.Unresolved as u:
            if u.name in self.declared or u.name in self._ctx():
                message = f"{u} (the step that sets it did not run or failed)"
            else:
                message = str(u)
                raise_after = ScenarioInvalid(f"{where}: {u}")
        except expr.ExprError as e:
            message = str(e)
            raise_after = ScenarioInvalid(f"{where}: {e}")
        except ScenarioInvalid as e:
            message = "; ".join(e.problems)
            raise_after = e
        except WaitTimeout as e:
            message = str(e)
        except ServerRejected as e:
            message = f"rejected by the bridge: HTTP {e.status} {e.kind}: {e.message}"
            data = {"httpStatus": e.status, "kind": e.kind}
            if e.payload.get("stack"):
                data["stack"] = str(e.payload["stack"])[:2000]
        except ServerUnreachable as e:
            message = f"server unreachable: {e}"
            raise_after = Abort(EXIT_UNREACHABLE, message, teardown=False)
        except Abort as a:
            message = a.message
            raise_after = a
        row["data"].update(data or {})
        if raise_after is None and rendered is not None and not schema.is_readonly(rendered):
            try:
                msgs = self._watchdogs(rendered, row)
            except ServerUnreachable as e:
                msgs = [f"server unreachable: {e}"]
                raise_after = Abort(EXIT_UNREACHABLE, msgs[0], teardown=False)
            if msgs:
                message = "; ".join(msgs) if ok else f"{message}; {'; '.join(msgs)}"
                ok = False
        self._finalize(row, ok, message, None, step, soft, t0)
        if raise_after is None and self._abort_after is not None:
            raise_after, self._abort_after = self._abort_after, None
        if raise_after is not None:
            raise raise_after
        return row

    def _render_step(self, step: dict) -> dict:
        skip = ("actions", "until") if step.get("action") == "repeat" else ()
        ctx = self._ctx()
        return {k: (v if k in skip else expr.render(v, ctx)) for k, v in step.items()}

    def _finalize(self, row: dict, ok: bool, message: str, data: dict | None, step: dict,
                  soft: tuple | None, t0: float) -> None:
        if data:
            row["data"].update(data)
        row["ok"] = bool(ok)
        row["message"] = message
        row["durationMs"] = int((time.monotonic() - t0) * 1000)
        known = step.get("knownIssue")
        if ok:
            row["status"] = "pass"
            if known:
                row["knownIssue"] = known
                row["knownIssueResolved"] = True
                self.anomalies.append({"type": "known_issue_resolved", "step": row["index"], "knownIssue": known,
                                       "message": "the step passed: the known issue looks fixed, drop knownIssue"})
        else:
            if known:
                kind, text = "known_issue", known
            elif step.get("optional"):
                kind, text = "warn", None
            elif soft:
                kind, text = soft
            else:
                kind, text = "fail", None
            row["status"] = kind
            if kind == "known_issue":
                row["knownIssue"] = text
                self.anomalies.append({"type": "known_issue", "step": row["index"], "knownIssue": text,
                                       "message": message})
            elif kind == "fail":
                self.failed = True
                if not self.opts.no_stop:
                    self.halt = True
        if self.opts.verbose:
            self.out(report.step_line(row))

    @staticmethod
    def _soft_of(raw: dict, inherited: tuple | None) -> tuple | None:
        if raw.get("knownIssue"):
            return ("known_issue", raw["knownIssue"])
        if raw.get("optional"):
            return ("warn", None)
        return inherited

    def _timeout(self, s: dict, default_s: float) -> float:
        t = s.get("timeoutMs")
        return default_s if t is None else max(0.0, float(t) / 1000.0)

    def _http_timeout(self, s: dict) -> float | None:
        t = s.get("timeoutMs")
        return None if t is None else max(self.cfg.http_timeout, float(t) / 1000.0)

    # ---- logs & watchdogs ---------------------------------------------------------

    def _log_tail(self) -> int:
        cursor = 0
        for _ in range(100):
            r = self.b.logs(since=cursor, level="all", limit=2000)
            nxt = int(r.get("nextIndex") or 0)
            if not r.get("entries") or nxt <= cursor:
                return max(cursor, nxt)
            cursor = nxt
        return cursor

    def _read_logs(self, since: int, level: str = "all", retries: int | None = None) -> tuple[list, int]:
        entries: list = []
        cursor = since
        reset_done = False
        for _ in range(100):
            r = self.b.logs(since=cursor, level=level, limit=2000, retries=retries)
            nxt = int(r.get("nextIndex", cursor) or 0)
            if nxt < cursor and not reset_done:
                # The ring restarted (domain reload on Play / recompile): all of it is new.
                reset_done = True
                self.handled.clear()
                self.run_log_start = 0
                self.anomalies.append({"type": "log_ring_reset", "message": f"log index went {cursor} -> {nxt}"})
                cursor = since = 0
                continue
            if r.get("dropped") and not self._dropped_noted:
                self._dropped_noted = True
                self.anomalies.append({"type": "logs_dropped", "count": r.get("dropped"),
                                       "message": "the log ring overflowed; some entries were not seen"})
            batch = [e for e in r.get("entries") or [] if int(e.get("index", 0)) >= since]
            entries.extend(batch)
            if not batch or nxt <= cursor:
                cursor = max(cursor, nxt)
                break
            cursor = nxt
        return entries, cursor

    def _classify_red(self, entries: list, step: dict) -> tuple[list, int]:
        allow = step.get("allowRedLogs")
        allow_rx = [re.compile(p) for p in allow] if isinstance(allow, list) else []
        reds, allowed = [], 0
        for e in entries:
            if e.get("type") not in RED_TYPES:
                continue
            idx = e.get("index")
            if idx in self.handled:
                continue
            self.handled.add(idx)
            msg = str(e.get("message") or "")
            if any(rx.search(msg) for rx in self.ignore_rx):
                continue
            if allow is True or any(rx.search(msg) for rx in allow_rx):
                allowed += 1
                continue
            reds.append(e)
        return reds, allowed

    def _watchdogs(self, step: dict, row: dict) -> list[str]:
        msgs: list[str] = []
        entries, self.log_cursor = self._read_logs(self.log_cursor, "error")
        reds, allowed = self._classify_red(entries, step)
        if allowed:
            row["data"]["allowedRedLogs"] = allowed
        if reds:
            compact = [report.compact_log(e) for e in reds[:10]]
            row["data"]["redLogs"] = compact
            self.anomalies.append({"type": "red_logs", "step": row["index"], "count": len(reds), "entries": compact})
            if self.cfg.fail_on_red_logs:
                first = reds[0]
                msgs.append(f"{len(reds)} red log(s): {first.get('type')}: "
                            f"{str(first.get('message') or '').splitlines()[0][:200] if first.get('message') else ''}")
        findings = self._invariant_watch()
        if findings:
            row["data"]["invariants"] = findings[:20]
            self.anomalies.append({"type": "invariants", "step": row["index"], "findings": findings[:20]})
            threshold = SEVERITY_RANK.get(self.cfg.fail_on_invariants, 99)
            failing = [f for f in findings if _rank(f) >= threshold]
            if failing:
                f = failing[0]
                more = f" (+{len(failing) - 1} more)" if len(failing) > 1 else ""
                msgs.append(f"invariant {f.get('rule')} [{f.get('severity')}]: {f.get('detail')}{more}")
        return msgs

    def _invariant_watch(self) -> list[dict]:
        if not (self.cfg.invariants_every_step and self.playing and self.inv_supported):
            return []
        try:
            r = self.b.invariants(retries=0)
        except ServerRejected as e:
            if e.status == 501:
                self.inv_supported = False
                self.anomalies.append({"type": "invariants_unsupported",
                                       "message": "the game adapter has no invariants (501); watchdog off"})
            return []
        findings = r.get("findings") or []
        # A rule that keeps firing step after step is one finding, reported once;
        # it counts as new again only after a check where it did not fire.
        new = [f for f in findings if f.get("rule") not in self.firing_rules]
        self.firing_rules = {f.get("rule") for f in findings}
        return new

    # ---- path evaluation -------------------------------------------------------------

    def _state_value(self, retries: int | None = None) -> Any:
        r = self.b.state(retries=retries)
        if r.get("available") is False and r.get("state") is None:
            raise StepFail("game state not available (adapter reports available=false)")
        return r.get("state")

    def _eval(self, path: str, retries: int | None = None) -> tuple[bool, Any]:
        kind, name, parts = expr.path_root(path)
        if kind == "last":
            root = self.last
        elif kind == "var":
            ctx = self._ctx()
            if name not in ctx:
                return False, expr.MISSING
            root = ctx[name]
        else:
            root = self._state_value(retries)
        value = expr.resolve(root, parts) if parts else root
        return value is not expr.MISSING, value

    def _until_holds(self, until: dict) -> bool:
        spec = expr.render(until, self._ctx())
        try:
            found, value = self._eval(spec["path"])
        except StepFail:
            return False
        return expr.check(spec, value, found)[0]

    def _find_target(self, tid: str, deadline: float) -> dict:
        while True:
            targets = self.b.targets(retries=0)
            for t in targets:
                if str(t.get("id")) == str(tid):
                    return t
            if time.monotonic() >= deadline:
                ids = [str(t.get("id")) for t in targets]
                raise StepFail(f"target {tid!r} not found; available: {', '.join(ids[:20]) or 'none'}",
                               {"targets": ids[:50]})
            time.sleep(self.poll)

    @staticmethod
    def _ui_sel(s: dict) -> tuple[dict, str]:
        for k in ("path", "text", "match"):
            if k in s:
                return {k: s[k]}, f"{k}={s[k]!r}"
        raise StepFail("no UI selector")

    # ---- actions ------------------------------------------------------------------------

    def _a_play(self, s, raw, cx):
        on = bool(s.get("on", True))
        scene = s.get("scene")
        timeout = self._timeout(s, self.cfg.play_timeout_s)
        st = self.b.status()
        playing = bool(st.get("isPlaying"))
        if on and playing and scene and not scene_matches(st.get("activeScene"), scene):
            self.runner.set_play(False, timeout=timeout)
            playing = False
        if playing == on:
            self.playing = on
            return ("already playing" if on else "already in edit mode"), {"activeScene": st.get("activeScene")}
        st = self.runner.set_play(on, scene, timeout)
        self.playing = on
        if on and self.ts_applied is not None:
            self.b.timescale(self.ts_applied)
        return (f"Play mode {'on' if on else 'off'}" + (f" ({st.get('activeScene')})" if on else ""),
                {"activeScene": st.get("activeScene")})

    def _a_wait(self, s, raw, cx):
        ms = float(s["ms"])
        time.sleep(min(ms, 600_000) / 1000.0)
        return f"waited {ms:g} ms", {}

    def _a_timescale(self, s, raw, cx):
        value = float(s["value"])
        got = self.b.timescale(value)
        self.ts_applied = value
        self.ts_changed = True
        return f"timeScale = {got:g}", {"timeScale": got}

    def _a_refresh(self, s, raw, cx):
        before = (self.b.compile_status().get("lastCompile") or {}).get("finishedAtUtc")
        self.b.refresh(exit_play=bool(s.get("exitPlay")))
        cs = self.b.wait_compile(before, timeout=self._timeout(s, self.cfg.compile_timeout_s))
        self.playing = bool(self.b.status().get("isPlaying"))
        errors = compile_error_lines(cs)
        count = int((cs.get("lastCompile") or {}).get("errorCount") or len(errors))
        if count:
            msg = f"{count} compile error(s): " + (errors[0] if errors else "")
            self._abort_after = Abort(EXIT_COMPILE, msg)
            raise StepFail(msg, {"compileErrors": errors[:50]})
        return "refreshed, no compile errors", {"lastCompile": report.clip(cs.get("lastCompile"), 4000)}

    def _a_op(self, s, raw, cx):
        op = s["op"]
        expect_error = s.get("expectError")
        try:
            r = self.b.game_op(op, s.get("params") or {}, timeout=self._http_timeout(s))
        except ServerRejected as e:
            info = {"httpStatus": e.status, "kind": e.kind, "error": e.message}
            if e.payload.get("stack"):
                info["stack"] = str(e.payload["stack"])[:2000]
            if expect_error not in (None, False):
                if expect_error is True or expect_error == e.kind or str(expect_error) in e.message:
                    self.last = info
                    return f"op {op} failed as expected: {e.kind}: {e.message}", info
                raise StepFail(f"op {op} failed with {e.kind} ({e.message}), expected {expect_error!r}", info)
            raise StepFail(f"op {op} failed: HTTP {e.status} {e.kind}: {e.message}", info)
        result = r.get("result")
        data = {"result": report.clip(result, 4000)}
        if expect_error not in (None, False):
            raise StepFail(f"op {op} succeeded but was expected to fail", data)
        self.last = result
        if s.get("saveAs"):
            self.vars[s["saveAs"]] = result
        if "expect" in s:
            mism = expr.subset_match(s["expect"], result)
            if mism:
                raise StepFail(f"op {op}: " + "; ".join(mism[:5]), data)
        return f"op {op} -> {expr.short(result)}", data

    def _a_state(self, s, raw, cx):
        path = s.get("path")
        if path:
            found, value = self._eval(path)
            if not found:
                raise StepFail(f"{path}: not found")
        else:
            value = self._state_value()
        self.snapshots.append({"step": cx.row["index"], "path": path or "", "value": report.clip(value, 50_000)})
        if s.get("saveAs"):
            self.vars[s["saveAs"]] = value
        return f"{path or 'state'} = {expr.short(value)}", {"value": report.clip(value, 4000)}

    def _a_assert(self, s, raw, cx):
        found, value = self._eval(s["path"])
        ok, detail = expr.check(s, value, found)
        msg = f"{s['path']} {detail}"
        if not ok:
            raise StepFail(msg, {"value": report.clip(value, 4000)})
        return msg, {}

    def _a_wait_state(self, s, raw, cx):
        path = s["path"]
        timeout = self._timeout(s, 10.0)
        poll = max(0.01, float(s.get("pollMs", self.cfg.poll_ms)) / 1000.0)
        deadline = time.monotonic() + timeout
        polls, last, value = 0, "", expr.MISSING
        while True:
            polls += 1
            try:
                found, value = self._eval(path, retries=0)
                ok, detail = expr.check(s, value, found)
                if ok:
                    return f"{path} {detail} (after {polls} poll(s))", {"polls": polls}
                last = detail
            except StepFail as f:
                last = f.message
            except ServerUnreachable:
                last = "server unreachable"
            except ServerRejected as e:
                if e.status not in (409, 503):
                    raise
                last = f"{e.kind}: {e.message}"
            if time.monotonic() >= deadline:
                raise StepFail(f"timed out after {timeout * 1000:.0f} ms: {path} {last}",
                               {"polls": polls, "value": report.clip(None if value is expr.MISSING else value, 4000)})
            time.sleep(poll)

    def _a_click(self, s, raw, cx):
        expect = bool(s.get("expectExecuted", True))
        deadline = time.monotonic() + self._timeout(s, 2.0 if expect else 0.0)
        attempts = 0
        while True:
            attempts += 1
            if "target" in s:
                t = self._find_target(s["target"], deadline)
                c = t.get("center") or [0, 0]
                desc = f"target {s['target']}"
                r = self.b.ui_click(x=c[0], y=c[1])
                r = dict(r, target=t.get("id"))
            elif "path" in s:
                desc = f"path {s['path']!r}"
                r = self.b.ui_click(path=s["path"])
            elif "text" in s:
                desc = f"text {s['text']!r}"
                r = self.b.ui_click(text=s["text"])
            else:
                desc = f"({s['x']}, {s['y']})"
                r = self.b.ui_click(x=s["x"], y=s["y"])
            executed = bool(r.get("executed"))
            if executed == expect or not expect or time.monotonic() >= deadline:
                break
            time.sleep(self.poll)
        self.last = r
        data = {"result": report.clip(r, 4000), "attempts": attempts}
        if executed != expect:
            raise StepFail(f"click {desc}: executed={str(executed).lower()}, expected "
                           f"{str(expect).lower()}: {r.get('message') or ''}".rstrip(": "), data)
        return f"click {desc} -> {r.get('targetPath') or r.get('mode') or 'none'}", data

    def _a_drag(self, s, raw, cx):
        deadline = time.monotonic() + self._timeout(s, 2.0)
        kw: dict = {"steps": s.get("steps"), "frames": s.get("frames")}
        if "from" in s:
            kw["from_path"], src = s["from"], s["from"]
        elif "fromTarget" in s:
            c = self._find_target(s["fromTarget"], deadline).get("center") or [0, 0]
            kw["x0"], kw["y0"], src = c[0], c[1], s["fromTarget"]
        else:
            kw["x0"], kw["y0"], src = s["x0"], s["y0"], f"({s['x0']}, {s['y0']})"
        if "to" in s:
            kw["to_path"], dst = s["to"], s["to"]
        elif "toTarget" in s:
            c = self._find_target(s["toTarget"], deadline).get("center") or [0, 0]
            kw["x1"], kw["y1"], dst = c[0], c[1], s["toTarget"]
        else:
            kw["x1"], kw["y1"], dst = s["x1"], s["y1"], f"({s['x1']}, {s['y1']})"
        r = self.b.drag(**kw)
        if r.get("started") is False:
            raise StepFail(f"drag {src} -> {dst} did not start: {r.get('message') or ''}", {"reply": r})
        st = self.b.wait_drag(timeout=self._timeout(s, 15.0))
        result = st.get("result") or {}
        self.last = result
        data = {"result": report.clip(result, 4000)}
        expect = bool(s.get("expectExecuted", True))
        if bool(result.get("executed")) != expect:
            raise StepFail(f"drag {src} -> {dst}: executed={str(bool(result.get('executed'))).lower()}, "
                           f"expected {str(expect).lower()}", data)
        if "expect" in s:
            mism = expr.subset_match(s["expect"], result)
            if mism:
                raise StepFail(f"drag {src} -> {dst}: " + "; ".join(mism[:5]), data)
        return f"drag {src} -> {dst}: drop on {result.get('dropTarget') or 'nothing'}", data

    def _a_wait_ui(self, s, raw, cx):
        sel, desc = self._ui_sel(s)
        want = s.get("state", "active")
        timeout = self._timeout(s, 5.0)
        poll = max(0.01, float(s.get("pollMs", self.cfg.poll_ms)) / 1000.0)
        deadline = time.monotonic() + timeout
        r: dict = {}
        while True:
            try:
                r = self.b.ui_find(retries=0, **sel)
                if ui_state_ok(r, want):
                    return f"{desc} is {want}", {"path": r.get("path")}
            except ServerUnreachable:
                pass
            except ServerRejected as e:
                if e.status not in (409, 503):
                    raise
            if time.monotonic() >= deadline:
                raise StepFail(f"{desc} not {want} after {timeout * 1000:.0f} ms (found={bool(r.get('found'))}, "
                               f"active={r.get('activeInHierarchy')}, interactable={r.get('interactable')})",
                               {"last": report.clip(r, 2000)})
            time.sleep(poll)

    def _a_assert_ui(self, s, raw, cx):
        sel, desc = self._ui_sel(s)
        exp = s["expect"]
        r = self.b.ui_find(**sel)
        found = bool(r.get("found"))
        problems = []
        if "found" in exp and found != bool(exp["found"]):
            problems.append(f"found={str(found).lower()}, expected {str(bool(exp['found'])).lower()}")
        elif not found and exp.get("found", True):
            raise StepFail(f"{desc}: not found", {"last": r})
        if found:
            if "active" in exp:
                active = bool(r.get("activeInHierarchy", r.get("active")))
                if active != bool(exp["active"]):
                    problems.append(f"active={str(active).lower()}, expected {str(bool(exp['active'])).lower()}")
            if "interactable" in exp:
                inter = bool(r.get("interactable"))
                if inter != bool(exp["interactable"]):
                    problems.append(f"interactable={str(inter).lower()}, expected {str(bool(exp['interactable'])).lower()}")
            if "caption" in exp:
                cap = str(r.get("caption") or "")
                if str(exp["caption"]) not in cap:
                    problems.append(f"caption {cap!r} does not contain {str(exp['caption'])!r}")
        if problems:
            raise StepFail(f"{desc}: " + "; ".join(problems), {"last": report.clip(r, 2000)})
        return f"{desc}: " + ", ".join(f"{k}={expr.short(v)}" for k, v in exp.items()), {}

    def _a_screenshot(self, s, raw, cx):
        name = _sanitize(s.get("name") or f"{self.stem}{self.case.file_suffix}_{cx.row['index']:03d}")
        r = self.b.screenshot(name, s.get("superSize"))
        path = r.get("path")
        if not path:
            raise StepFail("the bridge returned no screenshot path", {"reply": r})
        try:
            self.b.wait_screenshot(path, timeout=self._timeout(s, 15.0))
        except WaitTimeout as e:
            raise StepFail(f"screenshot file never appeared: {path}", {"path": path, "error": str(e)}) from None
        self.screenshots.append({"step": cx.row["index"], "name": name, "path": path})
        return f"screenshot {path}", {"path": path}

    def _a_logs(self, s, raw, cx):
        level = s.get("level", "all")
        entries, _ = self._read_logs(self.run_log_start, level)
        reds, allowed = self._classify_red(entries, s)
        data: dict = {"count": len(entries), "redCount": len(reds),
                      "entries": [report.compact_log(e) for e in entries[-20:]]}
        if reds:
            data["red"] = [report.compact_log(e) for e in reds[:10]]
            self.anomalies.append({"type": "red_logs", "step": cx.row["index"], "count": len(reds), "entries": data["red"]})
        if s.get("match"):
            rx = re.compile(s["match"])
            if not any(rx.search(str(e.get("message") or "")) for e in entries):
                raise StepFail(f"no log entry since the run started matches /{s['match']}/", data)
        if s.get("failOnRed", True) and reds:
            first = str(reds[0].get("message") or "").splitlines()
            raise StepFail(f"{len(reds)} red log(s) since the run started: {reds[0].get('type')}: "
                           f"{first[0][:200] if first else ''}", data)
        return f"{len(entries)} log entries, {len(reds)} new red", data

    def _a_invariants(self, s, raw, cx):
        r = self.b.invariants(s.get("rules"))
        min_sev = s.get("minSeverity", "high")
        findings = r.get("findings") or []
        if not s.get("rules"):
            self.firing_rules = {f.get("rule") for f in findings}
        bad = [f for f in findings if _rank(f) >= SEVERITY_RANK[min_sev]]
        data = {"findings": findings[:50], "rules": r.get("rules")}
        if bad:
            f = bad[0]
            raise StepFail(f"{len(bad)} invariant finding(s) at or above {min_sev}: "
                           f"{f.get('rule')} [{f.get('severity')}]: {f.get('detail')}", data)
        return f"{len(findings)} finding(s), none at or above {min_sev}", data

    def _a_reflect(self, s, raw, cx):
        op = s["op"]
        r = self.b.reflect(op, type=s.get("type"), member=s.get("member"), method=s.get("method"),
                           args=s.get("args"), value=s.get("value"), static=s.get("static"),
                           path=s.get("path"), timeout=self._http_timeout(s), has_value="value" in s)
        threw = bool(r.get("threw"))
        info = {"exception": r.get("exception"), "stack": str(r.get("stack") or "")[:2000]} if threw else {}
        want = s.get("expectThrows")
        what = s.get("method") or s.get("member") or s.get("type") or op
        if want not in (None, False):
            if not threw:
                raise StepFail(f"{what} was expected to throw, it returned {expr.short(r.get('returned'))}")
            if isinstance(want, str) and want not in str(r.get("exception") or ""):
                raise StepFail(f"{what} threw {r.get('exception')!r}, expected {want!r}", info)
            self.last = info
            return f"{what} threw as expected: {r.get('exception')}", info
        if threw:
            raise StepFail(f"{what} threw {r.get('exception')}", info)
        value = {"get": r.get("value"), "call": r.get("returned"), "types": r.get("types"),
                 "members": r.get("members")}.get(op, r)
        self.last = value
        if s.get("saveAs"):
            self.vars[s["saveAs"]] = value
        data = {"value": report.clip(value, 4000)}
        if "expect" in s:
            mism = expr.subset_match(s["expect"], value, "value")
            if mism:
                raise StepFail(f"reflect {op} {what}: " + "; ".join(mism[:5]), data)
        return f"reflect {op} {what} -> {expr.short(value)}", data

    def _a_menu(self, s, raw, cx):
        r = self.b.menu(s["item"])
        if not r.get("executed"):
            raise StepFail(f"menu item {s['item']!r} did not execute", {"reply": r})
        return f"menu {s['item']}", {}

    def _a_repeat(self, s, raw, cx):
        times = int(s["times"])
        until = raw.get("until")
        as_name = s.get("as")
        child_soft = self._soft_of(raw, cx.soft)
        held, done, any_failed = False, 0, False
        for it in range(times + 1):
            if until is not None and self._until_holds(until):
                held = True
                break
            if it == times or (self.halt and not cx.always):
                break
            if as_name:
                self.vars[as_name] = it
            failed = self._run_list(raw["actions"], cx.phase, f"{cx.where}#{it}", child_soft, cx.depth + 1, cx.always)
            done += 1
            if failed:
                any_failed = True
                if not self.opts.no_stop:
                    raise StepFail(f"iteration {it}: a step failed", {"iterations": done})
        data = {"iterations": done, "untilHeld": held}
        if any_failed:
            raise StepFail("a step failed in at least one iteration", data)
        if until is not None and not held and s.get("untilRequired"):
            raise StepFail(f"'until' never held in {times} iteration(s)", data)
        return f"{done} iteration(s)" + (", until held" if held else ""), data

    def _a_include(self, s, raw, cx):
        ref = s["scenario"]
        target = schema.resolve_ref(ref, self.include_stack[-1].parent, self.runner.search_dirs)
        if target is None:
            raise ScenarioInvalid(f"{cx.where}: included scenario not found: {ref}")
        if target in self.include_stack or len(self.include_stack) > schema.MAX_INCLUDE_DEPTH:
            raise ScenarioInvalid(f"{cx.where}: include cycle or nesting too deep at {target.name}")
        try:
            doc = schema.load_doc(target)
        except schema.SchemaError as e:
            raise ScenarioInvalid(f"{cx.where}: {e}") from None
        child_soft = self._soft_of(raw, cx.soft)
        w = f"{cx.where}<{target.stem}>"
        self.include_stack.append(target)
        self.defaults_stack.append(dict(doc.get("params") or {}))
        self.scope_stack.append(dict(s.get("params") or {}))
        failed = False
        try:
            try:
                failed = self._run_list(doc.get("setup") or [], cx.phase, w + ".setup", child_soft, cx.depth + 1, cx.always)
                if failed and not self.opts.no_stop:
                    self._skip(doc.get("actions") or [], cx.phase, w + ".actions", 0, cx.depth + 1, "skipped: setup failed")
                else:
                    failed |= self._run_list(doc.get("actions") or [], cx.phase, w + ".actions", child_soft,
                                             cx.depth + 1, cx.always)
            except Abort as a:
                if a.teardown:
                    self._run_list(doc.get("teardown") or [], cx.phase, w + ".teardown", child_soft, cx.depth + 1, True)
                raise
            failed |= self._run_list(doc.get("teardown") or [], cx.phase, w + ".teardown", child_soft, cx.depth + 1, True)
        finally:
            self.include_stack.pop()
            self.defaults_stack.pop()
            self.scope_stack.pop()
        if failed:
            raise StepFail(f"included {target.name}: a step failed", {"scenario": str(target)})
        return f"included {target.name}", {"scenario": str(target)}


def _rank(finding: dict) -> int:
    return SEVERITY_RANK.get(str(finding.get("severity") or "high").lower(), 3)
