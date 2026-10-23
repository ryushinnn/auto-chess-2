"""Configuration and the JSONC reader shared by config, scenario and campaign files.

Precedence (later wins): built-in defaults < `auto_touch/autotest.config.json`
(JSONC: `//` and `/* */` comments, trailing commas) < environment
(`AUTOTEST_HOST`, `AUTOTEST_PORT`) < CLI flags (`--host`, `--port`).

`AUTOTEST_CONFIG` / `--config` point at another config file (the test suite uses
that to redirect output folders). Relative paths in the file (`scenarios_dir`,
`out_dir`) are relative to the `AutoTest Server/` folder.
"""
from __future__ import annotations

import json
import os
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

PACKAGE_DIR = Path(__file__).resolve().parent
SERVER_DIR = PACKAGE_DIR.parent          # "AutoTest Server/"
REPO_ROOT = SERVER_DIR.parent            # the Unity project root
CONFIG_FILENAME = "autotest.config.json"
DEFAULT_CONFIG_PATH = PACKAGE_DIR / CONFIG_FILENAME

INVARIANT_LEVELS = ("never", "high", "medium", "low")


class ConfigError(ValueError):
    pass


# ---- JSONC -------------------------------------------------------------------

def strip_jsonc(text: str) -> str:
    """Remove `//` and `/* */` comments and trailing commas, string-aware, keeping
    line numbers intact so JSON errors still point at the right line."""
    out: list[str] = []
    i, n, in_str = 0, len(text), False
    while i < n:
        c = text[i]
        if in_str:
            out.append(c)
            if c == "\\" and i + 1 < n:
                out.append(text[i + 1])
                i += 2
                continue
            if c == '"':
                in_str = False
            i += 1
            continue
        if c == '"':
            in_str = True
            out.append(c)
        elif c == "/" and text.startswith("//", i):
            j = text.find("\n", i)
            i = n if j < 0 else j
            continue
        elif c == "/" and text.startswith("/*", i):
            j = text.find("*/", i + 2)
            end = n if j < 0 else j + 2
            out.append("\n" * text.count("\n", i, end))
            i = end
            continue
        else:
            out.append(c)
        i += 1
    return _drop_trailing_commas("".join(out))


def _drop_trailing_commas(s: str) -> str:
    out: list[str] = []
    i, n, in_str = 0, len(s), False
    while i < n:
        c = s[i]
        if in_str:
            out.append(c)
            if c == "\\" and i + 1 < n:
                out.append(s[i + 1])
                i += 2
                continue
            if c == '"':
                in_str = False
        elif c == '"':
            in_str = True
            out.append(c)
        elif c == ",":
            j = i + 1
            while j < n and s[j] in " \t\r\n":
                j += 1
            if not (j < n and s[j] in "]}"):
                out.append(c)
        else:
            out.append(c)
        i += 1
    return "".join(out)


def loads_jsonc(text: str, where: str = "<string>") -> Any:
    if text.startswith("﻿"):
        text = text[1:]
    try:
        return json.loads(strip_jsonc(text))
    except json.JSONDecodeError as e:
        raise ConfigError(f"{where}:{e.lineno}:{e.colno}: invalid JSON: {e.msg}") from None


def load_jsonc_file(path: str | os.PathLike) -> Any:
    p = Path(path)
    try:
        text = p.read_text(encoding="utf-8")
    except OSError as e:
        raise ConfigError(f"cannot read {p}: {e}") from None
    return loads_jsonc(text, str(p))


# ---- config ------------------------------------------------------------------

_TYPES: dict[str, Any] = {
    "host": str, "port": int, "http_timeout": float, "server_retries": int,
    "server_retry_delay": float, "poll_ms": int, "play_timeout_s": float,
    "compile_timeout_s": float, "default_timescale": "optfloat", "red_log_ignore": list,
    "fail_on_red_logs": bool, "invariants_every_step": bool, "fail_on_invariants": str,
    "keep_diagnostics": int, "auto_enter_play": bool, "scenarios_dir": str, "out_dir": str,
}


@dataclass
class Config:
    host: str = "127.0.0.1"
    port: int = 8766
    http_timeout: float = 10.0          # seconds per HTTP request
    server_retries: int = 8             # extra attempts for idempotent GETs (domain reload)
    server_retry_delay: float = 1.0     # seconds between those attempts
    poll_ms: int = 200                  # default poll interval of every wait loop
    play_timeout_s: float = 120.0       # enter/exit Play
    compile_timeout_s: float = 300.0    # refresh + compile, Unity test runs
    default_timescale: float | None = None  # applied to every scenario run (None = leave)
    red_log_ignore: list = field(default_factory=list)  # regexes over red-log messages
    fail_on_red_logs: bool = True
    invariants_every_step: bool = True
    fail_on_invariants: str = "high"    # never | high | medium | low
    keep_diagnostics: int = 50          # newest N diagnostics JSON files kept (0 = all)
    auto_enter_play: bool = True        # enter Play for requiresPlay scenarios on our own
    scenarios_dir: str = "auto_touch/scenarios"
    out_dir: str = "auto_touch/out"

    # provenance (not settable from the file)
    config_path: str | None = None
    unknown_keys: list = field(default_factory=list)
    sources: dict = field(default_factory=dict)

    # -- loading --
    @classmethod
    def load(cls, config_path: str | os.PathLike | None = None, cli: dict | None = None,
             env: dict | None = None) -> "Config":
        env = os.environ if env is None else env
        cfg = cls()
        explicit = config_path or env.get("AUTOTEST_CONFIG")
        path = Path(explicit) if explicit else DEFAULT_CONFIG_PATH
        if path.exists():
            data = load_jsonc_file(path)
            if not isinstance(data, dict):
                raise ConfigError(f"{path}: the config must be a JSON object")
            cfg.config_path = str(path)
            for key, value in data.items():
                if key in _TYPES:
                    cfg.set(key, value, "file")
                else:
                    cfg.unknown_keys.append(key)
        elif explicit:
            raise ConfigError(f"config file not found: {path}")
        if env.get("AUTOTEST_HOST"):
            cfg.set("host", env["AUTOTEST_HOST"], "env")
        if env.get("AUTOTEST_PORT"):
            cfg.set("port", env["AUTOTEST_PORT"], "env")
        for key, value in (cli or {}).items():
            if value is not None:
                cfg.set(key, value, "cli")
        cfg.check()
        return cfg

    def set(self, key: str, value: Any, source: str = "code") -> None:
        kind = _TYPES.get(key)
        if kind is None:
            raise ConfigError(f"unknown config key {key!r}")
        try:
            if kind == "optfloat":
                value = None if value is None or value == "" else float(value)
            elif kind is bool:
                if isinstance(value, str):
                    value = value.strip().lower() in ("1", "true", "yes", "on")
                value = bool(value)
            elif kind is list:
                if isinstance(value, str):
                    value = [value]
                value = [str(v) for v in value]
            else:
                value = kind(value)
        except (TypeError, ValueError):
            raise ConfigError(f"config {key}: cannot use {value!r} ({source})") from None
        setattr(self, key, value)
        self.sources[key] = source

    def check(self) -> None:
        if self.fail_on_invariants not in INVARIANT_LEVELS:
            raise ConfigError(f"fail_on_invariants must be one of {', '.join(INVARIANT_LEVELS)}, "
                              f"got {self.fail_on_invariants!r}")
        if not 0 < self.port < 65536:
            raise ConfigError(f"port out of range: {self.port}")
        for pattern in self.red_log_ignore:
            try:
                re.compile(pattern)
            except re.error as e:
                raise ConfigError(f"red_log_ignore: bad regex {pattern!r}: {e}") from None

    # -- derived --
    @property
    def base_url(self) -> str:
        return f"http://{self.host}:{self.port}"

    def _abs(self, p: str) -> Path:
        path = Path(p)
        return path if path.is_absolute() else (SERVER_DIR / path).resolve()

    @property
    def scenarios_path(self) -> Path:
        return self._abs(self.scenarios_dir)

    @property
    def out_path(self) -> Path:
        return self._abs(self.out_dir)

    def out_sub(self, name: str) -> Path:
        """screens | diagnostics | reports | unity-tests, created on demand."""
        p = self.out_path / name
        p.mkdir(parents=True, exist_ok=True)
        return p

    def to_dict(self) -> dict:
        return {k: getattr(self, k) for k in _TYPES}


DEFAULT_CONFIG_TEXT = """{
  // auto_touch configuration (JSONC: comments and trailing commas are fine).
  // Precedence: built-in defaults < this file < env AUTOTEST_HOST / AUTOTEST_PORT
  // < CLI --host / --port. Effective values: py -m auto_touch config
  // Relative paths below are relative to the "AutoTest Server" folder.

  // ---- Unity Editor bridge (Assets/AutoTest/Editor) ------------------------
  "host": "127.0.0.1",
  "port": 8766,                // 8765 belongs to the chirpy-sort rig
  "http_timeout": 10.0,        // seconds per request
  "server_retries": 8,         // extra tries for read-only GETs while Unity reloads its domain
  "server_retry_delay": 1.0,   // seconds between those tries (POSTs are never retried)

  // ---- waiting -------------------------------------------------------------
  "poll_ms": 200,              // default poll interval of wait_state / wait_ui / play / compile
  "play_timeout_s": 120,       // enter/exit Play mode
  "compile_timeout_s": 300,    // refresh + recompile, Unity Test Runner runs

  // ---- scenario runner -----------------------------------------------------
  "default_timescale": null,   // e.g. 2.0 to run every scenario faster; null = leave Time.timeScale
  "auto_enter_play": true,     // enter Play by ourselves when a scenario needs it
  // Red logs (Error / Assert / Exception) whose message matches one of these
  // regexes are ignored by every watchdog. Keep this list short and explained.
  "red_log_ignore": [
  ],
  "fail_on_red_logs": true,        // a new red log after a step fails that step
  "invariants_every_step": true,   // run the adapter's /invariants after every mutating step
  "fail_on_invariants": "high",    // never | high | medium | low: lowest severity that fails a step
  "keep_diagnostics": 50,          // newest N report JSONs kept in out/diagnostics (0 = keep all)

  // ---- folders ---------------------------------------------------------------
  "scenarios_dir": "auto_touch/scenarios",
  "out_dir": "auto_touch/out"      // screens/ diagnostics/ reports/ unity-tests/
}
"""


def write_default_config(path: Path = DEFAULT_CONFIG_PATH, overwrite: bool = False) -> bool:
    if path.exists() and not overwrite:
        return False
    path.write_text(DEFAULT_CONFIG_TEXT, encoding="utf-8")
    return True
