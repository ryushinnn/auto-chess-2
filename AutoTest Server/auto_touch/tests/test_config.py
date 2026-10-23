from __future__ import annotations

import shutil
import tempfile
import unittest
from pathlib import Path

from auto_touch.config import (DEFAULT_CONFIG_PATH, SERVER_DIR, Config, ConfigError, load_jsonc_file,
                               loads_jsonc, strip_jsonc)


class TestJsonc(unittest.TestCase):
    def test_comments_trailing_commas_and_strings(self):
        text = """{
          // line comment
          "url": "http://example.com/a//b",  // the // inside the string survives
          /* block
             comment */ "list": [1, 2, 3,],
          "quote": "a \\" // not a comment",
        }"""
        data = loads_jsonc(text)
        self.assertEqual(data["url"], "http://example.com/a//b")
        self.assertEqual(data["list"], [1, 2, 3])
        self.assertEqual(data["quote"], 'a " // not a comment')

    def test_error_keeps_line_numbers(self):
        with self.assertRaises(ConfigError) as cm:
            loads_jsonc('{\n  // c\n  /* x\n  */\n  "a": oops\n}', "f.json")
        self.assertIn("f.json:5:", str(cm.exception))

    def test_strip_is_noop_without_comments(self):
        self.assertEqual(strip_jsonc('{"a": [1, 2]}'), '{"a": [1, 2]}')


class TestPrecedence(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp, True)
        self.file = self.tmp / "c.json"
        self.file.write_text('{\n // test\n "port": 9001, "host": "10.0.0.1", "poll_ms": 50,\n'
                             ' "fail_on_invariants": "medium", "mystery": 1,\n}', encoding="utf-8")

    def test_defaults(self):
        cfg = Config()
        self.assertEqual(cfg.port, 8766)
        self.assertEqual(cfg.host, "127.0.0.1")
        self.assertTrue(cfg.fail_on_red_logs)
        self.assertEqual(cfg.fail_on_invariants, "high")

    def test_file_over_defaults(self):
        cfg = Config.load(self.file, env={})
        self.assertEqual((cfg.port, cfg.host, cfg.poll_ms, cfg.fail_on_invariants), (9001, "10.0.0.1", 50, "medium"))
        self.assertEqual(cfg.unknown_keys, ["mystery"])
        self.assertEqual(cfg.sources["port"], "file")
        self.assertEqual(cfg.sources.get("http_timeout"), None)

    def test_env_over_file_and_cli_over_env(self):
        env = {"AUTOTEST_PORT": "9002", "AUTOTEST_HOST": "envhost"}
        cfg = Config.load(self.file, env=env)
        self.assertEqual((cfg.port, cfg.host), (9002, "envhost"))
        self.assertEqual(cfg.sources["port"], "env")
        cfg = Config.load(self.file, cli={"port": 9003, "host": None}, env=env)
        self.assertEqual((cfg.port, cfg.host), (9003, "envhost"))
        self.assertEqual(cfg.sources["port"], "cli")

    def test_autotest_config_env_selects_file(self):
        cfg = Config.load(None, env={"AUTOTEST_CONFIG": str(self.file)})
        self.assertEqual(cfg.port, 9001)
        self.assertEqual(cfg.config_path, str(self.file))

    def test_explicit_missing_file_is_an_error(self):
        with self.assertRaises(ConfigError):
            Config.load(self.tmp / "nope.json", env={})

    def test_bad_values(self):
        self.file.write_text('{"fail_on_invariants": "sometimes"}', encoding="utf-8")
        with self.assertRaises(ConfigError):
            Config.load(self.file, env={})
        self.file.write_text('{"port": "abc"}', encoding="utf-8")
        with self.assertRaises(ConfigError):
            Config.load(self.file, env={})
        self.file.write_text('{"red_log_ignore": ["(unclosed"]}', encoding="utf-8")
        with self.assertRaises(ConfigError):
            Config.load(self.file, env={})

    def test_paths_and_coercion(self):
        cfg = Config()
        self.assertEqual(cfg.scenarios_path, (SERVER_DIR / "auto_touch" / "scenarios").resolve())
        cfg.set("out_dir", str(self.tmp / "o"))
        self.assertEqual(cfg.out_path, self.tmp / "o")
        self.assertTrue(cfg.out_sub("screens").is_dir())
        cfg.set("fail_on_red_logs", "false")
        self.assertFalse(cfg.fail_on_red_logs)
        cfg.set("default_timescale", None)
        self.assertIsNone(cfg.default_timescale)
        cfg.set("red_log_ignore", "^trace")
        self.assertEqual(cfg.red_log_ignore, ["^trace"])
        self.assertEqual(cfg.base_url, "http://127.0.0.1:8766")

    def test_shipped_config_matches_defaults(self):
        data = load_jsonc_file(DEFAULT_CONFIG_PATH)
        defaults = Config().to_dict()
        self.assertEqual(set(data), set(defaults), "shipped config must list every key")
        cfg = Config.load(DEFAULT_CONFIG_PATH, env={})
        self.assertEqual(cfg.to_dict(), defaults)


if __name__ == "__main__":
    unittest.main()
