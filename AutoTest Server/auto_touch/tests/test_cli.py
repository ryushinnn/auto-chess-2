from __future__ import annotations

import contextlib
import io
import json
import unittest
from pathlib import Path

from auto_touch import EXIT_COMPILE, EXIT_FAIL, EXIT_INVALID, EXIT_OK, EXIT_UNREACHABLE, cli, commands
from auto_touch.tests.helpers import StubTestCase, free_port, write_config_file


class CliCase(StubTestCase):
    def setUp(self):
        super().setUp()
        self.cfg_file = write_config_file(self.tmp / "cfg.json", self.srv.port, self.tmp)

    def cli(self, *argv, port=None) -> tuple[int, str, str]:
        out, err = io.StringIO(), io.StringIO()
        args = ["--config", str(self.cfg_file)] + (["--port", str(port)] if port else []) + list(argv)
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = cli.main(args)
        return code, out.getvalue(), err.getvalue()

    def cli_json(self, *argv) -> tuple[int, dict]:
        code, out, _ = self.cli("--json", *argv)
        return code, json.loads(out)


class TestCliCommands(CliCase):
    def test_status_text_and_json(self):
        code, out, _ = self.cli("status")
        self.assertEqual(code, EXIT_OK)
        self.assertIn("mode: PLAYING", out)
        code, data = self.cli_json("status")
        self.assertTrue(data["isPlaying"])
        code, out, _ = self.cli("status", "--json")  # --json after the command works too
        self.assertTrue(json.loads(out)["ok"])

    def test_status_compile_errors_exit_5(self):
        self.game.last_compile["errorCount"] = 2
        self.assertEqual(self.cli("status")[0], EXIT_COMPILE)
        self.assertEqual(self.cli("compile-status")[0], EXIT_COMPILE)

    def test_unreachable_exit_2(self):
        code, out, err = self.cli("status", port=free_port())
        self.assertEqual(code, EXIT_UNREACHABLE)
        self.assertIn("Tools > AutoTest > Start Server", err)
        code, out, _ = self.cli("--json", "ping", port=free_port())
        self.assertEqual(json.loads(out)["exitCode"], EXIT_UNREACHABLE)

    def test_bad_arguments_exit_3(self):
        self.assertEqual(self.cli("play", "sideways")[0], EXIT_INVALID)
        self.assertEqual(self.cli("nonsense")[0], EXIT_INVALID)
        self.assertEqual(self.cli("op", "add-gold", "notkv")[0], EXIT_INVALID)

    def test_op_state_and_ops(self):
        self.assertEqual(self.cli("op", "add-gold", "amount=5")[0], EXIT_OK)
        self.assertEqual(self.cli("op", "add-item", "--json", '{"id": "knight"}')[0], EXIT_OK)
        code, data = self.cli_json("state", "--path", "gold")
        self.assertEqual(data["value"], 15)
        code, out, _ = self.cli("state", "--path", "items[0]")
        self.assertIn("knight", out)
        self.assertEqual(self.cli("state", "--path", "nope")[0], EXIT_FAIL)
        code, data = self.cli_json("ops")
        self.assertIn("count-to", data["ops"])
        self.assertEqual(self.cli("op", "boom")[0], EXIT_FAIL)
        self.assertEqual(self.cli("op", "add-gold", "amount=abc")[0], EXIT_INVALID)

    def test_play_pause_step_timescale_logs_screenshot(self):
        self.assertEqual(self.cli("play", "off")[0], EXIT_OK)
        self.assertFalse(self.game.playing)
        self.assertEqual(self.cli("play", "on")[0], EXIT_OK)
        self.assertTrue(self.game.playing)
        self.assertEqual(self.cli("pause", "on")[0], EXIT_OK)
        self.assertTrue(self.game.paused)
        self.assertEqual(self.cli("step")[0], EXIT_OK)
        code, data = self.cli_json("timescale", "3")
        self.assertEqual(data["timeScale"], 3.0)
        self.game.add_log("Error", "oops")
        code, out, _ = self.cli("logs", "--level", "error")
        self.assertIn("[Error] oops", out)
        code, data = self.cli_json("screenshot", "--name", "cli_shot")
        self.assertTrue(Path(data["path"]).is_file())
        self.assertEqual(self.cli("logs", "--clear")[0], EXIT_OK)

    def test_ui_find_click_drag_targets(self):
        code, out, _ = self.cli("ui")
        self.assertIn("Canvas/HUD/NextButton", out)
        self.assertEqual(self.cli("find", "--text", "Next")[0], EXIT_OK)
        self.assertEqual(self.cli("find", "--path", "No/Such")[0], EXIT_FAIL)
        self.assertEqual(self.cli("click", "--target", "shop:0")[0], EXIT_OK)
        self.assertEqual(self.cli("click", "--path", "Canvas/HUD/LockedButton")[0], EXIT_FAIL)
        code, data = self.cli_json("targets", "--kind", "bench")
        self.assertEqual([t["id"] for t in data["targets"]], ["bench:0"])
        self.assertEqual(self.cli("drag", "--from-target", "bench:0", "--to-target", "board:0")[0], EXIT_OK)
        self.assertEqual(self.game.game["board"], ["unit0"])
        self.assertEqual(self.cli("hierarchy", "--components")[0], EXIT_OK)
        self.assertEqual(self.cli("raycast", "--x", "100", "--y", "50")[0], EXIT_OK)

    def test_reflect_menu_invariants(self):
        code, data = self.cli_json("reflect", "--op", "get", "--type", "GameManager", "--member", "Gold")
        self.assertEqual(data["value"], 10)
        self.assertEqual(self.cli("reflect", "--op", "call", "--type", "GameManager", "--method", "AddGold",
                                  "--args", "[4]")[0], EXIT_OK)
        self.assertEqual(self.game.game["gold"], 14)
        self.assertEqual(self.cli("reflect", "--op", "call", "--type", "GameManager", "--method", "Boom")[0], EXIT_FAIL)
        self.assertEqual(self.cli("reflect", "--op", "get", "--type", "Nope", "--member", "x")[0], EXIT_INVALID)
        self.assertEqual(self.cli("invariants")[0], EXIT_OK)
        self.cli("op", "add-gold", "amount=-50")
        self.assertEqual(self.cli("invariants")[0], EXIT_FAIL)
        self.assertEqual(self.cli("menu", "Assets/Refresh")[0], EXIT_FAIL)  # 409: menu needs edit mode
        self.cli("play", "off")
        self.assertEqual(self.cli("menu", "Assets/Refresh")[0], EXIT_OK)

    def test_refresh_wait(self):
        self.game.playing = False
        self.assertEqual(self.cli("refresh", "--wait")[0], EXIT_OK)
        self.game.compile_errors = [{"file": "Assets/B.cs", "line": 9, "column": 1, "message": "CS0246: nope"}]
        code, out, _ = self.cli("refresh", "--wait")
        self.assertEqual(code, EXIT_COMPILE)
        self.assertIn("Assets/B.cs(9,1): CS0246", out)

    def test_unity_tests(self):
        code, out, err = self.cli("unity-tests")
        self.assertEqual(code, EXIT_FAIL)
        self.assertIn("--exit-play", err)
        code, out, _ = self.cli("unity-tests", "--exit-play", "--filter", "Fail")
        self.assertEqual(code, EXIT_FAIL)
        self.assertIn("1 failed", out)
        self.assertIn("FAIL AutoChess.Tests.Fails: Expected 3 but was 2", out)
        reports = list((self.tmp / "out" / "unity-tests").glob("*.md"))
        self.assertEqual(len(reports), 1)
        self.assertEqual(self.cli("unity-tests")[0], EXIT_OK)
        self.assertEqual(self.cli("unity-tests", "--filter", "Nothing")[0], EXIT_INVALID)


class TestCliScenarios(CliCase):
    def test_run_list_validate(self):
        self.write("ok.json", {"note": "ok\nsecond line", "actions": [{"action": "op", "op": "add-gold"}]})
        self.write("m.json", {"note": "m", "matrix": {"n": [1, 2]},
                              "actions": [{"action": "op", "op": "add-gold", "params": {"amount": "${n}"}}]})
        self.write("bad.json", {"note": "bad", "actions": [{"action": "nope"}]})
        self.write("all_campaign.json", {"note": "camp", "scenarios": ["ok.json", "m.json"]})
        code, out, _ = self.cli("run", "ok.json")
        self.assertEqual(code, EXIT_OK)
        self.assertTrue(out.startswith("PASS ok"))
        code, data = self.cli_json("run", "m.json", "--case", "n=2")
        self.assertEqual([c["name"] for c in data["cases"]], ["m[n=2]"])
        code, out, _ = self.cli("run", "m.json", "--list-cases", port=free_port())  # offline
        self.assertEqual(code, EXIT_OK)
        self.assertIn("m[n=1]", out)
        self.assertEqual(self.cli("run", "bad.json")[0], EXIT_INVALID)
        self.assertEqual(self.cli("run", "missing.json")[0], EXIT_INVALID)
        code, out, _ = self.cli("run", "all_campaign.json")  # a campaign given to run is run as one
        self.assertEqual(code, EXIT_OK)
        self.assertIn("campaign all_campaign", out)
        code, out, _ = self.cli("campaign", "all_campaign.json", "--exit-play")
        self.assertEqual(code, EXIT_OK)
        self.assertFalse(self.game.playing)
        code, data = self.cli_json("list")
        kinds = {Path(i["path"]).name: (i["kind"], i["note"]) for i in data["items"]}
        self.assertEqual(kinds["ok.json"], ("scenario", "ok"))
        self.assertEqual(kinds["all_campaign.json"][0], "campaign")
        self.assertEqual(self.cli("validate", "ok.json")[0], EXIT_OK)
        code, out, _ = self.cli("validate", "bad.json")
        self.assertEqual(code, EXIT_INVALID)
        self.assertIn("unknown action 'nope'", out)
        self.assertEqual(self.cli("validate", "all_campaign.json")[0], EXIT_OK)

    def test_run_flags(self):
        self.write("needs.json", {"note": "n", "actions": [{"action": "op", "op": "add-gold", "params": {"amount": "${x}"}}]})
        self.assertEqual(self.cli("run", "needs.json")[0], EXIT_INVALID)
        self.assertEqual(self.cli("run", "needs.json", "--param", "x=4", "--timescale", "2", "--exit-play")[0], EXIT_OK)
        self.assertEqual(self.game.game["gold"], 14)
        self.assertFalse(self.game.playing)
        self.assertEqual(self.cli("validate", "needs.json", "--param", "x")[0], EXIT_OK)

    def test_config_command(self):
        code, data = self.cli_json("config")
        self.assertEqual(data["values"]["port"], self.srv.port)
        self.assertEqual(data["sources"]["port"], "file")
        code, data = self.cli_json("--port", "9999", "config")
        self.assertEqual(data["sources"]["port"], "cli")


class TestRegisterMcp(unittest.TestCase):
    def test_merge_keeps_other_servers(self):
        import shutil
        import tempfile
        root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, root, True)
        (root / ".mcp.json").write_text(json.dumps({"mcpServers": {"other": {"command": "x"}}, "keep": 1}), encoding="utf-8")
        r = commands.register_mcp(root)
        self.assertTrue(r["changed"])
        data = json.loads((root / ".mcp.json").read_text(encoding="utf-8"))
        self.assertEqual(data["mcpServers"]["other"], {"command": "x"})
        self.assertEqual(data["keep"], 1)
        self.assertEqual(data["mcpServers"]["autotest"]["command"], "py")
        self.assertTrue(data["mcpServers"]["autotest"]["args"][0].endswith("mcp_server.py"))
        self.assertFalse(commands.register_mcp(root)["changed"])
        (root / ".mcp.json").write_text("{ broken", encoding="utf-8")
        with self.assertRaises(commands.CommandError):
            commands.register_mcp(root)
        self.assertEqual((root / ".mcp.json").read_text(encoding="utf-8"), "{ broken")


if __name__ == "__main__":
    unittest.main()
