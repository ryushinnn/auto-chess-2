"""Shared fixtures: a stub bridge per test class and a fast Config pointed at it,
with every output folder redirected into a temp dir."""
from __future__ import annotations

import json
import shutil
import socket
import tempfile
import unittest
from pathlib import Path

from auto_touch.config import Config
from auto_touch.scenario import RunOptions, Runner
from auto_touch.tests.stub_server import StubServer


def fast_config(port: int, tmp: Path, **overrides) -> Config:
    cfg = Config()
    values = dict(port=port, poll_ms=10, server_retries=1, server_retry_delay=0.01, http_timeout=5.0,
                  play_timeout_s=5.0, compile_timeout_s=5.0, keep_diagnostics=0,
                  out_dir=str(tmp / "out"), scenarios_dir=str(tmp / "scenarios"))
    values.update(overrides)
    for k, v in values.items():
        cfg.set(k, v, "test")
    return cfg


def write_config_file(path: Path, port: int, tmp: Path, **overrides) -> Path:
    data = dict(port=port, poll_ms=10, server_retries=1, server_retry_delay=0.01, play_timeout_s=5,
                compile_timeout_s=5, keep_diagnostics=0, out_dir=str(tmp / "out"), scenarios_dir=str(tmp / "scenarios"))
    data.update(overrides)
    path.write_text("// test config\n" + json.dumps(data, indent=2), encoding="utf-8")
    return path


def free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


class StubTestCase(unittest.TestCase):
    """One stub server per class; a fresh fake game and temp folders per test."""

    @classmethod
    def setUpClass(cls):
        cls.srv = StubServer().start()

    @classmethod
    def tearDownClass(cls):
        cls.srv.stop()

    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="autotest_test_"))
        (self.tmp / "scenarios").mkdir()
        self.addCleanup(shutil.rmtree, self.tmp, True)
        self.game = self.srv.game
        self.game.reset()
        self.game.requests.clear()
        self.game.drop_next = 0
        self.game.reject_next.clear()
        self.cfg = fast_config(self.srv.port, self.tmp)
        self.lines: list[str] = []

    def write(self, name: str, doc) -> Path:
        p = self.tmp / "scenarios" / name
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(doc if isinstance(doc, str) else json.dumps(doc, indent=2), encoding="utf-8")
        return p

    def run_doc(self, doc, name: str = "t.json", cfg: Config | None = None, **opts) -> dict:
        path = self.write(name, doc)
        runner = Runner(cfg or self.cfg, opts=RunOptions(**opts), out=self.lines.append)
        return runner.run_path(path)

    def case(self, res: dict, i: int = 0) -> dict:
        self.assertTrue(res["cases"], f"no case ran: {res.get('problems')}")
        return res["cases"][i]

    def steps(self, res: dict, i: int = 0) -> list[dict]:
        return self.case(res, i)["steps"]

    def requests(self, path: str, method: str | None = None) -> list[tuple]:
        return [r for r in self.game.requests if r[1] == path and (method is None or r[0] == method)]
