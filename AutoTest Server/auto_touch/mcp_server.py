"""MCP (Model Context Protocol) stdio server for the AutoTest rig.

Newline-delimited JSON-RPC 2.0 on stdin/stdout, implemented directly (stdlib only).
stdout carries protocol frames only: logging goes to stderr and tool bodies run
under redirect_stdout(stderr). Registered in <repo>/.mcp.json (setup.bat does it):

    "autotest": {"command": "py", "args": ["AutoTest Server/auto_touch/mcp_server.py"]}

The path is relative to the project root (the agent host's working directory);
this file bootstraps its own package path, so it also runs as a plain script.
"""
from __future__ import annotations

import contextlib
import json
import pathlib
import sys
from typing import Any, Callable

if __package__ in (None, ""):
    sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

from auto_touch import EXIT_MEANING, __version__, commands, report  # noqa: E402
from auto_touch.bridge import Bridge, BridgeError, ServerRejected, ServerUnreachable  # noqa: E402
from auto_touch.commands import CommandError  # noqa: E402
from auto_touch.config import Config, ConfigError  # noqa: E402
from auto_touch.scenario import RunOptions, Runner  # noqa: E402

SUPPORTED_PROTOCOLS = ("2025-06-18", "2025-03-26", "2024-11-05")
PROTOCOL_FALLBACK = "2025-06-18"


def _cfg() -> Config:
    return Config.load()


def _b() -> tuple[Config, Bridge]:
    cfg = _cfg()
    return cfg, Bridge(cfg)


def _schema(props: dict | None = None, required: list | None = None) -> dict:
    s: dict = {"type": "object", "properties": props or {}, "additionalProperties": False}
    if required:
        s["required"] = required
    return s


S = {"type": "string"}
N = {"type": "number"}
I = {"type": "integer"}
B = {"type": "boolean"}


def _d(desc: str, base: dict) -> dict:
    return {**base, "description": desc}


# ---- tool bodies -------------------------------------------------------------------

def t_status(a):
    cfg, b = _b()
    return b.status()


def t_compile_status(a):
    cfg, b = _b()
    return commands.compile_status(b)


def t_refresh(a):
    cfg, b = _b()
    return commands.refresh(b, wait=a.get("wait", True), exit_play=bool(a.get("exitPlay")))


def t_play(a):
    cfg, b = _b()
    return commands.play(b, bool(a.get("on", True)), a.get("scene"), wait=a.get("wait", True))


def t_logs(a):
    cfg, b = _b()
    if a.get("clear"):
        return b.logs_clear()
    r = b.logs(a.get("since"), a.get("level", "error"), a.get("limit", 100))
    r["entries"] = [report.compact_log(e) for e in r.get("entries") or []]
    return r


def t_screenshot(a):
    cfg, b = _b()
    return commands.screenshot(b, a.get("name"), a.get("superSize"))


def t_state(a):
    cfg, b = _b()
    r = commands.state(b, a.get("path"))
    if not a.get("path"):
        r["state"] = report.clip(r.get("state"), int(a.get("maxChars", 60_000)))
    return r


def t_op(a):
    cfg, b = _b()
    return b.game_op(a["op"], a.get("params") or {}, timeout=a.get("timeoutS"))


def t_ops(a):
    cfg, b = _b()
    return {"ops": b.ops()}


def t_invariants(a):
    cfg, b = _b()
    return b.invariants(a.get("rules"))


def t_targets(a):
    cfg, b = _b()
    return {"targets": b.targets(a.get("kind"))}


def t_ui(a):
    cfg, b = _b()
    return b.ui_components(a.get("kinds"), a.get("root"), a.get("match"),
                           True if a.get("includeInactive") else None)


def t_find(a):
    cfg, b = _b()
    if not (a.get("path") or a.get("text") or a.get("match")):
        raise CommandError(3, "give path, text or match")
    return b.ui_find(a.get("path"), a.get("text"), a.get("match"))


def t_click(a):
    cfg, b = _b()
    return commands.click(b, a.get("path"), a.get("text"), a.get("target"), a.get("x"), a.get("y"))


def t_drag(a):
    cfg, b = _b()
    return commands.drag(b, a.get("from"), a.get("fromTarget"), a.get("x0"), a.get("y0"), a.get("to"),
                         a.get("toTarget"), a.get("x1"), a.get("y1"), a.get("steps"), a.get("frames"))


def t_reflect(a):
    cfg, b = _b()
    return b.reflect(a["op"], type=a.get("type"), member=a.get("member"), method=a.get("method"),
                     args=a.get("args"), value=a.get("value"), static=a.get("static"), path=a.get("path"),
                     has_value="value" in a)


def t_menu(a):
    cfg, b = _b()
    return b.menu(a["item"])


def t_unity_tests(a):
    cfg, b = _b()
    r = commands.unity_tests(cfg, b, a.get("mode", "EditMode"), a.get("filter"), a.get("assembly"),
                             bool(a.get("exitPlay")), a.get("timeoutS"))
    r["meaning"] = EXIT_MEANING.get(r["exitCode"], "")
    return r


def _case_summary(rep: dict) -> dict:
    return {k: rep.get(k) for k in ("name", "verdict", "exitCode", "counts", "failure", "abort",
                                    "reportPath", "markdownPath", "durationMs", "screenshots")}


def t_run_scenario(a):
    cfg, b = _b()
    lines: list[str] = []
    opts = RunOptions(enter_play=True if a.get("enterPlay") else None, exit_play=bool(a.get("exitPlay")),
                      timescale=a.get("timescale"), no_stop=bool(a.get("noStop")), case=a.get("case"),
                      params=dict(a.get("params") or {}))
    res = Runner(cfg, b, opts, out=lines.append).run_path(a["scenario"])
    return {"exitCode": res["exitCode"], "meaning": EXIT_MEANING.get(res["exitCode"], ""),
            "verdict": res["verdict"], "problems": res.get("problems") or [],
            "cases": [_case_summary(r) for r in res.get("cases") or []], "console": lines[-40:]}


def t_run_campaign(a):
    from auto_touch.campaign import run_campaign
    cfg, b = _b()
    lines: list[str] = []
    opts = RunOptions(enter_play=True if a.get("enterPlay") else None, exit_play=bool(a.get("exitPlay")),
                      params=dict(a.get("params") or {}))
    res = run_campaign(cfg, a["campaign"], opts, b, out=lines.append)
    return {"exitCode": res["exitCode"], "meaning": EXIT_MEANING.get(res["exitCode"], ""),
            "verdict": res["verdict"], "rows": res.get("rows"), "problems": res.get("problems") or [],
            "reportPath": res.get("reportPath"), "csvPath": res.get("csvPath"), "console": lines[-40:]}


def t_list_scenarios(a):
    cfg = _cfg()
    return {"scenariosDir": str(cfg.scenarios_path), "items": commands.list_scenarios(cfg)}


def t_validate_scenario(a):
    cfg = _cfg()
    return commands.validate(cfg, a["scenario"], list(a.get("params") or []))


SCENARIO_PARAMS = _d("external ${name} values for the scenario", {"type": "object"})

TOOLS: list[dict] = [
    {"name": "autotest_status", "fn": t_status,
     "description": "Unity Editor bridge status: isPlaying, isCompiling, lastCompileErrors, activeScene, timeScale, "
                    "adapter {present, available, gameId}. Call first; an error means Unity is not open or the server "
                    "is off (Tools > AutoTest > Start Server, port 8766).",
     "inputSchema": _schema()},
    {"name": "autotest_compile_status", "fn": t_compile_status,
     "description": "Last script compile result with every error (file, line, message). Works while the game "
                    "does not compile.",
     "inputSchema": _schema()},
    {"name": "autotest_refresh", "fn": t_refresh,
     "description": "AssetDatabase.Refresh + script recompile (works with Unity unfocused). With wait (default) "
                    "returns errorCount and errorLines once compiling is over.",
     "inputSchema": _schema({"wait": _d("wait for the compile (default true)", B),
                             "exitPlay": _d("exit Play mode first", B)})},
    {"name": "autotest_play", "fn": t_play,
     "description": "Enter (on=true) or exit Play mode and wait until the transition is over.",
     "inputSchema": _schema({"on": _d("true = enter Play, false = exit", B),
                             "scene": _d("scene asset path to open before entering", S),
                             "wait": _d("wait for the transition (default true)", B)}, ["on"])},
    {"name": "autotest_logs", "fn": t_logs,
     "description": "Unity console ring buffer. Pass the returned nextIndex as since to read only newer entries. "
                    "level error (default) | warning | all.",
     "inputSchema": _schema({"since": _d("first log index", I),
                             "level": {"type": "string", "enum": ["error", "warning", "all"]},
                             "limit": _d("max entries (default 100)", I), "clear": _d("clear the buffer", B)})},
    {"name": "autotest_screenshot", "fn": t_screenshot,
     "description": "Capture the Game view (Play mode) to auto_touch/out/screens/<name>.png and wait for the file; "
                    "returns its absolute path (open it with the Read tool to look at it).",
     "inputSchema": _schema({"name": _d("file stem", S), "superSize": _d("1-4", I)})},
    {"name": "autotest_state", "fn": t_state,
     "description": "The game adapter's read-only state snapshot, or one dotted path of it (player.gold, units[0].hp).",
     "inputSchema": _schema({"path": _d("dotted path into the state", S),
                             "maxChars": _d("clip a whole-state reply (default 60000)", I)})},
    {"name": "autotest_op", "fn": t_op,
     "description": "Run a named game op of the adapter (IAutoTestGameOps), e.g. add-gold {amount: 10}. "
                    "Mutating; never retried. List the ops with autotest_ops.",
     "inputSchema": _schema({"op": _d("op name", S), "params": _d("op parameters", {"type": "object"}),
                             "timeoutS": _d("HTTP timeout for a slow op", N)}, ["op"])},
    {"name": "autotest_ops", "fn": t_ops, "description": "Names of the game ops the adapter offers.",
     "inputSchema": _schema()},
    {"name": "autotest_invariants", "fn": t_invariants,
     "description": "Evaluate the adapter's game invariants: findings {rule, severity high|medium|low, detail}.",
     "inputSchema": _schema({"rules": _d("comma-separated rule names (default all)", S)})},
    {"name": "autotest_targets", "fn": t_targets,
     "description": "Adapter click/drag targets with stable ids (shop:2, bench:0, board:1,3), screen center/rect "
                    "(bottom-left origin), visible, interactable.",
     "inputSchema": _schema({"kind": _d("filter by kind", S)})},
    {"name": "autotest_ui", "fn": t_ui,
     "description": "uGUI components on screen (buttons, toggles, sliders, inputs, texts...) with path, caption, "
                    "active, interactable and screen rect.",
     "inputSchema": _schema({"kinds": _d("csv of button,toggle,slider,input,dropdown,scroll,text,image", S),
                             "root": _d("hierarchy path to search under", S),
                             "match": _d("substring of path or caption", S),
                             "includeInactive": _d("include inactive objects", B)})},
    {"name": "autotest_find", "fn": t_find,
     "description": "Find one UI object by exact path, caption text or path substring; found:false is a normal answer.",
     "inputSchema": _schema({"path": S, "text": S, "match": S})},
    {"name": "autotest_click", "fn": t_click,
     "description": "Click through the EventSystem (no OS mouse): by hierarchy path, caption text, adapter target id, "
                    "or screen x,y (bottom-left origin). Returns executed, targetPath, mode, message. Mutating.",
     "inputSchema": _schema({"path": S, "text": S, "target": _d("adapter target id from autotest_targets", S),
                             "x": N, "y": N})},
    {"name": "autotest_drag", "fn": t_drag,
     "description": "Drag gesture (pointerDown, beginDrag, drag..., drop, endDrag) from a path / target / point to a "
                    "path / target / point; waits until done and returns {executed, dragTarget, dropTarget, fired}.",
     "inputSchema": _schema({"from": _d("start GameObject path", S), "fromTarget": _d("start target id", S),
                             "x0": N, "y0": N, "to": _d("end GameObject path", S), "toTarget": _d("end target id", S),
                             "x1": N, "y1": N, "steps": _d("2-120 (default 12)", I), "frames": _d("1-10 per step", I)})},
    {"name": "autotest_reflect", "fn": t_reflect,
     "description": "Reflection into game code: op types | members | get | set | call. A throwing method returns "
                    "threw:true with the exception (a finding, not a tool error).",
     "inputSchema": _schema({"op": {"type": "string", "enum": ["types", "members", "get", "set", "call"]},
                             "type": _d("full or short type name", S), "member": S, "method": S,
                             "args": _d("method arguments", {"type": "array"}), "value": _d("value to set (any JSON)", {}),
                             "static": B, "path": _d("GameObject path of the instance component", S)}, ["op"])},
    {"name": "autotest_menu", "fn": t_menu,
     "description": "Execute an editor menu item by path (edit mode), e.g. 'Assets/Refresh'.",
     "inputSchema": _schema({"item": S}, ["item"])},
    {"name": "autotest_unity_tests", "fn": t_unity_tests,
     "description": "Run the Unity Test Runner (EditMode default or PlayMode) and wait: totals, failures, report path. "
                    "exitCode 4 = failures, 3 = no test matched.",
     "inputSchema": _schema({"mode": {"type": "string", "enum": ["EditMode", "PlayMode"]},
                             "filter": _d("test name substring or full name", S), "assembly": S,
                             "exitPlay": _d("leave Play mode first when needed", B),
                             "timeoutS": _d("seconds (default compile_timeout_s)", N)})},
    {"name": "autotest_run_scenario", "fn": t_run_scenario,
     "description": "Run a scenario JSON file (path relative to 'AutoTest Server/', or to scenarios_dir) - every "
                    "matrix case - and return the verdict, failing step and report paths "
                    "(auto_touch/out/diagnostics/*.json, out/reports/*.md). exitCode 0 PASS, 4 FAIL, 3 invalid, "
                    "2 unreachable, 5 compile errors.",
     "inputSchema": _schema({"scenario": _d("scenario path", S), "enterPlay": _d("enter Play when needed", B),
                             "exitPlay": _d("exit Play at the end", B), "timescale": N,
                             "noStop": _d("keep going after a failed step", B),
                             "case": _d("matrix filter k=v,k2=v2 or label substring", S),
                             "params": SCENARIO_PARAMS}, ["scenario"])},
    {"name": "autotest_run_campaign", "fn": t_run_campaign,
     "description": "Run a campaign file (a list of scenarios, one Play session) and return one row per case plus the "
                    "Markdown/CSV report paths. exitCode 0 only if every case passed.",
     "inputSchema": _schema({"campaign": _d("campaign file path", S), "enterPlay": B, "exitPlay": B,
                             "params": SCENARIO_PARAMS}, ["campaign"])},
    {"name": "autotest_list_scenarios", "fn": t_list_scenarios,
     "description": "Scenario and campaign files under scenarios_dir with the first line of their note.",
     "inputSchema": _schema()},
    {"name": "autotest_validate_scenario", "fn": t_validate_scenario,
     "description": "Offline schema check of a scenario or campaign file (actions, operators, placeholders, includes); "
                    "no Unity needed.",
     "inputSchema": _schema({"scenario": _d("file path", S),
                             "params": _d("names of parameters supplied from outside", {"type": "array", "items": S})},
                            ["scenario"])},
]
TOOL_MAP: dict[str, dict] = {t["name"]: t for t in TOOLS}


# ---- JSON-RPC over stdio ---------------------------------------------------------

def _log(msg: str) -> None:
    print(f"[autotest-mcp] {msg}", file=sys.stderr, flush=True)


def _write(msg: dict) -> None:
    sys.stdout.write(json.dumps(msg, separators=(",", ":"), ensure_ascii=False, default=str) + "\n")
    sys.stdout.flush()


def _reply(req_id, result=None, error=None) -> None:
    msg: dict[str, Any] = {"jsonrpc": "2.0", "id": req_id}
    if error is not None:
        msg["error"] = error
    else:
        msg["result"] = result
    _write(msg)


def _error_content(error: str, message: str, **extra) -> dict:
    return {"content": [{"type": "text", "text": json.dumps({"error": error, "message": message, **extra})}],
            "isError": True}


def call_tool(name: str, arguments: dict) -> dict:
    tool = TOOL_MAP.get(name)
    if tool is None:
        return _error_content("UnknownTool", f"unknown tool {name!r}")
    fn: Callable = tool["fn"]
    try:
        with contextlib.redirect_stdout(sys.stderr):
            payload = fn(arguments or {})
        return {"content": [{"type": "text", "text": json.dumps(payload, indent=1, ensure_ascii=False, default=str)}],
                "isError": False}
    except ServerUnreachable as e:
        return _error_content("ServerUnreachable", str(e), exitCode=2)
    except ServerRejected as e:
        return _error_content("ServerRejected", e.message, status=e.status, kind=e.kind)
    except CommandError as e:
        return _error_content("CommandError", e.message, exitCode=e.code)
    except (BridgeError, ConfigError) as e:
        return _error_content(type(e).__name__, str(e))
    except KeyError as e:
        return _error_content("BadArguments", f"missing argument {e}")
    except Exception as e:  # a tool crash must never kill the server
        _log(f"tool {name} crashed: {e!r}")
        return _error_content("InternalError", repr(e))


def dispatch(msg: dict) -> None:
    method = msg.get("method")
    req_id = msg.get("id")
    params = msg.get("params") or {}
    if method == "initialize":
        proto = params.get("protocolVersion")
        _reply(req_id, {"protocolVersion": proto if proto in SUPPORTED_PROTOCOLS else PROTOCOL_FALLBACK,
                        "capabilities": {"tools": {"listChanged": False}},
                        "serverInfo": {"name": "autotest", "version": __version__},
                        "instructions": "Unity Editor AutoTest bridge for auto-chess-2 (port 8766). Start with "
                                        "autotest_status; run scenarios with autotest_run_scenario."})
    elif method in ("notifications/initialized", "notifications/cancelled"):
        pass
    elif method == "ping":
        _reply(req_id, {})
    elif method == "tools/list":
        _reply(req_id, {"tools": [{"name": t["name"], "description": t["description"],
                                   "inputSchema": t["inputSchema"]} for t in TOOLS]})
    elif method == "tools/call":
        _reply(req_id, call_tool(str(params.get("name", "")), params.get("arguments") or {}))
    elif req_id is not None:
        _reply(req_id, error={"code": -32601, "message": f"method not found: {method}"})


def main() -> int:
    for stream in (sys.stdin, sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass
    _log(f"auto_touch MCP server {__version__} starting")
    for raw in sys.stdin:
        raw = raw.strip()
        if not raw:
            continue
        try:
            msg = json.loads(raw)
        except json.JSONDecodeError:
            _write({"jsonrpc": "2.0", "id": None, "error": {"code": -32700, "message": "parse error"}})
            continue
        for m in (msg if isinstance(msg, list) else [msg]):
            if isinstance(m, dict):
                try:
                    dispatch(m)
                except Exception as e:  # never die on one bad frame
                    _log(f"dispatch failed: {e!r}")
                    if m.get("id") is not None:
                        _reply(m.get("id"), error={"code": -32603, "message": repr(e)})
    _log("stdin closed - exiting")
    return 0


if __name__ == "__main__":
    sys.exit(main())
