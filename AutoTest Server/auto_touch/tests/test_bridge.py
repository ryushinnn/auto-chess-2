from __future__ import annotations

import os
import time
import unittest

from auto_touch.bridge import Bridge, ServerRejected, ServerUnreachable, WaitTimeout, split_params
from auto_touch.tests.helpers import StubTestCase, fast_config, free_port


class TestParams(unittest.TestCase):
    def test_split(self):
        q, body = split_params({"a": 1, "b": True, "c": None, "d": [1, 2], "e": {"x": 1}}, "POST")
        self.assertEqual(q, {"a": "1", "b": "true"})
        self.assertEqual(body, {"d": [1, 2], "e": {"x": 1}})
        q, body = split_params({"d": [1, 2]}, "GET")
        self.assertEqual(q, {"d": "[1,2]"})
        self.assertEqual(body, {})


class TestRetryRules(StubTestCase):
    def setUp(self):
        super().setUp()
        self.cfg.set("server_retries", 3)
        self.b = Bridge(self.cfg)

    def test_get_is_retried_after_dropped_connections(self):
        self.game.drop_next = 2
        st = self.b.status()
        self.assertTrue(st["ok"])
        self.assertEqual(len(self.requests("/status")), 3)

    def test_get_gives_up_with_a_hint(self):
        self.game.drop_next = 10
        with self.assertRaises(ServerUnreachable) as cm:
            self.b.status()
        self.assertIn("Tools > AutoTest > Start Server", str(cm.exception))
        self.assertEqual(len(self.requests("/status")), 4)

    def test_post_is_never_retried(self):
        self.game.drop_next = 1
        with self.assertRaises(ServerUnreachable) as cm:
            self.b.game_op("add-gold", {"amount": 5})
        self.assertIn("not retried", str(cm.exception))
        self.assertEqual(len(self.requests("/game-op")), 1)
        self.assertEqual(self.game.game["gold"], 10)

    def test_get_503_is_retried_post_503_is_not(self):
        self.game.reject_next["/state"] = (503, "timeout", "main thread busy")
        self.assertIn("state", self.b.state())
        self.assertEqual(len(self.requests("/state")), 2)
        self.game.reject_next["/game-op"] = (503, "timeout", "main thread busy")
        with self.assertRaises(ServerRejected) as cm:
            self.b.game_op("add-gold")
        self.assertEqual(cm.exception.status, 503)
        self.assertEqual(len(self.requests("/game-op")), 1)

    def test_other_rejections_are_not_retried(self):
        self.game.reject_next["/status"] = (500, "error", "kaput")
        with self.assertRaises(ServerRejected):
            self.b.status()
        self.assertEqual(len(self.requests("/status")), 1)

    def test_dead_port(self):
        cfg = fast_config(free_port(), self.tmp, server_retries=1)
        t0 = time.monotonic()
        with self.assertRaises(ServerUnreachable):
            Bridge(cfg).status()
        self.assertLess(time.monotonic() - t0, 5)


class TestRoutes(StubTestCase):
    def setUp(self):
        super().setUp()
        self.b = Bridge(self.cfg)

    def test_rejected_fields(self):
        with self.assertRaises(ServerRejected) as cm:
            self.b.game_op("boom")
        e = cm.exception
        self.assertEqual((e.status, e.kind), (500, "error"))
        self.assertIn("boom", e.message)
        self.assertIn("stack", e.payload)
        self.game.playing = False
        with self.assertRaises(ServerRejected) as cm:
            self.b.targets()
        self.assertEqual((cm.exception.status, cm.exception.kind), (409, "play_mode_required"))
        with self.assertRaises(ServerRejected) as cm:
            self.b.get("/nope")
        self.assertEqual(cm.exception.status, 404)

    def test_conventional_methods(self):
        self.b.status()
        self.b.ping()
        self.b.compile_status()
        self.b.timescale()
        self.b.timescale(2)
        self.b.state()
        self.b.targets()
        self.b.invariants("gold-non-negative")
        self.b.ui_find(path="Canvas/HUD/NextButton")
        self.b.ui_components()
        self.b.reflect("get", type="GameManager", member="Gold")
        self.b.reflect("call", type="GameManager", method="AddGold", args=[2])
        self.b.game_op("next-phase")
        self.b.pause(True)
        self.b.step()
        self.b.logs()
        self.b.logs_clear()
        self.b.hierarchy()
        seen = {(m, p) for m, p, _q, _b in self.game.requests}
        for get in ("/status", "/ping", "/compile-status", "/timescale", "/state", "/targets", "/invariants",
                    "/ui/find", "/ui/components", "/reflect", "/logs", "/hierarchy"):
            self.assertIn(("GET", get), seen)
        for post in ("/timescale", "/reflect", "/game-op", "/pause", "/step", "/logs/clear"):
            self.assertIn(("POST", post), seen)
        self.assertEqual(self.game.time_scale, 2.0)
        self.assertEqual(self.game.game["gold"], 12)

    def test_structured_params_travel_in_the_body(self):
        self.b.game_op("add-item", {"id": "knight", "meta": {"stars": 2}, "flag": True})
        method, path, q, body = self.requests("/game-op")[-1]
        self.assertEqual(body, {"meta": {"stars": 2}})
        self.assertEqual(q["id"], "knight")
        self.assertEqual(q["flag"], "true")
        self.assertEqual(self.game.game["items"], ["knight"])
        self.b.reflect("call", type="GameManager", method="AddGold", args=[3])
        _m, _p, q, _b = self.requests("/reflect")[-1]
        self.assertEqual(q["args"], "[3]")

    def test_ops_list(self):
        self.assertIn("add-gold", self.b.ops())

    def test_wait_play_and_screenshot(self):
        self.b.play(False)
        st = self.b.wait_play(False, timeout=3)
        self.assertFalse(st["isPlaying"])
        self.b.play(True)
        self.assertTrue(self.b.wait_play(True, timeout=3)["isPlaying"])
        self.b.timescale(1)
        path = self.b.screenshot("shot")["path"]
        self.assertEqual(self.b.wait_screenshot(path, timeout=3), path)
        self.assertTrue(os.path.getsize(path) > 0)

    def test_wait_compile_reports_errors(self):
        self.game.playing = False
        self.game.compile_errors = [{"assembly": "Assembly-CSharp", "file": "Assets/A.cs", "line": 3, "column": 5,
                                     "message": "CS1002: ; expected"}]
        before = self.b.compile_status()["lastCompile"]["finishedAtUtc"]
        self.b.refresh()
        cs = self.b.wait_compile(before, timeout=3)
        self.assertEqual(cs["lastCompile"]["errorCount"], 1)

    def test_wait_compile_first_compile_of_the_session(self):
        # Before any compile the bridge omits finishedAtUtc (NullValueHandling.Ignore).
        self.game.playing = False
        self.game.last_compile = {"errorCount": 0, "warningCount": 0, "errors": []}
        self.game.compiling_polls = 0
        before = self.b.compile_status()["lastCompile"].get("finishedAtUtc")
        self.assertIsNone(before)
        self.b.refresh()
        self.game.compiling_polls = 0  # compile already over before the first poll
        self.game._finish_compile()
        t0 = time.monotonic()
        cs = self.b.wait_compile(before, timeout=3, start_grace=2)
        self.assertLess(time.monotonic() - t0, 1.0)
        self.assertIn("finishedAtUtc", cs["lastCompile"])

    def test_wait_tests_and_drag(self):
        self.game.playing = False
        run_id = self.b.tests_run("EditMode", "Fail")["runId"]
        st = self.b.wait_tests(run_id, timeout=3)
        self.assertEqual((st["total"], st["failed"]), (3, 1))
        self.game.playing = True
        self.game.game["items"] = ["unit0"]
        self.b.drag(x0=100, y0=150, x1=400, y1=300)
        st = self.b.wait_drag(timeout=3)
        self.assertTrue(st["result"]["executed"])
        self.assertEqual(self.game.game["board"], ["unit0"])

    def test_wait_until_timeout(self):
        with self.assertRaises(WaitTimeout):
            self.b.wait_until(lambda: False, 0.05, poll=0.01, desc="never")


if __name__ == "__main__":
    unittest.main()
