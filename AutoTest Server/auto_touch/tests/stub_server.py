"""Stdlib stub of the Unity AutoTest bridge (every PROTOCOL.md route) with a tiny
fake auto-chess game, for offline development and the unit tests.

    py -m auto_touch.tests.stub_server [port]      (default 8766; Ctrl+C to stop)
    srv = StubServer().start(); ...; srv.stop()    (tests: port 0 = any free port)

Fake game: state {phase, round, gold, counter, items[], board[]}; ops list /
add-gold {amount} / next-phase / boom (throws) / add-item {id} / log {type,
message} / count-to {target} (each /state read then bumps counter by 1);
invariants gold-non-negative (high) and bench-cap (medium, > 8 items); targets
shop:0-2, bench:<i>, board:0; UI Canvas/HUD/{NextButton, GoldButton, LockedButton,
OpenPopup, GoldText}, Canvas/Popup/CloseButton. Hooks for tests: game.drop_next
(close N connections unanswered), game.reject_next[path] = (status, kind, msg),
game.add_log(type, msg), game.compile_errors, game.requests (method, path, query, body).
"""
from __future__ import annotations

import json
import shutil
import sys
import tempfile
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse

PNG_1PX = bytes.fromhex(
    "89504e470d0a1a0a0000000d4948445200000001000000010806000000"
    "1f15c4890000000d49444154789c6360000002000154a24f5d0000000049454e44ae426082")

PLAY_ROUTES = {"/pause", "/step", "/screenshot", "/ui/components", "/ui/find", "/ui/click", "/input/drag",
               "/world/raycast", "/world/screen-point", "/state", "/game-op", "/invariants", "/targets"}
EDIT_ROUTES = {"/menu", "/tests/run"}
ADAPTER_ROUTES = {"/state", "/game-op", "/invariants", "/targets"}
PHASES = ["shop", "combat", "result"]


class Reject(Exception):
    def __init__(self, status: int, kind: str, message: str, **extra):
        super().__init__(message)
        self.status, self.kind, self.message, self.extra = status, kind, message, extra


def _truthy(v) -> bool:
    return str(v).strip().lower() in ("1", "true", "yes", "on")


class FakeGame:
    def __init__(self):
        self.lock = threading.RLock()
        self.screens_dir = Path(tempfile.mkdtemp(prefix="autotest_stub_"))
        self.requests: list[tuple] = []
        self.port = 0
        self.drop_next = 0
        self.reject_next: dict[str, tuple] = {}
        self.reset()

    # ---- state ------------------------------------------------------------------
    def reset(self) -> None:
        with self.lock:
            self.playing = True
            self.paused = False
            self.play_polls = 0           # status polls left while playModeChanging
            self.pending_play: bool | None = None
            self.transition_polls = 2
            self.compiling_polls = 0
            self.compile_errors: list[dict] = []
            self.last_compile = {"startedAtUtc": "2026-01-01T00:00:00Z", "finishedAtUtc": "2026-01-01T00:00:01Z",
                                 "errorCount": 0, "warningCount": 0, "errors": []}
            self.compile_count = 0
            self.time_scale = 1.0
            self.frame = 100
            self.active_scene = "Assets/Scenes/Main.unity"
            self.adapter = True
            self.adapter_available = True
            self.invariants_supported = True
            self.log_reset_on_play = True
            self.logs: list[dict] = []
            self.next_log = 0
            self.count_target: int | None = None
            self.drag_state = {"active": False, "done": False, "result": None, "polls": 0}
            self.tests: dict[str, dict] = {}
            self.test_counter = 0
            self.screenshot_delay = 0.05
            self.menu_items = {"Assets/Refresh", "Tools/AutoTest/Open Output Folder"}
            self.version = "0.9.0"
            self.new_game()

    def new_game(self) -> None:
        self.game = {"phase": "shop", "round": 1, "gold": 10, "counter": 0, "items": [], "board": []}
        self.popup_open = False

    def add_log(self, type_: str, message: str, stack: str = "") -> int:
        with self.lock:
            idx = self.next_log
            self.logs.append({"index": idx, "time": time.strftime("%H:%M:%S"), "type": type_,
                              "message": message, "stackTrace": stack})
            self.next_log += 1
            self.logs = self.logs[-2000:]
            return idx

    # ---- UI model -----------------------------------------------------------------
    def ui_elements(self) -> dict:
        g = self.game
        return {
            "Canvas": {"kind": None, "active": True, "caption": "", "rect": [0, 0, 800, 600]},
            "Canvas/HUD": {"kind": None, "active": True, "caption": "", "rect": [0, 500, 800, 600]},
            "Canvas/HUD/NextButton": {"kind": "button", "active": True, "interactable": True, "caption": "Next",
                                      "rect": [700, 540, 780, 580]},
            "Canvas/HUD/GoldButton": {"kind": "button", "active": True, "interactable": True, "caption": "+1 Gold",
                                      "rect": [600, 540, 680, 580]},
            "Canvas/HUD/LockedButton": {"kind": "button", "active": True, "interactable": False, "caption": "Locked",
                                        "rect": [500, 540, 580, 580]},
            "Canvas/HUD/OpenPopup": {"kind": "button", "active": True, "interactable": True, "caption": "Settings",
                                     "rect": [400, 540, 480, 580]},
            "Canvas/HUD/GoldText": {"kind": "text", "active": True, "caption": f"Gold: {g['gold']}",
                                    "rect": [20, 540, 200, 580]},
            "Canvas/Popup": {"kind": None, "active": self.popup_open, "caption": "", "rect": [200, 200, 600, 400]},
            "Canvas/Popup/CloseButton": {"kind": "button", "active": True, "interactable": True, "caption": "Close",
                                         "rect": [500, 350, 580, 390]},
        }

    def ui_view(self, path: str) -> dict:
        els = self.ui_elements()
        e = els[path]
        parts = path.split("/")
        active_h = all(els.get("/".join(parts[:i]), {"active": True})["active"] for i in range(1, len(parts) + 1))
        r = e["rect"]
        return {"kind": e["kind"], "name": parts[-1], "path": path, "caption": e["caption"], "active": e["active"],
                "activeInHierarchy": active_h, "interactable": e.get("interactable"), "rect": r,
                "center": [(r[0] + r[2]) / 2, (r[1] + r[3]) / 2], "visible": active_h,
                "components": ["RectTransform"] + (["Button"] if e["kind"] == "button" else [])}

    def ui_lookup(self, q: dict) -> dict | None:
        els = self.ui_elements()
        if q.get("path"):
            return self.ui_view(q["path"]) if q["path"] in els else None
        if q.get("text"):
            hits = [p for p in els if q["text"] in els[p]["caption"]]
            active = [p for p in hits if self.ui_view(p)["activeInHierarchy"]]
            pick = (active or hits or [None])[0]
            return self.ui_view(pick) if pick else None
        if q.get("match"):
            hits = [p for p in els if q["match"] in p or q["match"] in els[p]["caption"]]
            return self.ui_view(hits[0]) if hits else None
        raise Reject(400, "bad_request", "path, text or match required")

    def ui_effect(self, path: str) -> None:
        g = self.game
        if path == "Canvas/HUD/NextButton":
            self.next_phase()
        elif path == "Canvas/HUD/GoldButton":
            g["gold"] += 1
        elif path == "Canvas/HUD/OpenPopup":
            self.popup_open = True
        elif path == "Canvas/Popup/CloseButton":
            self.popup_open = False

    def targets(self) -> list[dict]:
        out = []
        for i in range(3):
            out.append(self._target(f"shop:{i}", "shop", f"Unit {'ABC'[i]}", 100 + i * 100, 50, {"cost": 3}))
        for i, item in enumerate(self.game["items"]):
            out.append(self._target(f"bench:{i}", "bench", str(item), 100 + i * 60, 150, {"unit": item}))
        out.append(self._target("board:0", "board", "Board", 400, 300, {}))
        return out

    @staticmethod
    def _target(tid, kind, label, x, y, data) -> dict:
        return {"id": tid, "kind": kind, "label": label, "path": f"World/{kind}/{tid.replace(':', '_')}",
                "center": [float(x), float(y)], "rect": [x - 20.0, y - 20.0, x + 20.0, y + 20.0],
                "visible": True, "interactable": True, "data": data}

    def hit(self, x: float, y: float) -> tuple[str, str] | None:
        for t in self.targets():
            r = t["rect"]
            if r[0] <= x <= r[2] and r[1] <= y <= r[3]:
                return "target", t["id"]
        for path in self.ui_elements():
            v = self.ui_view(path)
            r = v["rect"]
            if v["kind"] == "button" and v["activeInHierarchy"] and r[0] <= x <= r[2] and r[1] <= y <= r[3]:
                return "ui", path
        return None

    def target_effect(self, tid: str) -> None:
        g = self.game
        if tid.startswith("shop:"):
            if g["gold"] >= 3:
                g["gold"] -= 3
                g["items"].append(f"unit{tid.split(':')[1]}")
            else:
                self.add_log("Warning", "not enough gold")

    def next_phase(self) -> None:
        g = self.game
        i = PHASES.index(g["phase"])
        if i == len(PHASES) - 1:
            g["round"] += 1
        g["phase"] = PHASES[(i + 1) % len(PHASES)]

    # ---- request handling -----------------------------------------------------------
    def handle(self, method: str, path: str, q: dict) -> dict:
        if path in self.reject_next:
            status, kind, msg = self.reject_next.pop(path)
            raise Reject(status, kind, msg)
        if path in PLAY_ROUTES and not self.playing:
            raise Reject(409, "play_mode_required", f"{path} needs Play mode")
        if path in EDIT_ROUTES and self.playing:
            raise Reject(409, "edit_mode_required", f"{path} needs edit mode")
        if path in ADAPTER_ROUTES and not self.adapter:
            raise Reject(501, "not_supported", "no game adapter")
        fn = ROUTES.get(path)
        if fn is None:
            raise Reject(404, "not_found", f"unknown route {path}")
        return fn(self, method, q)

    def status_json(self) -> dict:
        if self.compiling_polls > 0:
            self.compiling_polls -= 1
            if self.compiling_polls == 0:
                self._finish_compile()
        if self.pending_play is not None:
            if self.play_polls > 0:
                self.play_polls -= 1
            else:
                self._finish_play()
        self.frame += 1
        return {"serverVersion": self.version, "project": "auto-chess-2", "unityVersion": "6000.0.0f1",
                "isPlaying": self.playing, "isPaused": self.paused, "isCompiling": self.compiling_polls > 0,
                "isUpdating": False, "playModeChanging": self.pending_play is not None,
                "timeScale": self.time_scale, "frameCount": self.frame, "activeScene": self.active_scene,
                "lastCompileErrors": self.last_compile["errorCount"],
                "adapter": {"present": self.adapter, "available": self.adapter_available, "gameId": "auto-chess"},
                "port": self.port}

    def _finish_compile(self) -> None:
        self.last_compile = {"startedAtUtc": f"2026-01-01T00:{self.compile_count:02d}:00Z",
                             "finishedAtUtc": f"2026-01-01T00:{self.compile_count:02d}:05Z",
                             "errorCount": len(self.compile_errors), "warningCount": 0,
                             "errors": list(self.compile_errors)[:50]}

    def _finish_play(self) -> None:
        on = self.pending_play
        self.pending_play = None
        self.playing = bool(on)
        if on:
            self.new_game()
            self.time_scale = 1.0
            if self.log_reset_on_play:
                self.logs, self.next_log = [], 0
            self.add_log("Log", "Game booted")


def r_ping(g, m, q): return {"serverVersion": g.version}
def r_status(g, m, q): return g.status_json()


def r_compile_status(g, m, q):
    return {"isCompiling": g.compiling_polls > 0, "lastCompile": g.last_compile}


def r_refresh(g, m, q):
    if g.playing:
        if not _truthy(q.get("exitPlay", "0")):
            raise Reject(409, "edit_mode_required", "refresh needs edit mode (exitPlay=1)")
        g.playing = False
    g.compile_count += 1
    g.compiling_polls = 2
    return {"requested": True}


def r_play(g, m, q):
    if "on" not in q:
        raise Reject(400, "bad_request", "missing on")
    on = _truthy(q["on"])
    if q.get("scene"):
        if g.playing:
            raise Reject(409, "edit_mode_required", "scene can only be opened in edit mode")
        g.active_scene = q["scene"]
    changed = on != g.playing
    if changed:
        g.pending_play = on
        g.play_polls = g.transition_polls
    return {"isPlaying": g.playing, "requested": True, "changed": changed}


def r_pause(g, m, q):
    g.paused = _truthy(q.get("on", "1"))
    return {"isPaused": g.paused}


def r_step(g, m, q):
    g.frame += 1
    return {"frameCount": g.frame}


def r_timescale(g, m, q):
    if "value" in q and m == "POST":
        v = float(q["value"])
        if not 0 <= v <= 100:
            raise Reject(400, "bad_request", "value must be 0-100")
        g.time_scale = v
    return {"timeScale": g.time_scale}


def r_logs(g, m, q):
    since = int(q.get("since", 0) or 0)
    level = q.get("level", "all")
    limit = int(q.get("limit", 200) or 200)
    wanted = {"error": ("Error", "Assert", "Exception"),
              "warning": ("Warning", "Error", "Assert", "Exception")}.get(level)
    pool = [e for e in g.logs if e["index"] >= since]
    oldest = g.logs[0]["index"] if g.logs else g.next_log
    dropped = max(0, oldest - since) if since < oldest else 0
    picked, nxt = [], g.next_log
    for e in pool:
        if wanted and e["type"] not in wanted:
            continue
        if len(picked) >= limit:
            nxt = e["index"]
            break
        picked.append(e)
    red = sum(1 for e in picked if e["type"] in ("Error", "Assert", "Exception"))
    return {"nextIndex": nxt, "dropped": dropped, "redCount": red, "entries": picked}


def r_logs_clear(g, m, q):
    g.logs = []
    return {"nextIndex": g.next_log}


def r_screenshot(g, m, q):
    name = q.get("name") or time.strftime("%Y%m%d_%H%M%S")
    path = g.screens_dir / f"{name}.png"
    if path.exists():
        path.unlink()
    threading.Timer(g.screenshot_delay, lambda: path.write_bytes(PNG_1PX)).start()
    return {"path": str(path), "pending": True}


def r_hierarchy(g, m, q):
    els = g.ui_elements()
    with_comp = _truthy(q.get("components", "0"))

    def node(path):
        v = g.ui_view(path)
        kids = [p for p in els if p.startswith(path + "/") and "/" not in p[len(path) + 1:]]
        n = {"name": v["name"], "path": path, "active": v["active"], "activeInHierarchy": v["activeInHierarchy"],
             "children": [node(k) for k in kids]}
        if with_comp:
            n["components"] = v["components"]
        return n
    root = q.get("root") or "Canvas"
    if root not in els:
        raise Reject(404, "not_found", f"no object at {root}")
    return {"nodes": [node(root)]}


def r_ui_components(g, m, q):
    kinds = (q.get("kinds") or "button,toggle,slider,input,dropdown").split(",")
    inc = _truthy(q.get("includeInactive", "0"))
    items = []
    for p in g.ui_elements():
        v = g.ui_view(p)
        if v["kind"] not in kinds:
            continue
        if q.get("root") and not p.startswith(q["root"]):
            continue
        if q.get("match") and q["match"] not in p and q["match"] not in v["caption"]:
            continue
        if not inc and not v["activeInHierarchy"]:
            continue
        items.append({k: v[k] for k in ("kind", "name", "path", "caption", "interactable", "rect", "center", "visible")}
                     | {"active": v["activeInHierarchy"]})
    return {"items": items}


def r_ui_find(g, m, q):
    v = g.ui_lookup(q)
    if v is None:
        return {"found": False}
    return {"found": True, **{k: v[k] for k in ("path", "active", "activeInHierarchy", "interactable", "caption",
                                                 "components", "rect", "center")}}


def r_ui_click(g, m, q):
    if "x" in q and "y" in q and not q.get("path") and not q.get("text"):
        h = g.hit(float(q["x"]), float(q["y"]))
        if h is None:
            return {"executed": False, "targetPath": None, "mode": "none", "message": "nothing under the point"}
        kind, ref = h
        if kind == "target":
            g.target_effect(ref)
            return {"executed": True, "targetPath": f"World/{ref}", "mode": "world", "message": ""}
        g.ui_effect(ref)
        return {"executed": True, "targetPath": ref, "mode": "ui", "message": ""}
    v = g.ui_lookup(q)
    if v is None:
        return {"executed": False, "targetPath": None, "mode": "none", "message": "not found"}
    if not v["activeInHierarchy"]:
        return {"executed": False, "targetPath": v["path"], "mode": "none", "message": "not active"}
    if v["interactable"] is False:
        return {"executed": False, "targetPath": v["path"], "mode": "ui", "message": "not interactable"}
    g.ui_effect(v["path"])
    return {"executed": True, "targetPath": v["path"], "mode": "ui", "message": ""}


def _point_target(g, x, y):
    h = g.hit(float(x), float(y))
    return h[1] if h and h[0] == "target" else None


def r_drag(g, m, q):
    src = q.get("from") or (_point_target(g, q.get("x0"), q.get("y0")) if "x0" in q else None)
    dst = q.get("to") or (_point_target(g, q.get("x1"), q.get("y1")) if "x1" in q else None)
    if src is None:
        return {"started": False, "message": "nothing to drag at the start point"}
    executed = False
    if str(src).startswith("bench:") and dst == "board:0":
        i = int(str(src).split(":")[1])
        if i < len(g.game["items"]):
            g.game["board"].append(g.game["items"].pop(i))
            executed = True
    g.drag_state = {"active": True, "done": False, "polls": 2,
                    "result": {"executed": executed, "dragTarget": f"World/{src}",
                               "dropTarget": f"World/{dst}" if dst else None,
                               "fired": ["pointerDown", "beginDrag", "drag", "pointerUp", "drop", "endDrag"]}}
    return {"started": True, "dragTarget": f"World/{src}"}


def r_drag_status(g, m, q):
    d = g.drag_state
    if d["active"]:
        d["polls"] -= 1
        if d["polls"] <= 0:
            d["active"], d["done"] = False, True
    return {"active": d["active"], "done": d["done"], "result": d["result"] if d["done"] else None}


def r_raycast(g, m, q):
    h = g.hit(float(q["x"]), float(q["y"]))
    return {"eventSystem": [{"path": h[1]}] if h and h[0] == "ui" else [],
            "physics": [{"path": f"World/{h[1]}", "distance": 10.0, "point": [0, 0, 0]}] if h and h[0] == "target" else []}


def r_screen_point(g, m, q):
    for t in g.targets():
        if t["path"] == q.get("path"):
            return {"found": True, "world": [0.0, 0.0, 0.0], "screen": t["center"], "onScreen": True}
    return {"found": False}


REFLECT_TYPES = {
    "AutoChess.GameManager": [{"name": "Gold", "kind": "property", "type": "Int32", "static": False},
                              {"name": "Version", "kind": "field", "type": "String", "static": True},
                              {"name": "AddGold", "kind": "method", "type": "Int32", "static": False},
                              {"name": "Boom", "kind": "method", "type": "Void", "static": False}],
    "AutoChess.Shop": [{"name": "Reroll", "kind": "method", "type": "Void", "static": False}],
}


def _find_type(name):
    for full in REFLECT_TYPES:
        if name in (full, full.rsplit(".", 1)[-1]):
            return full
    raise Reject(404, "not_found", f"type {name} not found")


def r_reflect(g, m, q):
    op = q.get("op")
    if op == "types":
        match = q.get("type") or ""
        return {"types": [{"name": t} for t in REFLECT_TYPES if match in t]}
    t = _find_type(q.get("type") or "")
    if op == "members":
        return {"members": REFLECT_TYPES[t]}
    if op in ("get", "set", "call") and not g.playing:
        raise Reject(409, "play_mode_required", "instance members need Play mode")
    if op == "get":
        member = q.get("member")
        if member == "Gold":
            return {"value": g.game["gold"]}
        if member == "Version":
            return {"value": "1.2.3"}
        raise Reject(404, "not_found", f"member {member} not found")
    if op == "set":
        if q.get("member") != "Gold":
            raise Reject(404, "not_found", f"member {q.get('member')} not found")
        g.game["gold"] = int(json.loads(q["value"]))
        return {"value": g.game["gold"]}
    if op == "call":
        method = q.get("method")
        args = json.loads(q.get("args") or "[]")
        if method == "AddGold":
            g.game["gold"] += int(args[0] if args else 1)
            return {"returned": g.game["gold"], "threw": False}
        if method == "Boom":
            return {"threw": True, "exception": "System.InvalidOperationException: kaboom",
                    "stack": "at AutoChess.GameManager.Boom()"}
        if method == "Reroll":
            return {"returned": None, "threw": False}
        raise Reject(404, "not_found", f"method {method} not found")
    raise Reject(400, "bad_request", f"unknown reflect op {op}")


def r_menu(g, m, q):
    return {"executed": q.get("item") in g.menu_items}


def r_state(g, m, q):
    if g.count_target is not None and g.game["counter"] < g.count_target:
        g.game["counter"] += 1
    if not g.adapter_available:
        return {"gameId": "auto-chess", "available": False}
    state = json.loads(json.dumps(g.game))
    if q.get("path"):
        cur = state
        for part in q["path"].split("."):
            if isinstance(cur, list):
                cur = cur[int(part)]
            elif isinstance(cur, dict) and part in cur:
                cur = cur[part]
            else:
                raise Reject(404, "not_found", f"path {q['path']} not found")
        return {"gameId": "auto-chess", "available": True, "value": cur}
    return {"gameId": "auto-chess", "available": True, "state": state}


OPS = ["list", "add-gold", "next-phase", "boom", "add-item", "log", "count-to"]


def r_game_op(g, m, q):
    op = q.get("op")
    gm = g.game
    if op == "list":
        return {"result": OPS}
    if op == "add-gold":
        try:
            amount = int(q.get("amount", 1))
        except ValueError:
            raise Reject(400, "bad_request", "amount must be an integer") from None
        gm["gold"] += amount
        return {"result": {"gold": gm["gold"]}}
    if op == "next-phase":
        g.next_phase()
        return {"result": {"phase": gm["phase"], "round": gm["round"]}}
    if op == "boom":
        raise Reject(500, "error", "boom: simulated adapter exception", stack="at FakeAdapter.GameOp\nat Bridge")
    if op == "add-item":
        gm["items"].append(q.get("id", "unit"))
        return {"result": {"items": list(gm["items"])}}
    if op == "log":
        idx = g.add_log(q.get("type", "Log"), q.get("message", "hello"))
        return {"result": {"index": idx}}
    if op == "count-to":
        g.count_target = int(q.get("target", 3))
        gm["counter"] = 0
        return {"result": {"counter": 0}}
    raise Reject(400, "bad_request", f"unknown op {op}")


def r_invariants(g, m, q):
    if not g.invariants_supported:
        raise Reject(501, "not_supported", "adapter has no invariants")
    rules = ["gold-non-negative", "bench-cap"]
    only = set(filter(None, (q.get("rules") or "").split(","))) or set(rules)
    findings = []
    if "gold-non-negative" in only and g.game["gold"] < 0:
        findings.append({"rule": "gold-non-negative", "severity": "high", "detail": f"gold is {g.game['gold']}",
                         "data": {"gold": g.game["gold"]}})
    if "bench-cap" in only and len(g.game["items"]) > 8:
        findings.append({"rule": "bench-cap", "severity": "medium", "detail": f"{len(g.game['items'])} units on the bench"})
    return {"rules": [r for r in rules if r in only], "findings": findings,
            "highCount": sum(1 for f in findings if f["severity"] == "high")}


def r_targets(g, m, q):
    ts = g.targets()
    if q.get("kind"):
        ts = [t for t in ts if t["kind"] == q["kind"]]
    return {"targets": ts}


def r_tests_run(g, m, q):
    g.test_counter += 1
    run_id = f"run-{g.test_counter}"
    flt = q.get("filter") or ""
    results = [{"name": "Passes", "fullName": "AutoChess.Tests.Passes", "result": "Passed", "message": "", "durationSec": 0.01},
               {"name": "AlsoPasses", "fullName": "AutoChess.Tests.AlsoPasses", "result": "Passed", "message": "",
                "durationSec": 0.02}]
    if "Fail" in flt:
        results = [{"name": "Fails", "fullName": "AutoChess.Tests.Fails", "result": "Failed",
                    "message": "Expected 3 but was 2", "durationSec": 0.01}] + results
    if flt == "Nothing":
        results = []
    g.tests[run_id] = {"polls": 2, "results": results, "mode": q.get("mode", "EditMode")}
    return {"started": True, "runId": run_id}


def r_tests_status(g, m, q):
    run_id = q.get("runId") or (f"run-{g.test_counter}" if g.test_counter else None)
    t = g.tests.get(run_id)
    if t is None:
        return {"running": False, "finished": False, "total": 0, "passed": 0, "failed": 0, "skipped": 0,
                "durationSec": 0, "results": []}
    if t["polls"] > 0:
        t["polls"] -= 1
        return {"running": True, "finished": False, "total": 0, "passed": 0, "failed": 0, "skipped": 0,
                "durationSec": 0, "results": []}
    res = t["results"]
    failed = sum(1 for r in res if r["result"] == "Failed")
    return {"running": False, "finished": True, "total": len(res), "passed": len(res) - failed, "failed": failed,
            "skipped": 0, "durationSec": 0.03, "results": res}


ROUTES = {
    "/ping": r_ping, "/status": r_status, "/compile-status": r_compile_status, "/refresh": r_refresh,
    "/play": r_play, "/pause": r_pause, "/step": r_step, "/timescale": r_timescale, "/logs": r_logs,
    "/logs/clear": r_logs_clear, "/screenshot": r_screenshot, "/hierarchy": r_hierarchy,
    "/ui/components": r_ui_components, "/ui/find": r_ui_find, "/ui/click": r_ui_click, "/input/drag": r_drag,
    "/input/drag-status": r_drag_status, "/world/raycast": r_raycast, "/world/screen-point": r_screen_point,
    "/reflect": r_reflect, "/menu": r_menu, "/state": r_state, "/game-op": r_game_op,
    "/invariants": r_invariants, "/targets": r_targets, "/tests/run": r_tests_run, "/tests/status": r_tests_status,
}


class _Handler(BaseHTTPRequestHandler):
    game: FakeGame = None  # set per server class

    def log_message(self, fmt, *args):  # keep test output clean
        pass

    def _serve(self, method: str):
        u = urlparse(self.path)
        q = {k: v[-1] for k, v in parse_qs(u.query, keep_blank_values=True).items()}
        body = None
        length = int(self.headers.get("Content-Length") or 0)
        if length:
            raw = self.rfile.read(length)
            try:
                body = json.loads(raw.decode("utf-8"))
            except (UnicodeDecodeError, json.JSONDecodeError):
                body = None
            if isinstance(body, dict):
                for k, v in body.items():
                    q[k] = v if isinstance(v, str) else json.dumps(v) if isinstance(v, (dict, list)) else (
                        ("true" if v else "false") if isinstance(v, bool) else str(v))
        g = self.game
        with g.lock:
            g.requests.append((method, u.path, dict(q), body))
            if g.drop_next > 0:
                g.drop_next -= 1
                self.close_connection = True
                return  # no response at all: the client sees a dropped connection
            try:
                status, payload = 200, {"ok": True, **g.handle(method, u.path, q)}
            except Reject as e:
                status, payload = e.status, {"ok": False, "error": e.message, "kind": e.kind, **e.extra}
            except Exception as e:  # noqa: BLE001 - mirror the bridge's 500
                status, payload = 500, {"ok": False, "error": repr(e), "kind": "error"}
        data = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        self._serve("GET")

    def do_POST(self):
        self._serve("POST")


class StubServer:
    def __init__(self, port: int = 0):
        self.game = FakeGame()
        handler = type("Handler", (_Handler,), {"game": self.game})
        self.httpd = ThreadingHTTPServer(("127.0.0.1", port), handler)
        self.httpd.daemon_threads = True
        self.game.port = self.port
        self.thread: threading.Thread | None = None

    @property
    def port(self) -> int:
        return self.httpd.server_address[1]

    def start(self) -> "StubServer":
        self.thread = threading.Thread(target=self.httpd.serve_forever, daemon=True)
        self.thread.start()
        return self

    def stop(self) -> None:
        self.httpd.shutdown()
        self.httpd.server_close()
        shutil.rmtree(self.game.screens_dir, ignore_errors=True)

    def __enter__(self):
        return self.start()

    def __exit__(self, *exc):
        self.stop()


def main(argv=None) -> int:
    argv = sys.argv[1:] if argv is None else argv
    port = int(argv[0]) if argv else 8766
    srv = StubServer(port).start()
    print(f"stub AutoTest bridge on http://127.0.0.1:{srv.port}/ (Ctrl+C to stop)", flush=True)
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        pass
    srv.stop()
    return 0


if __name__ == "__main__":
    sys.exit(main())
