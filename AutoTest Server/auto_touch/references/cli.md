# CLI, MCP tools and exit codes

Run from `AutoTest Server/`: `py -m auto_touch [--json] [--host H] [--port N] [--config F] <command>`.
`--json` prints one JSON object (it also works after most commands; for `op` put it
before the command, because `op --json '{...}'` carries the op parameters).

## Exit codes (every command, every .bat)

| code | meaning |
|---|---|
| 0 | ok / PASS |
| 2 | bridge unreachable (Unity closed, server off, wrong port) |
| 3 | invalid input: scenario / campaign file, arguments, a 400/404 from the bridge, no test matched |
| 4 | FAIL: assertion, step, red log, invariant, click not executed, call threw, Unity tests failed |
| 5 | Unity compile errors |
| 10 | installed, Unity must compile (reserved for installers) |

## Commands

| command | does |
|---|---|
| `status` | editor/bridge status (mode, scene, timeScale, compile errors, adapter); exit 5 on compile errors |
| `ping` | is the bridge listening |
| `compile-status` | last compile with every error; exit 5 on errors |
| `refresh [--wait] [--exit-play]` | refresh + recompile; with `--wait` print the errors, exit 5 on errors |
| `play on\|off [--scene X] [--no-wait]` | enter / exit Play (waits by default) |
| `pause on\|off`, `step` | pause, advance one frame |
| `timescale [value]` | read or set Time.timeScale |
| `logs [--since N] [--level error\|warning\|all] [--limit N] [--clear]` | console ring buffer |
| `screenshot [--name X] [--super-size N]` | capture the Game view and wait for the file |
| `state [--path P]` | adapter state or one path (exit 4 when the path is missing) |
| `op <op> [key=value ...] [--json '{...}'] [--timeout S]` | run a game op (values parsed as JSON when possible) |
| `ops` | list the game ops |
| `invariants [--rules a,b]` | evaluate invariants; exit 4 at/above `fail_on_invariants` |
| `targets [--kind K]` | adapter targets (id, kind, label, center) |
| `ui [--kinds ..] [--match ..] [--root ..] [--include-inactive]` | uGUI components |
| `find --path P \| --text T \| --match M` | one UI object; exit 4 when not found |
| `click --path P \| --text T \| --target ID \| --x X --y Y` | click; exit 4 when not executed |
| `drag --from P \| --from-target ID \| --x0 --y0  --to P \| --to-target ID \| --x1 --y1 [--steps] [--frames]` | drag; exit 4 when not executed |
| `reflect --op types\|members\|get\|set\|call [--type] [--member] [--method] [--args JSON] [--value JSON] [--static] [--path]` | reflection; exit 4 when it throws |
| `menu <item>` | editor menu item (edit mode) |
| `hierarchy [--root] [--depth] [--components] [--active-only]`, `raycast --x --y`, `screen-point --path` | inspection |
| `unity-tests [--mode EditMode\|PlayMode] [--filter F] [--assembly A] [--exit-play] [--timeout S]` | Unity Test Runner; summary + failures, `out/unity-tests/<ts>.md`; exit 4 on failures, 3 when nothing matched |
| `run <scenario> [--enter-play] [--exit-play] [--timescale N] [--no-stop] [--case k=v,..] [--list-cases] [--verbose] [--param k=v]` | run a scenario (every matrix case) |
| `campaign <file> [same flags]` | run a campaign |
| `list` | scenarios and campaigns under `scenarios_dir` with the first line of their note |
| `validate <file> [--param name]` | offline schema check; exit 3 on problems |
| `config [--init]` | effective configuration and where each value came from |
| `register-mcp` | add the `autotest` server to `<repo>/.mcp.json` (merge) |
| `mcp` | run the MCP stdio server |

Offline test suite of the toolkit itself: `py -m auto_touch.run_tests` (or
`py -m unittest discover -s auto_touch/tests -t .`). Stub bridge for manual tries without
Unity: `py -m auto_touch.tests.stub_server [port]` (default 8766).

## MCP tools (server `autotest`, `auto_touch/mcp_server.py`)

`autotest_status`, `autotest_compile_status`, `autotest_refresh`, `autotest_play`,
`autotest_logs`, `autotest_screenshot`, `autotest_state`, `autotest_op`, `autotest_ops`,
`autotest_invariants`, `autotest_targets`, `autotest_ui`, `autotest_find`,
`autotest_click`, `autotest_drag`, `autotest_reflect`, `autotest_menu`,
`autotest_unity_tests`, `autotest_run_scenario`, `autotest_run_campaign`,
`autotest_list_scenarios`, `autotest_validate_scenario`.

Bridge errors come back as `isError: true` content `{error, message, status?, kind?}`;
a FAIL verdict is a normal result (`exitCode` 4 in the payload). The server reads the
same config file as the CLI on every call, so edits apply without a restart.

## Config keys (`auto_touch/autotest.config.json`)

`host`, `port` (8766), `http_timeout`, `server_retries`, `server_retry_delay`, `poll_ms`,
`play_timeout_s`, `compile_timeout_s`, `default_timescale`, `auto_enter_play`,
`red_log_ignore[]`, `fail_on_red_logs`, `invariants_every_step`, `fail_on_invariants`
(never|high|medium|low), `keep_diagnostics`, `scenarios_dir`, `out_dir`. Precedence:
defaults < file < env `AUTOTEST_HOST` / `AUTOTEST_PORT` < `--host` / `--port`.
`AUTOTEST_CONFIG` / `--config` select another file.
