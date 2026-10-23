"""Operations shared by the CLI and the MCP server. Each returns a JSON-able dict
and raises BridgeError / CommandError; neither prints anything."""
from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from . import EXIT_FAIL, EXIT_INVALID, EXIT_OK, expr, report, schema
from .bridge import Bridge, ServerUnreachable
from .config import REPO_ROOT, SERVER_DIR, Config
from .scenario import compile_error_lines


class CommandError(Exception):
    def __init__(self, code: int, message: str, data: dict | None = None):
        super().__init__(message)
        self.code = code
        self.message = message
        self.data = data or {}


def parse_value(text: str) -> Any:
    """CLI `key=value`: JSON when it parses (5, true, [1,2], {"a":1}, "quoted"), else the raw string."""
    try:
        return json.loads(text)
    except (json.JSONDecodeError, ValueError):
        return text


def parse_kv(pairs: list[str] | None) -> dict:
    out: dict = {}
    for p in pairs or []:
        if "=" not in p:
            raise CommandError(EXIT_INVALID, f"expected key=value, got {p!r}")
        k, v = p.split("=", 1)
        out[k.strip()] = parse_value(v)
    return out


def compile_status(b: Bridge) -> dict:
    cs = b.compile_status()
    cs["errorLines"] = compile_error_lines(cs)
    return cs


def refresh(b: Bridge, wait: bool = True, exit_play: bool = False, timeout: float | None = None) -> dict:
    before = (b.compile_status().get("lastCompile") or {}).get("finishedAtUtc")
    r = b.refresh(exit_play=exit_play)
    out = {"requested": bool(r.get("requested", True))}
    if wait:
        cs = b.wait_compile(before, timeout=timeout)
        last = cs.get("lastCompile") or {}
        out.update(lastCompile=last, errorCount=int(last.get("errorCount") or 0),
                   errorLines=compile_error_lines(cs))
    return out


def play(b: Bridge, on: bool, scene: str | None = None, wait: bool = True, timeout: float | None = None) -> dict:
    try:
        r = b.play(on, scene)
    except ServerUnreachable:
        b.status()  # the reply was cut by the domain reload; a retried GET proves the editor is alive
        r = {"requested": True, "replyLost": True}
    if wait:
        st = b.wait_play(on, timeout)
        r["isPlaying"] = st.get("isPlaying")
        r["activeScene"] = st.get("activeScene")
    return r


def screenshot(b: Bridge, name: str | None = None, super_size: int | None = None, wait: bool = True,
               timeout: float = 15.0) -> dict:
    r = b.screenshot(name, super_size)
    path = r.get("path")
    if wait and path:
        b.wait_screenshot(path, timeout)
        r["pending"] = False
        r["exists"] = True
    return r


def state(b: Bridge, path: str | None = None) -> dict:
    r = b.state()
    if not path:
        return r
    value = expr.resolve(r.get("state"), path)
    return {"gameId": r.get("gameId"), "available": r.get("available"), "path": path,
            "found": value is not expr.MISSING, "value": None if value is expr.MISSING else value}


def find_target(b: Bridge, tid: str) -> dict:
    targets = b.targets()
    for t in targets:
        if str(t.get("id")) == str(tid):
            return t
    raise CommandError(EXIT_FAIL, f"target {tid!r} not found; available: "
                       + ", ".join(str(t.get("id")) for t in targets[:30]))


def click(b: Bridge, path: str | None = None, text: str | None = None, target: str | None = None,
          x: float | None = None, y: float | None = None) -> dict:
    if target:
        t = find_target(b, target)
        c = t.get("center") or [0, 0]
        r = b.ui_click(x=c[0], y=c[1])
        r["target"] = t.get("id")
        return r
    if path or text:
        return b.ui_click(path=path, text=text)
    if x is None or y is None:
        raise CommandError(EXIT_INVALID, "click needs path, text, target, or x and y")
    return b.ui_click(x=x, y=y)


def drag(b: Bridge, from_path=None, from_target=None, x0=None, y0=None, to_path=None, to_target=None,
         x1=None, y1=None, steps=None, frames=None, wait: bool = True, timeout: float = 15.0) -> dict:
    if from_target:
        x0, y0 = (find_target(b, from_target).get("center") or [0, 0])[:2]
    if to_target:
        x1, y1 = (find_target(b, to_target).get("center") or [0, 0])[:2]
    if not from_path and (x0 is None or y0 is None):
        raise CommandError(EXIT_INVALID, "drag needs a start: from, fromTarget, or x0 and y0")
    if not to_path and (x1 is None or y1 is None):
        raise CommandError(EXIT_INVALID, "drag needs an end: to, toTarget, or x1 and y1")
    start = b.drag(x0=x0, y0=y0, x1=x1, y1=y1, from_path=from_path, to_path=to_path, steps=steps, frames=frames)
    out = {"start": start}
    if wait and start.get("started", True):
        out["status"] = b.wait_drag(timeout)
        out["result"] = out["status"].get("result")
    return out


def unity_tests(cfg: Config, b: Bridge, mode: str = "EditMode", filter: str | None = None,
                assembly: str | None = None, exit_play: bool = False, timeout: float | None = None) -> dict:
    st = b.status()
    if st.get("isPlaying"):
        if not exit_play:
            raise CommandError(EXIT_FAIL, "Unity is in Play mode; the Test Runner starts from edit mode "
                                          "(pass --exit-play / exitPlay)")
        play(b, False)
    r = b.tests_run(mode, filter, assembly)
    run_id = r.get("runId")
    final = b.wait_tests(run_id, timeout)
    meta = {"mode": mode, "filter": filter, "assembly": assembly, "runId": run_id}
    md, js = report.write_unity_tests(cfg, final, meta)
    results = final.get("results") or []
    failures = [{"name": x.get("fullName") or x.get("name"), "message": str(x.get("message") or "")[:600]}
                for x in results if str(x.get("result", "")).lower().startswith("fail")]
    total = int(final.get("total") or 0)
    failed = int(final.get("failed") or 0)
    code = EXIT_OK if total > 0 and failed == 0 else (EXIT_INVALID if total == 0 else EXIT_FAIL)
    return {"exitCode": code, "runId": run_id, "mode": mode, "total": total, "passed": final.get("passed", 0),
            "failed": failed, "skipped": final.get("skipped", 0), "durationSec": final.get("durationSec"),
            "failures": failures, "reportPath": str(md), "jsonPath": str(js)}


def list_scenarios(cfg: Config) -> list[dict]:
    root = cfg.scenarios_path
    out = []
    if not root.is_dir():
        return out
    for p in sorted(root.rglob("*.json")):
        try:
            doc = schema.load_doc(p)
        except schema.SchemaError as e:
            out.append({"kind": "broken", "path": report.rel(p), "note": str(e)[:200]})
            continue
        note = str(doc.get("note") or "").strip().splitlines() if isinstance(doc, dict) else []
        entry = {"kind": schema.kind_of(doc), "path": report.rel(p), "note": note[0] if note else ""}
        if isinstance(doc, dict) and doc.get("tags"):
            entry["tags"] = doc["tags"]
        out.append(entry)
    return out


def validate(cfg: Config, ref: str, external: list[str] | None = None) -> dict:
    from .scenario import resolve_scenario
    path = resolve_scenario(cfg, ref)
    if path is None:
        return {"path": ref, "ok": False, "kind": "unknown", "errors": [f"file not found: {ref}"],
                "warnings": [], "cases": 0, "includes": []}
    v = schema.validate_path(path, schema.default_search_dirs(cfg), external=set(external or []))
    d = v.to_dict()
    d["path"] = report.rel(path)
    return d


MCP_ENTRY = {"command": "py", "args": ["AutoTest Server/auto_touch/mcp_server.py"]}


def register_mcp(repo_root: Path = REPO_ROOT, name: str = "autotest") -> dict:
    """Add/refresh our server in <repo>/.mcp.json without touching other servers."""
    path = Path(repo_root) / ".mcp.json"
    data: dict = {}
    if path.exists():
        text = path.read_text(encoding="utf-8-sig").strip()
        if text:
            try:
                data = json.loads(text)
            except json.JSONDecodeError as e:
                raise CommandError(EXIT_INVALID, f"{path} is not valid JSON ({e}); fix it by hand, nothing was written")
        if not isinstance(data, dict):
            raise CommandError(EXIT_INVALID, f"{path} must contain a JSON object")
    servers = data.setdefault("mcpServers", {})
    if not isinstance(servers, dict):
        raise CommandError(EXIT_INVALID, f"{path}: 'mcpServers' must be an object")
    rel_server = (SERVER_DIR / "auto_touch" / "mcp_server.py").resolve()
    try:
        arg = rel_server.relative_to(Path(repo_root).resolve()).as_posix()
    except ValueError:
        arg = str(rel_server)
    entry = {"command": "py", "args": [arg]}
    changed = servers.get(name) != entry
    if changed:
        servers[name] = entry
        path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    return {"path": str(path), "changed": changed, "entry": entry, "servers": sorted(servers)}
