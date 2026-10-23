"""Command line: `py -m auto_touch [--json] [--host H] [--port N] [--config F] <command> ...`

Exit codes (PROTOCOL.md section 4): 0 ok/PASS, 2 server unreachable, 3 invalid input,
4 FAIL, 5 Unity compile errors, 10 installed / needs Unity compile.
`--json` (before or after the command; for `op` put it before, since `op --json`
carries the op parameters) prints one machine-readable JSON object.
"""
from __future__ import annotations

import argparse
import json
import sys
from typing import Any, Callable

from . import (EXIT_COMPILE, EXIT_FAIL, EXIT_INVALID, EXIT_OK, EXIT_UNREACHABLE, __version__,
               commands, matrix, report, schema)
from .bridge import Bridge, BridgeError, ServerRejected, ServerUnreachable, WaitTimeout
from .commands import CommandError
from .config import Config, ConfigError, DEFAULT_CONFIG_PATH, write_default_config
from .scenario import SEVERITY_RANK, RunOptions, Runner, resolve_scenario


class _Parser(argparse.ArgumentParser):
    def error(self, message: str):  # argparse would exit 2, which means "server unreachable" here
        self.print_usage(sys.stderr)
        print(f"{self.prog}: error: {message}", file=sys.stderr)
        raise SystemExit(EXIT_INVALID)


class Ctx:
    def __init__(self, cfg: Config, args: argparse.Namespace):
        self.cfg = cfg
        self.args = args
        self.json = bool(getattr(args, "json_out", False))
        self.bridge = Bridge(cfg)

    def say(self, line: str = "") -> None:
        print(line, file=sys.stderr if self.json else sys.stdout, flush=True)

    def emit(self, data: Any, text: Callable[[], None] | str | list | None = None) -> None:
        if self.json:
            print(json.dumps(data, indent=2, ensure_ascii=False, default=str), flush=True)
        elif callable(text):
            text()
        elif isinstance(text, list):
            for line in text:
                print(line)
        elif text is not None:
            print(text)
        else:
            print(json.dumps(data, indent=2, ensure_ascii=False, default=str))


def _onoff(v: str) -> bool:
    v = v.strip().lower()
    if v in ("on", "1", "true", "yes"):
        return True
    if v in ("off", "0", "false", "no"):
        return False
    raise argparse.ArgumentTypeError("expected on or off")


# ---- commands --------------------------------------------------------------------

def cmd_status(a, c: Ctx) -> int:
    st = c.bridge.status()

    def text():
        ad = st.get("adapter") or {}
        mode = "PLAYING" if st.get("isPlaying") else "edit mode"
        if st.get("isPaused"):
            mode += " (paused)"
        if st.get("playModeChanging"):
            mode += " (changing)"
        print(f"Unity {st.get('unityVersion')} - project {st.get('project')} - bridge {st.get('serverVersion')} "
              f"on port {st.get('port', c.cfg.port)}")
        print(f"mode: {mode} - scene {st.get('activeScene')} - timeScale {st.get('timeScale')} - frame {st.get('frameCount')}")
        print(f"compiling: {'yes' if st.get('isCompiling') else 'no'} - last compile errors: {st.get('lastCompileErrors', 0)}")
        if ad.get("present"):
            print(f"adapter: {ad.get('gameId')} ({'available' if ad.get('available') else 'not available yet'})")
        else:
            print("adapter: none (state / game-op / invariants / targets answer 501)")
    c.emit(st, text)
    failed = int(st.get("lastCompileErrors") or 0) > 0 or bool(st.get("scriptCompilationFailed"))
    return EXIT_COMPILE if failed else EXIT_OK


def cmd_ping(a, c: Ctx) -> int:
    r = c.bridge.ping()
    c.emit(r, f"pong - bridge {r.get('serverVersion')} at {c.cfg.base_url}")
    return EXIT_OK


def _print_compile(lines: list[str], count: int) -> None:
    if count:
        print(f"{count} compile error(s):")
        for line in lines[:50]:
            print(f"  {line}")
    else:
        print("no compile errors")


def cmd_compile_status(a, c: Ctx) -> int:
    cs = commands.compile_status(c.bridge)
    last = cs.get("lastCompile") or {}
    count = int(last.get("errorCount") or 0)

    def text():
        print(f"compiling: {'yes' if cs.get('isCompiling') else 'no'} - last compile finished "
              f"{last.get('finishedAtUtc') or '-'} ({last.get('warningCount', 0)} warning(s))")
        _print_compile(cs["errorLines"], count)
    c.emit(cs, text)
    return EXIT_COMPILE if count else EXIT_OK


def cmd_refresh(a, c: Ctx) -> int:
    r = commands.refresh(c.bridge, wait=a.wait, exit_play=a.exit_play)
    if not a.wait:
        c.emit(r, "refresh requested (poll with: compile-status)")
        return EXIT_OK
    c.emit(r, lambda: _print_compile(r["errorLines"], r["errorCount"]))
    return EXIT_COMPILE if r["errorCount"] else EXIT_OK


def cmd_play(a, c: Ctx) -> int:
    r = commands.play(c.bridge, a.on, a.scene, wait=not a.no_wait)
    if a.no_wait:
        c.emit(r, f"Play {'on' if a.on else 'off'} requested")
    else:
        c.emit(r, f"Play mode {'on' if r.get('isPlaying') else 'off'}" + (f" - scene {r.get('activeScene')}" if r.get("isPlaying") else ""))
    return EXIT_OK


def cmd_pause(a, c: Ctx) -> int:
    r = c.bridge.pause(a.on)
    c.emit(r, f"paused: {str(bool(r.get('isPaused'))).lower()}")
    return EXIT_OK


def cmd_step(a, c: Ctx) -> int:
    r = c.bridge.step()
    c.emit(r, f"stepped - frame {r.get('frameCount')}")
    return EXIT_OK


def cmd_timescale(a, c: Ctx) -> int:
    v = c.bridge.timescale(a.value)
    c.emit({"timeScale": v}, f"timeScale = {v:g}")
    return EXIT_OK


def cmd_logs(a, c: Ctx) -> int:
    if a.clear:
        r = c.bridge.logs_clear()
        c.emit(r, f"log buffer cleared - nextIndex {r.get('nextIndex')}")
        return EXIT_OK
    r = c.bridge.logs(a.since, a.level, a.limit)

    def text():
        for e in r.get("entries") or []:
            first = str(e.get("message") or "").splitlines()
            print(f"#{e.get('index')} [{e.get('type')}] {first[0] if first else ''}")
        print(f"-- {len(r.get('entries') or [])} entr(ies), {r.get('redCount', 0)} red, nextIndex {r.get('nextIndex')}"
              + (f", {r.get('dropped')} dropped" if r.get("dropped") else ""))
    c.emit(r, text)
    return EXIT_OK


def cmd_screenshot(a, c: Ctx) -> int:
    r = commands.screenshot(c.bridge, a.name, a.super_size, wait=not a.no_wait)
    c.emit(r, f"screenshot: {r.get('path')}")
    return EXIT_OK


def cmd_state(a, c: Ctx) -> int:
    r = commands.state(c.bridge, a.path)
    if a.path:
        c.emit(r, json.dumps(r.get("value"), indent=2, ensure_ascii=False) if r["found"] else f"{a.path}: not found")
        return EXIT_OK if r["found"] else EXIT_FAIL
    c.emit(r, json.dumps(r.get("state"), indent=2, ensure_ascii=False))
    return EXIT_OK


def cmd_op(a, c: Ctx) -> int:
    params = commands.parse_kv(a.kv)
    if a.params_json:
        try:
            extra = json.loads(a.params_json)
        except json.JSONDecodeError as e:
            raise CommandError(EXIT_INVALID, f"--json is not valid JSON: {e}") from None
        if not isinstance(extra, dict):
            raise CommandError(EXIT_INVALID, "--json must be a JSON object")
        params.update(extra)
    r = c.bridge.game_op(a.op, params, timeout=a.timeout)
    c.emit(r, json.dumps(r.get("result"), indent=2, ensure_ascii=False))
    return EXIT_OK


def cmd_ops(a, c: Ctx) -> int:
    ops = c.bridge.ops()
    c.emit({"ops": ops}, ops and [f"  {o}" for o in ops] or "no ops")
    return EXIT_OK


def cmd_invariants(a, c: Ctx) -> int:
    r = c.bridge.invariants(a.rules)
    findings = r.get("findings") or []
    threshold = SEVERITY_RANK.get(c.cfg.fail_on_invariants, 99)
    bad = [f for f in findings if SEVERITY_RANK.get(str(f.get("severity")).lower(), 3) >= threshold]

    def text():
        print(f"rules: {', '.join(r.get('rules') or []) or '-'}")
        for f in findings:
            print(f"  [{f.get('severity')}] {f.get('rule')}: {f.get('detail')}")
        print(f"-- {len(findings)} finding(s), {r.get('highCount', 0)} high")
    c.emit(r, text)
    return EXIT_FAIL if bad else EXIT_OK


def cmd_targets(a, c: Ctx) -> int:
    targets = c.bridge.targets(a.kind)

    def text():
        for t in targets:
            flags = ("" if t.get("visible", True) else " hidden") + ("" if t.get("interactable", True) else " disabled")
            print(f"  {t.get('id'):<14} {t.get('kind', ''):<8} {t.get('label', '')!s:<20} center={t.get('center')}{flags}")
        print(f"-- {len(targets)} target(s)")
    c.emit({"targets": targets}, text)
    return EXIT_OK


def cmd_ui(a, c: Ctx) -> int:
    r = c.bridge.ui_components(a.kinds, a.root, a.match, True if a.include_inactive else None)
    items = r.get("items") or []

    def text():
        for it in items:
            state = ("" if it.get("active", True) else " inactive") + ("" if it.get("interactable", True) else " disabled")
            cap = f" {it.get('caption')!r}" if it.get("caption") else ""
            print(f"  [{it.get('kind')}] {it.get('path')}{cap} center={it.get('center')}{state}")
        print(f"-- {len(items)} element(s)")
    c.emit(r, text)
    return EXIT_OK


def cmd_find(a, c: Ctx) -> int:
    if not (a.path or a.text or a.match):
        raise CommandError(EXIT_INVALID, "find needs --path, --text or --match")
    r = c.bridge.ui_find(a.path, a.text, a.match)
    c.emit(r, (f"found {r.get('path')} active={r.get('activeInHierarchy')} interactable={r.get('interactable')} "
               f"caption={r.get('caption')!r} center={r.get('center')}") if r.get("found") else "not found")
    return EXIT_OK if r.get("found") else EXIT_FAIL


def cmd_click(a, c: Ctx) -> int:
    r = commands.click(c.bridge, a.path, a.text, a.target, a.x, a.y)
    c.emit(r, f"executed={str(bool(r.get('executed'))).lower()} target={r.get('targetPath')} mode={r.get('mode')}"
              + (f" - {r.get('message')}" if r.get("message") else ""))
    return EXIT_OK if r.get("executed") else EXIT_FAIL


def cmd_drag(a, c: Ctx) -> int:
    r = commands.drag(c.bridge, a.from_path, a.from_target, a.x0, a.y0, a.to_path, a.to_target, a.x1, a.y1,
                      a.steps, a.frames)
    res = r.get("result") or {}
    if not r["start"].get("started", True):
        c.emit(r, f"drag did not start: {r['start'].get('message')}")
        return EXIT_FAIL
    c.emit(r, f"executed={str(bool(res.get('executed'))).lower()} drag={res.get('dragTarget')} drop={res.get('dropTarget')} "
              f"fired={res.get('fired')}")
    return EXIT_OK if res.get("executed") else EXIT_FAIL


def cmd_reflect(a, c: Ctx) -> int:
    args = commands.parse_value(a.args) if a.args is not None else None
    has_value = a.value is not None
    value = commands.parse_value(a.value) if has_value else None
    r = c.bridge.reflect(a.op, type=a.type, member=a.member, method=a.method, args=args, value=value,
                         static=a.static or None, path=a.path, has_value=has_value)
    if r.get("threw"):
        c.emit(r, f"THREW {r.get('exception')}\n{r.get('stack') or ''}".rstrip())
        return EXIT_FAIL
    key = {"types": "types", "members": "members", "get": "value", "call": "returned"}.get(a.op)
    c.emit(r, json.dumps(r.get(key) if key else r, indent=2, ensure_ascii=False))
    return EXIT_OK


def cmd_menu(a, c: Ctx) -> int:
    r = c.bridge.menu(a.item)
    c.emit(r, f"executed={str(bool(r.get('executed'))).lower()}")
    return EXIT_OK if r.get("executed") else EXIT_FAIL


def cmd_hierarchy(a, c: Ctx) -> int:
    r = c.bridge.hierarchy(a.root, a.depth, False if a.active_only else None, True if a.components else None)

    def walk(nodes, depth=0):
        for n in nodes or []:
            flag = "" if n.get("activeInHierarchy", n.get("active", True)) else " (inactive)"
            comps = f" [{', '.join(n.get('components') or [])}]" if n.get("components") else ""
            print(f"{'  ' * depth}{n.get('name')}{flag}{comps}")
            walk(n.get("children"), depth + 1)
    c.emit(r, lambda: walk(r.get("nodes")))
    return EXIT_OK


def cmd_raycast(a, c: Ctx) -> int:
    c.emit(c.bridge.raycast(a.x, a.y))
    return EXIT_OK


def cmd_screen_point(a, c: Ctx) -> int:
    r = c.bridge.screen_point(a.path)
    c.emit(r)
    return EXIT_OK if r.get("found") else EXIT_FAIL


def cmd_unity_tests(a, c: Ctx) -> int:
    r = commands.unity_tests(c.cfg, c.bridge, a.mode, a.filter, a.assembly, a.exit_play, a.timeout)

    def text():
        print(f"Unity tests {r['mode']}: {r['passed']} passed, {r['failed']} failed, {r['skipped']} skipped "
              f"of {r['total']} ({float(r.get('durationSec') or 0):.1f} s)")
        for f in r["failures"][:30]:
            first = f["message"].splitlines()
            print(f"  FAIL {f['name']}: {first[0] if first else ''}")
        if r["total"] == 0:
            print("  no test matched the filter / assembly")
        print(f"  report: {report.rel(r['reportPath'])}")
    c.emit(r, text)
    return r["exitCode"]


def _summary(res: dict) -> dict:
    keep = ("name", "case", "verdict", "exitCode", "counts", "failure", "abort", "reportPath", "markdownPath",
            "durationMs", "screenshots")
    return {"scenario": res.get("scenario"), "exitCode": res["exitCode"], "verdict": res["verdict"],
            "problems": res.get("problems") or [],
            "cases": [{k: rep.get(k) for k in keep} for rep in res.get("cases") or []]}


def _run_opts(a) -> RunOptions:
    return RunOptions(enter_play=True if a.enter_play else None, exit_play=a.exit_play,
                      timescale=getattr(a, "timescale", None), no_stop=a.no_stop, verbose=a.verbose,
                      case=getattr(a, "case", None), params=commands.parse_kv(a.param))


def cmd_run(a, c: Ctx) -> int:
    path = resolve_scenario(c.cfg, a.scenario)
    if path is not None:
        try:
            doc = schema.load_doc(path)
        except schema.SchemaError:
            doc = None
        if doc is not None and schema.kind_of(doc) == "campaign":
            c.say(f"{path.name} is a campaign file - running it as a campaign")
            return cmd_campaign(argparse.Namespace(**{**vars(a), "file": a.scenario}), c)
        if a.list_cases and doc is not None:
            try:
                cases = matrix.select(matrix.expand(doc), a.case)
            except matrix.MatrixError as e:
                raise CommandError(EXIT_INVALID, str(e)) from None
            names = [case.name(path.stem) for case in cases]
            c.emit({"scenario": str(path), "cases": [{"name": n, "params": cs.params} for n, cs in zip(names, cases)]},
                   [f"  {n}" for n in names] + [f"-- {len(names)} case(s)"])
            return EXIT_OK
    runner = Runner(c.cfg, c.bridge, _run_opts(a), out=c.say)
    res = runner.run_path(a.scenario)
    if c.json:
        c.emit(_summary(res))
    return res["exitCode"]


def cmd_campaign(a, c: Ctx) -> int:
    from .campaign import run_campaign
    opts = _run_opts(a)
    res = run_campaign(c.cfg, a.file, opts, c.bridge, out=c.say)
    if c.json:
        c.emit({k: res.get(k) for k in ("path", "exitCode", "verdict", "rows", "problems", "reportPath", "csvPath",
                                         "durationMs")})
    return res["exitCode"]


def cmd_list(a, c: Ctx) -> int:
    items = commands.list_scenarios(c.cfg)

    def text():
        for kind in ("scenario", "campaign", "broken"):
            group = [i for i in items if i["kind"] == kind]
            if group:
                print(f"{kind}s:")
                for i in group:
                    print(f"  {i['path']}  - {i['note']}")
        if not items:
            print(f"no scenarios under {c.cfg.scenarios_path}")
    c.emit({"scenariosDir": str(c.cfg.scenarios_path), "items": items}, text)
    return EXIT_OK


def cmd_validate(a, c: Ctx) -> int:
    v = commands.validate(c.cfg, a.file, [k.split("=", 1)[0] for k in a.param or []])

    def text():
        head = "OK" if v["ok"] else "INVALID"
        files = "scenario file(s)" if v["kind"] == "campaign" else "included file(s)"
        print(f"{head} {v['path']} ({v['kind']}, {v['cases']} case(s)"
              + (f", {len(v['includes'])} {files}" if v["includes"] else "") + ")")
        for e in v["errors"]:
            print(f"  error: {e}")
        for w in v["warnings"]:
            print(f"  warning: {w}")
    c.emit(v, text)
    return EXIT_OK if v["ok"] else EXIT_INVALID


def cmd_mcp(a, c: Ctx) -> int:
    from .mcp_server import main as mcp_main
    return mcp_main()


def cmd_config(a, c: Ctx) -> int:
    if a.init:
        wrote = write_default_config(DEFAULT_CONFIG_PATH, overwrite=False)
        c.say(f"{'wrote' if wrote else 'kept existing'} {DEFAULT_CONFIG_PATH}")
    data = {"configPath": c.cfg.config_path, "values": c.cfg.to_dict(), "sources": c.cfg.sources,
            "unknownKeys": c.cfg.unknown_keys, "outDir": str(c.cfg.out_path), "scenariosDir": str(c.cfg.scenarios_path)}

    def text():
        print(f"config file: {c.cfg.config_path or '(none - defaults)'}")
        for k, v in c.cfg.to_dict().items():
            print(f"  {k:<22} = {json.dumps(v)}  [{c.cfg.sources.get(k, 'default')}]")
        if c.cfg.unknown_keys:
            print(f"  unknown keys ignored: {', '.join(c.cfg.unknown_keys)}")
    c.emit(data, text)
    return EXIT_OK


def cmd_register_mcp(a, c: Ctx) -> int:
    r = commands.register_mcp()
    c.emit(r, f"{'registered' if r['changed'] else 'already registered'} MCP server 'autotest' in {r['path']} "
              f"(servers: {', '.join(r['servers'])})")
    return EXIT_OK


# ---- parser ----------------------------------------------------------------------

def build_parser() -> argparse.ArgumentParser:
    p = _Parser(prog="py -m auto_touch", description="AutoTest toolkit for the auto-chess-2 Unity project "
                "(bridge on 127.0.0.1:8766, see AutoTest Server/README.md).")
    p.add_argument("--version", action="version", version=f"auto_touch {__version__}")
    p.add_argument("--json", dest="json_out", action="store_true", help="machine-readable JSON output")
    p.add_argument("--host", help="bridge host (default 127.0.0.1)")
    p.add_argument("--port", type=int, help="bridge port (default 8766)")
    p.add_argument("--config", help="config file (default auto_touch/autotest.config.json)")
    common = _Parser(add_help=False)
    common.add_argument("--json", dest="json_out", action="store_true", default=argparse.SUPPRESS,
                        help="machine-readable JSON output")
    sub = p.add_subparsers(dest="command", metavar="<command>", parser_class=_Parser)

    def add(name, fn, help_, json_flag=True):
        sp = sub.add_parser(name, help=help_, description=help_, parents=[common] if json_flag else [])
        sp.set_defaults(func=fn)
        return sp

    add("status", cmd_status, "bridge / editor status (exit 2 unreachable, 5 compile errors)")
    add("ping", cmd_ping, "is the bridge listening")
    add("compile-status", cmd_compile_status, "last compile result (exit 5 on errors)")
    sp = add("refresh", cmd_refresh, "AssetDatabase.Refresh + recompile (exit 5 on compile errors)")
    sp.add_argument("--wait", action="store_true", help="wait for the compile and report errors")
    sp.add_argument("--exit-play", action="store_true", help="exit Play mode first")
    sp = add("play", cmd_play, "enter / exit Play mode")
    sp.add_argument("on", type=_onoff, help="on | off")
    sp.add_argument("--scene", help="scene asset path to open before entering Play")
    sp.add_argument("--no-wait", action="store_true", help="return right after the request")
    sp = add("pause", cmd_pause, "pause / unpause Play mode")
    sp.add_argument("on", type=_onoff, help="on | off")
    add("step", cmd_step, "advance one frame (EditorApplication.Step)")
    sp = add("timescale", cmd_timescale, "read or set Time.timeScale")
    sp.add_argument("value", nargs="?", type=float, help="new value 0-100 (omit to read)")
    sp = add("logs", cmd_logs, "console log ring buffer")
    sp.add_argument("--since", type=int, help="first index (pass the previous nextIndex)")
    sp.add_argument("--level", choices=("error", "warning", "all"), default="all")
    sp.add_argument("--limit", type=int, help="max entries (bridge default 200)")
    sp.add_argument("--clear", action="store_true", help="clear the buffer")
    sp = add("screenshot", cmd_screenshot, "capture the Game view to out/screens (waits for the file)")
    sp.add_argument("--name", help="file stem (default: timestamp)")
    sp.add_argument("--super-size", type=int, choices=(1, 2, 3, 4))
    sp.add_argument("--no-wait", action="store_true")
    sp = add("state", cmd_state, "adapter game state (or one dotted path)")
    sp.add_argument("--path", help="dotted path, e.g. player.gold or units[0].hp")
    sp = add("op", cmd_op, "run a game op: op <name> [key=value ...] [--json '{...}']", json_flag=False)
    sp.add_argument("op")
    sp.add_argument("kv", nargs="*", metavar="key=value", help="op parameters (values parsed as JSON when possible)")
    sp.add_argument("--json", dest="params_json", metavar="JSON", help="op parameters as a JSON object")
    sp.add_argument("--timeout", type=float, help="HTTP timeout in seconds for a slow op")
    add("ops", cmd_ops, "list the adapter's game ops")
    sp = add("invariants", cmd_invariants, "evaluate the adapter's invariants (exit 4 at/above fail_on_invariants)")
    sp.add_argument("--rules", help="comma-separated rule names")
    sp = add("targets", cmd_targets, "adapter click/drag targets")
    sp.add_argument("--kind", help="filter by kind (shop, bench, board, ...)")
    sp = add("ui", cmd_ui, "uGUI components on screen")
    sp.add_argument("--kinds", help="csv of button,toggle,slider,input,dropdown,scroll,text,image")
    sp.add_argument("--match", help="substring of path or caption")
    sp.add_argument("--root", help="hierarchy path to search under")
    sp.add_argument("--include-inactive", action="store_true")
    sp = add("find", cmd_find, "find one UI object (exit 4 when not found)")
    sp.add_argument("--path")
    sp.add_argument("--text")
    sp.add_argument("--match")
    sp = add("click", cmd_click, "click by path, caption text, target id or screen point (exit 4 when not executed)")
    sp.add_argument("--path")
    sp.add_argument("--text")
    sp.add_argument("--target")
    sp.add_argument("--x", type=float)
    sp.add_argument("--y", type=float)
    sp = add("drag", cmd_drag, "drag gesture (waits until done)")
    sp.add_argument("--from", dest="from_path")
    sp.add_argument("--from-target")
    sp.add_argument("--x0", type=float)
    sp.add_argument("--y0", type=float)
    sp.add_argument("--to", dest="to_path")
    sp.add_argument("--to-target")
    sp.add_argument("--x1", type=float)
    sp.add_argument("--y1", type=float)
    sp.add_argument("--steps", type=int)
    sp.add_argument("--frames", type=int)
    sp = add("reflect", cmd_reflect, "call game code by reflection (exit 4 when it throws)")
    sp.add_argument("--op", required=True, choices=("types", "members", "get", "set", "call"))
    sp.add_argument("--type")
    sp.add_argument("--member")
    sp.add_argument("--method")
    sp.add_argument("--args", help="JSON array of arguments")
    sp.add_argument("--value", help="JSON value for set")
    sp.add_argument("--static", action="store_true")
    sp.add_argument("--path", help="GameObject path for an instance component")
    sp = add("menu", cmd_menu, "execute an editor menu item (edit mode)")
    sp.add_argument("item")
    sp = add("hierarchy", cmd_hierarchy, "scene hierarchy")
    sp.add_argument("--root")
    sp.add_argument("--depth", type=int)
    sp.add_argument("--components", action="store_true")
    sp.add_argument("--active-only", action="store_true")
    sp = add("raycast", cmd_raycast, "what is under a screen point")
    sp.add_argument("--x", type=float, required=True)
    sp.add_argument("--y", type=float, required=True)
    sp = add("screen-point", cmd_screen_point, "screen position of a GameObject")
    sp.add_argument("--path", required=True)
    sp = add("unity-tests", cmd_unity_tests, "run the Unity Test Runner (exit 4 on failures)")
    sp.add_argument("--mode", default="EditMode", choices=("EditMode", "PlayMode"))
    sp.add_argument("--filter")
    sp.add_argument("--assembly")
    sp.add_argument("--exit-play", action="store_true", help="leave Play mode first when needed")
    sp.add_argument("--timeout", type=float, help="seconds (default compile_timeout_s)")

    def run_flags(sp_):
        sp_.add_argument("--enter-play", action="store_true", help="enter Play mode when needed")
        sp_.add_argument("--exit-play", action="store_true", help="exit Play mode at the end")
        sp_.add_argument("--no-stop", action="store_true", help="keep going after a failed step")
        sp_.add_argument("--verbose", action="store_true", help="print every step")
        sp_.add_argument("--param", action="append", metavar="key=value", help="external ${key} value (repeatable)")
        sp_.add_argument("--case", help="matrix filter: k=v,k2=v2 or a label substring")
        sp_.add_argument("--timescale", type=float, help="Time.timeScale for the run")
        sp_.add_argument("--list-cases", action="store_true", help="print the matrix cases and exit (no Unity)")

    sp = add("run", cmd_run, "run a scenario file")
    sp.add_argument("scenario")
    run_flags(sp)
    sp = add("campaign", cmd_campaign, "run a campaign file")
    sp.add_argument("file")
    run_flags(sp)
    add("list", cmd_list, "scenarios and campaigns under scenarios_dir")
    sp = add("validate", cmd_validate, "offline schema check of a scenario / campaign (exit 3 on problems)")
    sp.add_argument("file")
    sp.add_argument("--param", action="append", metavar="key[=value]", help="parameter supplied from outside")
    add("mcp", cmd_mcp, "run the MCP stdio server", json_flag=False)
    sp = add("config", cmd_config, "print the effective configuration")
    sp.add_argument("--init", action="store_true", help="write a commented default config if missing")
    add("register-mcp", cmd_register_mcp, "register the MCP server in <repo>/.mcp.json (merge)")
    return p


def _setup_streams() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(errors="replace")
        except (AttributeError, ValueError):
            pass


def main(argv: list[str] | None = None) -> int:
    _setup_streams()
    parser = build_parser()
    try:
        args = parser.parse_args(argv)
    except SystemExit as e:
        return int(e.code or 0) if not isinstance(e.code, str) else EXIT_INVALID
    if not getattr(args, "command", None):
        parser.print_help()
        return EXIT_INVALID
    try:
        cfg = Config.load(args.config, cli={"host": args.host, "port": args.port})
    except ConfigError as e:
        print(f"config error: {e}", file=sys.stderr)
        return EXIT_INVALID
    c = Ctx(cfg, args)

    def fail(code: int, message: str, extra: dict | None = None) -> int:
        if c.json:
            print(json.dumps({"ok": False, "exitCode": code, "error": message, **(extra or {})}, indent=2))
        else:
            print(message, file=sys.stderr)
        return code
    try:
        return int(args.func(args, c))
    except ServerUnreachable as e:
        return fail(EXIT_UNREACHABLE, f"server unreachable: {e}")
    except ServerRejected as e:
        code = EXIT_INVALID if e.status in (400, 404) else EXIT_FAIL
        return fail(code, f"rejected: HTTP {e.status} {e.kind}: {e.message}", {"status": e.status, "kind": e.kind})
    except WaitTimeout as e:
        return fail(EXIT_FAIL, f"timeout: {e}")
    except CommandError as e:
        return fail(e.code, e.message, e.data)
    except BridgeError as e:
        return fail(EXIT_FAIL, str(e))
    except KeyboardInterrupt:
        return fail(130, "interrupted")


if __name__ == "__main__":
    sys.exit(main())
