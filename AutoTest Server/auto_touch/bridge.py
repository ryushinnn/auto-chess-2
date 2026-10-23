"""Typed HTTP client for the Unity Editor bridge (PROTOCOL.md section 3).

Conventions:
* GET for reads, POST for anything that mutates. Scalar parameters travel in the
  query string; structured ones (list / dict) travel in a JSON body on POST and
  JSON-encoded in the query on GET. Python bools become "true"/"false".
* Only idempotent GETs are retried (connection refused / reset / non-JSON reply /
  503 timeout): entering Play or recompiling reloads the editor domain and the
  listener is gone for a few seconds. A POST is never replayed - a click or a
  game op sent twice is a different test.
* Errors: `ServerUnreachable` (nobody answered) and `ServerRejected(status, kind,
  message)` (the bridge answered `ok:false`).
"""
from __future__ import annotations

import http.client
import json
import os
import time
import urllib.error
import urllib.parse
import urllib.request
from typing import Any, Callable

from .config import Config

UNREACHABLE_HINT = ("Is Unity open with this project and the AutoTest server running "
                    "(menu Tools > AutoTest > Start Server)? Port {port} - the chirpy-sort "
                    "rig uses 8765, this one 8766.")

_KIND_BY_STATUS = {400: "bad_request", 404: "not_found", 409: "play_mode_required",
                   501: "not_supported", 503: "timeout", 500: "error"}


class BridgeError(Exception):
    pass


class ServerUnreachable(BridgeError):
    """Nobody answered: Unity closed, server stopped, or a domain reload in progress."""


class ServerRejected(BridgeError):
    """The bridge answered with ok:false (400/404/409/501/503/500)."""

    def __init__(self, status: int, kind: str, message: str, payload: dict | None = None):
        super().__init__(f"HTTP {status} {kind}: {message}")
        self.status = status
        self.kind = kind
        self.message = message
        self.payload = payload or {}


class WaitTimeout(BridgeError):
    """A wait_* helper ran out of time."""


def _encode_scalar(v: Any) -> str:
    if isinstance(v, bool):
        return "true" if v else "false"
    return str(v)


def split_params(params: dict | None, method: str) -> tuple[dict, dict]:
    """(query, body). None values are dropped."""
    query: dict[str, str] = {}
    body: dict[str, Any] = {}
    for k, v in (params or {}).items():
        if v is None:
            continue
        if isinstance(v, (dict, list)):
            if method == "POST":
                body[k] = v
            else:
                query[k] = json.dumps(v, separators=(",", ":"))
        else:
            query[k] = _encode_scalar(v)
    return query, body


def _flag(v: bool | None) -> int | None:
    return None if v is None else (1 if v else 0)


class Bridge:
    def __init__(self, cfg: Config):
        self.cfg = cfg

    # ---- transport -----------------------------------------------------------

    def _hint(self) -> str:
        return UNREACHABLE_HINT.format(port=self.cfg.port)

    def _send(self, method: str, path: str, params: dict | None, timeout: float | None) -> dict:
        query, body = split_params(params, method)
        url = self.cfg.base_url + path
        if query:
            url += "?" + urllib.parse.urlencode(query)
        headers = {"Accept": "application/json"}
        data = None
        if method == "POST":
            data = json.dumps(body).encode("utf-8") if body else b""
            if body:
                headers["Content-Type"] = "application/json; charset=utf-8"
        req = urllib.request.Request(url, data=data, method=method, headers=headers)
        try:
            with urllib.request.urlopen(req, timeout=timeout or self.cfg.http_timeout) as resp:
                raw = resp.read()
        except urllib.error.HTTPError as e:
            try:
                raw = e.read()
            except Exception:
                raw = b""
            finally:
                e.close()
            payload = _parse(raw)
            if isinstance(payload, dict):
                kind = str(payload.get("kind") or _KIND_BY_STATUS.get(e.code, "error"))
                message = str(payload.get("error") or e.reason)
            else:
                kind = _KIND_BY_STATUS.get(e.code, "error")
                message = (raw.decode("utf-8", "replace").strip()[:300] or str(e.reason))
            raise ServerRejected(e.code, kind, message, payload if isinstance(payload, dict) else None) from None
        except (urllib.error.URLError, http.client.HTTPException, ConnectionError, TimeoutError, OSError) as e:
            reason = getattr(e, "reason", None) or e
            raise ServerUnreachable(f"{method} {path}: {reason}") from None
        payload = _parse(raw)
        if not isinstance(payload, dict):
            raise ServerUnreachable(f"{method} {path}: non-JSON reply (server restarting?)")
        if payload.get("ok") is False:
            raise ServerRejected(200, str(payload.get("kind") or "error"),
                                 str(payload.get("error") or "ok:false"), payload)
        return payload

    def get(self, path: str, params: dict | None = None, timeout: float | None = None,
            retries: int | None = None) -> dict:
        """Idempotent read: retried while the editor is unreachable or answers 503."""
        attempts = 1 + max(0, self.cfg.server_retries if retries is None else retries)
        last: BridgeError | None = None
        for i in range(attempts):
            try:
                return self._send("GET", path, params, timeout)
            except ServerUnreachable as e:
                last = e
            except ServerRejected as e:
                if e.status != 503:
                    raise
                last = e
            if i + 1 < attempts:
                time.sleep(self.cfg.server_retry_delay)
        if isinstance(last, ServerRejected):
            raise last
        suffix = f" (after {attempts} attempts)" if attempts > 1 else ""
        raise ServerUnreachable(f"{last}{suffix}. {self._hint()}")

    def post(self, path: str, params: dict | None = None, timeout: float | None = None) -> dict:
        """Mutating call: sent exactly once, never replayed."""
        try:
            return self._send("POST", path, params, timeout)
        except ServerUnreachable as e:
            raise ServerUnreachable(f"{e} (POST is not retried). {self._hint()}") from None

    # ---- routes (PROTOCOL.md section 3) ------------------------------------

    def ping(self, retries: int | None = None) -> dict:
        return self.get("/ping", retries=retries)

    def status(self, retries: int | None = None) -> dict:
        return self.get("/status", retries=retries)

    def compile_status(self, retries: int | None = None) -> dict:
        return self.get("/compile-status", retries=retries)

    def refresh(self, exit_play: bool = False) -> dict:
        return self.post("/refresh", {"exitPlay": _flag(exit_play) if exit_play else None})

    def play(self, on: bool, scene: str | None = None) -> dict:
        return self.post("/play", {"on": _flag(on), "scene": scene or None})

    def pause(self, on: bool) -> dict:
        return self.post("/pause", {"on": _flag(on)})

    def step(self) -> dict:
        return self.post("/step")

    def timescale(self, value: float | None = None) -> float:
        if value is None:
            r = self.get("/timescale")
        else:
            r = self.post("/timescale", {"value": value})
        return float(r.get("timeScale", 0.0))

    def logs(self, since: int | None = None, level: str | None = None, limit: int | None = None,
             retries: int | None = None) -> dict:
        return self.get("/logs", {"since": since, "level": level, "limit": limit}, retries=retries)

    def logs_clear(self) -> dict:
        return self.post("/logs/clear")

    def screenshot(self, name: str | None = None, super_size: int | None = None) -> dict:
        return self.post("/screenshot", {"name": name, "superSize": super_size})

    def hierarchy(self, root: str | None = None, depth: int | None = None,
                  include_inactive: bool | None = None, components: bool | None = None) -> dict:
        return self.get("/hierarchy", {"root": root, "depth": depth,
                                       "includeInactive": _flag(include_inactive),
                                       "components": _flag(components)})

    def ui_components(self, kinds: str | None = None, root: str | None = None,
                      match: str | None = None, include_inactive: bool | None = None) -> dict:
        return self.get("/ui/components", {"kinds": kinds, "root": root, "match": match,
                                           "includeInactive": _flag(include_inactive)})

    def ui_find(self, path: str | None = None, text: str | None = None, match: str | None = None,
                retries: int | None = None) -> dict:
        return self.get("/ui/find", {"path": path, "text": text, "match": match}, retries=retries)

    def ui_click(self, path: str | None = None, text: str | None = None,
                 x: float | None = None, y: float | None = None) -> dict:
        return self.post("/ui/click", {"path": path, "text": text, "x": x, "y": y})

    def drag(self, x0: float | None = None, y0: float | None = None,
             x1: float | None = None, y1: float | None = None,
             from_path: str | None = None, to_path: str | None = None,
             steps: int | None = None, frames: int | None = None) -> dict:
        return self.post("/input/drag", {"x0": x0, "y0": y0, "x1": x1, "y1": y1,
                                         "from": from_path, "to": to_path,
                                         "steps": steps, "frames": frames})

    def drag_status(self, retries: int | None = None) -> dict:
        return self.get("/input/drag-status", retries=retries)

    def raycast(self, x: float, y: float) -> dict:
        return self.get("/world/raycast", {"x": x, "y": y})

    def screen_point(self, path: str) -> dict:
        return self.get("/world/screen-point", {"path": path})

    def reflect(self, op: str, type: str | None = None, member: str | None = None,
                method: str | None = None, args: list | None = None, value: Any = None,
                static: bool | None = None, path: str | None = None,
                timeout: float | None = None, has_value: bool = False) -> dict:
        """op = types | members | get | set | call. `args` / `value` are sent as JSON
        text (the protocol's `args (JSON array)`, `value (JSON)`). get/types/members
        are GETs; set/call mutate and are POSTs."""
        params: dict[str, Any] = {"op": op, "type": type, "member": member, "method": method,
                                  "static": _flag(static) if static else None, "path": path}
        if args is not None:
            params["args"] = json.dumps(args if isinstance(args, list) else [args])
        if has_value or value is not None:
            params["value"] = json.dumps(value)
        if op in ("set", "call"):
            return self.post("/reflect", params, timeout=timeout)
        return self.get("/reflect", params, timeout=timeout)

    def menu(self, item: str) -> dict:
        return self.post("/menu", {"item": item})

    def state(self, path: str | None = None, retries: int | None = None) -> dict:
        return self.get("/state", {"path": path}, retries=retries)

    def game_op(self, op: str, params: dict | None = None, timeout: float | None = None) -> dict:
        merged = dict(params or {})
        merged["op"] = op
        return self.post("/game-op", merged, timeout=timeout)

    def ops(self) -> list:
        r = self.game_op("list")
        res = r.get("result", r.get("ops"))
        if isinstance(res, dict):
            res = res.get("ops", res)
        return list(res) if isinstance(res, (list, tuple)) else []

    def invariants(self, rules: str | list | None = None, retries: int | None = None) -> dict:
        if isinstance(rules, (list, tuple)):
            rules = ",".join(rules)
        return self.get("/invariants", {"rules": rules or None}, retries=retries)

    def targets(self, kind: str | None = None, retries: int | None = None) -> list:
        return list(self.get("/targets", {"kind": kind}, retries=retries).get("targets") or [])

    def tests_run(self, mode: str | None = None, filter: str | None = None,
                  assembly: str | None = None) -> dict:
        return self.post("/tests/run", {"mode": mode, "filter": filter, "assembly": assembly})

    def tests_status(self, run_id: str | None = None, retries: int | None = None) -> dict:
        return self.get("/tests/status", {"runId": run_id}, retries=retries)

    # ---- waiting helpers ---------------------------------------------------------

    def wait_until(self, predicate: Callable[[], Any], timeout: float, poll: float | None = None,
                   desc: str = "condition") -> Any:
        """Call predicate() until it returns something truthy. Unreachable / 503 are
        absorbed (domain reloads); any other ServerRejected propagates."""
        poll = self.cfg.poll_ms / 1000.0 if poll is None else poll
        deadline = time.monotonic() + max(0.0, timeout)
        last_err: BridgeError | None = None
        while True:
            try:
                value = predicate()
                if value:
                    return value
            except ServerUnreachable as e:
                last_err = e
            except ServerRejected as e:
                if e.status != 503:
                    raise
                last_err = e
            if time.monotonic() >= deadline:
                extra = f" (last error: {last_err})" if last_err else ""
                raise WaitTimeout(f"timed out after {timeout:g}s waiting for {desc}{extra}")
            time.sleep(poll)

    def wait_play(self, on: bool, timeout: float | None = None) -> dict:
        """Poll /status until isPlaying == on and the transition is over."""
        def ready():
            s = self.status(retries=0)
            if bool(s.get("isPlaying")) == on and not s.get("playModeChanging"):
                return s
            return None
        return self.wait_until(ready, self.cfg.play_timeout_s if timeout is None else timeout,
                               desc=f"Play mode {'on' if on else 'off'}")

    def wait_compile(self, before_finished: str | None = None, timeout: float | None = None,
                     start_grace: float = 5.0) -> dict:
        """After /refresh: poll /status until isCompiling/isUpdating is over, then
        return /compile-status. Done when a compile was seen (or the server dropped
        out for the domain reload), when lastCompile.finishedAtUtc moved past
        `before_finished`, or after `start_grace` seconds with nothing to compile."""
        t0 = time.monotonic()
        seen = {"busy": False}

        def done():
            try:
                s = self.status(retries=0)
            except ServerUnreachable:
                seen["busy"] = True
                raise
            if s.get("isCompiling") or s.get("isUpdating"):
                seen["busy"] = True
                return None
            cs = self.compile_status(retries=0)
            if cs.get("isCompiling"):
                seen["busy"] = True
                return None
            finished = (cs.get("lastCompile") or {}).get("finishedAtUtc")
            moved = finished is not None and finished != before_finished
            if seen["busy"] or moved or time.monotonic() - t0 >= start_grace:
                return cs
            return None
        return self.wait_until(done, self.cfg.compile_timeout_s if timeout is None else timeout,
                               desc="script compilation")

    def wait_screenshot(self, path: str, timeout: float = 15.0) -> str:
        """The capture is written at end of frame: wait until the file exists and its
        size is stable across two polls."""
        last = {"size": -1}

        def ready():
            try:
                size = os.path.getsize(path)
            except OSError:
                return None
            if size > 0 and size == last["size"]:
                return path
            last["size"] = size
            return None
        return self.wait_until(ready, timeout, poll=min(0.1, self.cfg.poll_ms / 1000.0),
                               desc=f"screenshot file {path}")

    def wait_tests(self, run_id: str | None, timeout: float | None = None) -> dict:
        def finished():
            s = self.tests_status(run_id, retries=0)
            return s if s.get("finished") and not s.get("running") else None
        return self.wait_until(finished, self.cfg.compile_timeout_s if timeout is None else timeout,
                               desc=f"Unity test run {run_id or ''}".strip())

    def wait_drag(self, timeout: float = 15.0) -> dict:
        def done():
            s = self.drag_status(retries=0)
            return s if s.get("done") and not s.get("active") else None
        return self.wait_until(done, timeout, desc="drag gesture")


def _parse(raw: bytes) -> Any:
    try:
        return json.loads(raw.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError):
        return None
