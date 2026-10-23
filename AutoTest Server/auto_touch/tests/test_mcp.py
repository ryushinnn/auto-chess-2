"""Spawns mcp_server.py as a real subprocess and speaks JSON-RPC over its pipes."""
from __future__ import annotations

import json
import os
import subprocess
import sys
import unittest
from pathlib import Path

from auto_touch import mcp_server
from auto_touch.tests.helpers import StubTestCase, write_config_file

SERVER = Path(__file__).resolve().parent.parent / "mcp_server.py"
EXPECTED_TOOLS = {
    "autotest_status", "autotest_compile_status", "autotest_refresh", "autotest_play", "autotest_logs",
    "autotest_screenshot", "autotest_state", "autotest_op", "autotest_ops", "autotest_invariants",
    "autotest_targets", "autotest_ui", "autotest_find", "autotest_click", "autotest_drag", "autotest_reflect",
    "autotest_menu", "autotest_unity_tests", "autotest_run_scenario", "autotest_run_campaign",
    "autotest_list_scenarios", "autotest_validate_scenario",
}


class TestMcp(StubTestCase):
    def talk(self, frames: list) -> list[dict]:
        cfg_file = write_config_file(self.tmp / "cfg.json", self.srv.port, self.tmp)
        env = {**os.environ, "AUTOTEST_CONFIG": str(cfg_file)}
        env.pop("AUTOTEST_PORT", None)
        proc = subprocess.Popen([sys.executable, str(SERVER)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                stderr=subprocess.PIPE, text=True, encoding="utf-8", env=env,
                                cwd=str(SERVER.parent.parent.parent))
        stdin = "\n".join(f if isinstance(f, str) else json.dumps(f) for f in frames) + "\n"
        out, err = proc.communicate(stdin, timeout=60)
        self.assertEqual(proc.returncode, 0, err)
        return [json.loads(line) for line in out.splitlines() if line.strip()]  # every stdout line is JSON

    def test_handshake_list_and_calls(self):
        self.write("ok.json", {"note": "ok", "actions": [{"action": "op", "op": "add-gold", "params": {"amount": 3}}]})
        replies = self.talk([
            {"jsonrpc": "2.0", "id": 1, "method": "initialize",
             "params": {"protocolVersion": "2025-03-26", "capabilities": {}, "clientInfo": {"name": "t", "version": "0"}}},
            {"jsonrpc": "2.0", "method": "notifications/initialized"},
            {"jsonrpc": "2.0", "id": 2, "method": "tools/list"},
            {"jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": {"name": "autotest_status", "arguments": {}}},
            {"jsonrpc": "2.0", "id": 4, "method": "tools/call",
             "params": {"name": "autotest_op", "arguments": {"op": "add-gold", "params": {"amount": 5}}}},
            {"jsonrpc": "2.0", "id": 5, "method": "tools/call",
             "params": {"name": "autotest_run_scenario", "arguments": {"scenario": str(self.tmp / "scenarios" / "ok.json")}}},
            {"jsonrpc": "2.0", "id": 6, "method": "tools/call", "params": {"name": "nope", "arguments": {}}},
            {"jsonrpc": "2.0", "id": 7, "method": "ping"},
            {"jsonrpc": "2.0", "id": 8, "method": "resources/list"},
            "this is not json",
        ])
        by_id = {r.get("id"): r for r in replies}
        self.assertEqual(by_id[1]["result"]["protocolVersion"], "2025-03-26")
        self.assertEqual(by_id[1]["result"]["serverInfo"]["name"], "autotest")
        tools = by_id[2]["result"]["tools"]
        self.assertEqual({t["name"] for t in tools}, EXPECTED_TOOLS)
        for t in tools:
            self.assertTrue(t["description"])
            self.assertEqual(t["inputSchema"]["type"], "object")
        status = by_id[3]["result"]
        self.assertFalse(status["isError"])
        self.assertTrue(json.loads(status["content"][0]["text"])["isPlaying"])
        self.assertEqual(json.loads(by_id[4]["result"]["content"][0]["text"])["result"]["gold"], 15)
        run = json.loads(by_id[5]["result"]["content"][0]["text"])
        self.assertEqual((run["exitCode"], run["verdict"]), (0, "PASS"))
        self.assertEqual(self.game.game["gold"], 18)
        self.assertTrue(by_id[6]["result"]["isError"])
        self.assertEqual(by_id[7]["result"], {})
        self.assertEqual(by_id[8]["error"]["code"], -32601)
        self.assertEqual(by_id[None]["error"]["code"], -32700)
        self.assertEqual(len(replies), 9)  # the notification got no reply

    def test_protocol_fallback_and_errors_as_content(self):
        self.srv.game.adapter = False
        replies = self.talk([
            {"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "1999-01-01"}},
            {"jsonrpc": "2.0", "id": 2, "method": "tools/call", "params": {"name": "autotest_state", "arguments": {}}},
            {"jsonrpc": "2.0", "id": 3, "method": "tools/call",
             "params": {"name": "autotest_validate_scenario", "arguments": {"scenario": "auto_touch/scenarios/smoke_editor.json"}}},
            {"jsonrpc": "2.0", "id": 4, "method": "tools/call", "params": {"name": "autotest_op", "arguments": {}}},
        ])
        by_id = {r["id"]: r for r in replies}
        self.assertEqual(by_id[1]["result"]["protocolVersion"], mcp_server.PROTOCOL_FALLBACK)
        err = by_id[2]["result"]
        self.assertTrue(err["isError"])
        self.assertEqual(json.loads(err["content"][0]["text"])["status"], 501)
        val = json.loads(by_id[3]["result"]["content"][0]["text"])
        self.assertTrue(val["ok"])
        self.assertTrue(by_id[4]["result"]["isError"])

    def test_tool_table_in_process(self):
        self.assertEqual(set(mcp_server.TOOL_MAP), EXPECTED_TOOLS)
        for proto in ("2024-11-05", "2025-03-26", "2025-06-18"):
            self.assertIn(proto, mcp_server.SUPPORTED_PROTOCOLS)


if __name__ == "__main__":
    unittest.main()
