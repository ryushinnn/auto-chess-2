# AutoTest Server - automated tests for auto-chess-2

Python side of the auto-chess-2 AutoTest rig. A small HTTP server inside the Unity Editor
(the *bridge*, `Assets/AutoTest/Editor`, port **8766**) exposes the editor and the game;
this folder drives it from a CLI, a JSON scenario runner, campaign reports, the Unity Test
Runner and an MCP server for Claude Code. Stdlib-only Python 3.10+, no installs.

The wire contract both sides implement is [`PROTOCOL.md`](PROTOCOL.md).

## Architecture

```
 Claude Code ──/autotest skill──> *.bat helpers ─┐
      │                                          ├─> py -m auto_touch (CLI) ──┐
      └──── MCP stdio (autotest_* tools) ────────┘                            │
                                                                              v
   auto_touch/                                                      bridge.py (HTTP client:
     scenario.py  runner: setup -> actions -> teardown,               GET retried during domain
                  watchdogs (red logs, invariants), reports           reloads, POST never retried)
     matrix.py    cases from "matrix"                                         │ http://127.0.0.1:8766
     campaign.py  many scenarios, one verdict (MD + CSV)                      v
     schema.py    offline validation                         ┌──── Unity Editor ─────────────────────┐
     expr.py      paths, operators, ${templating}            │ AutoTest bridge (HttpListener, main-   │
     report.py    diagnostics JSON, Markdown reports         │ thread job queue, log ring, compile    │
     commands.py  shared CLI / MCP operations                │ status, uGUI/EventSystem input, Test   │
     mcp_server.py JSON-RPC 2.0 over stdio                   │ Runner, reflection)                    │
     tests/       unittest suite + stub bridge (no Unity)    │   └─> game adapter (IAutoTestGame*):  │
                                                             │       state, ops, invariants, targets │
                                                             └───────────────────────────────────────┘
```

Outputs (gitignored) go to `auto_touch/out/`: `screens/`, `diagnostics/` (one JSON per
run with every step), `reports/` (Markdown per run, campaign `.md` + `.csv`),
`unity-tests/`.

## Quick start

1. Open the project in Unity; the bridge starts by itself (menu **Tools > AutoTest >
   Start Server** if it does not).
2. Run `setup.bat` once: installs the `/autotest` skill into `.claude/skills/`, registers
   the `autotest` MCP server in the repo's `.mcp.json`, prints the bridge status. Restart
   the Claude Code session afterwards.
3. `start.bat` - status, then enter Play mode.
4. `run.bat auto_touch\scenarios\smoke_editor.json` - the editor smoke test.
5. `campaign.bat auto_touch\scenarios\smoke_campaign.json` - a campaign.
6. `unity_tests.bat --exit-play` - the Unity Test Runner (EditMode by default).

Every `.bat` switches to this folder, passes the exit code through, sets
`PYTHONIOENCODING=utf-8` and ends with `pause` unless `AUTOTEST_NO_PAUSE=1`.

From Claude Code just say what to test ("/autotest chạy smoke test", "auto test the shop
scenario"); the skill picks the helper, runs it and reports.

## CLI

Run from this folder: `py -m auto_touch [--json] [--host H] [--port N] <command>`.

| group | commands |
|---|---|
| editor | `status`, `ping`, `compile-status`, `refresh [--wait]`, `play on\|off [--scene]`, `pause on\|off`, `step`, `timescale [v]`, `logs`, `screenshot`, `menu <item>`, `hierarchy`, `unity-tests` |
| game | `state [--path]`, `op <op> k=v ...`, `ops`, `invariants`, `targets`, `ui`, `find`, `click`, `drag`, `reflect`, `raycast`, `screen-point` |
| scenarios | `run <file>`, `campaign <file>`, `list`, `validate <file>` |
| plumbing | `config`, `register-mcp`, `mcp` |

Exit codes: 0 ok/PASS, 2 bridge unreachable, 3 invalid input, 4 FAIL, 5 Unity compile
errors. Full reference: [`auto_touch/references/cli.md`](auto_touch/references/cli.md).

## Scenarios

JSON files in `auto_touch/scenarios/`:

```jsonc
{
  "note": "Clicking Next in the shop phase starts combat.",
  "actions": [
    {"action": "assert", "path": "phase", "equals": "shop"},
    {"action": "click", "text": "Next"},
    {"action": "wait_state", "path": "phase", "equals": "combat", "timeoutMs": 5000},
    {"action": "screenshot"}
  ]
}
```

Actions: play, wait, timescale, refresh, op, state, assert, wait_state, click, drag,
wait_ui, assert_ui, screenshot, logs, invariants, reflect, menu, repeat, include. After
every mutating step the runner checks the console for new red logs and the adapter's
invariants. Format: [`auto_touch/references/scenarios.md`](auto_touch/references/scenarios.md);
matrix and campaigns: [`auto_touch/references/campaigns.md`](auto_touch/references/campaigns.md);
authoring workflow: [`auto_touch/references/authoring.md`](auto_touch/references/authoring.md).

Game-specific scenarios need the game adapter (Unity side) to expose state, ops,
targets and invariants; discover them with `ops`, `state`, `targets`, `ui`.

## Configuration

`auto_touch/autotest.config.json` (JSONC, documented inline). Precedence: defaults <
file < env `AUTOTEST_HOST` / `AUTOTEST_PORT` < CLI `--host` / `--port`;
`py -m auto_touch config` shows the effective values and their source.

## Tests (no Unity needed)

```
py -m auto_touch.run_tests
py -m unittest discover -s auto_touch/tests -t .
py -m auto_touch.tests.stub_server 8766      # fake bridge with a tiny fake game, for manual tries
```

## Troubleshooting

| symptom | cause / fix |
|---|---|
| exit 2, "server unreachable" | Unity is not open, the project is still importing, or the bridge is off: **Tools > AutoTest > Start Server** (autostart toggle in the same menu). Check the port: this rig is **8766**; **8765** is the chirpy-sort rig, which may run at the same time - never point one at the other. |
| exit 2 only right after Play / recompiling | the domain reload restarts the bridge for a few seconds; GETs retry `server_retries` times (`server_retry_delay` apart). Raise them for a slow machine. |
| exit 5, compile errors | `py -m auto_touch compile-status` lists `file(line,col): message`; fix, then `py -m auto_touch refresh --wait`. Unity refuses Play while scripts do not compile. |
| port clash (another app on 8766) | change `EditorPrefs` int `AutoTest.Port` in Unity and `port` in the config (or `AUTOTEST_PORT`). |
| "requires Play mode" failure | `auto_enter_play` is false in the config: pass `--enter-play` or enable it. |
| 501 `not_supported` | the game adapter is missing or does not implement that interface (ops / invariants / targets). Smoke scenarios still work. |
| 409 `play_mode_required` / `edit_mode_required` | the route needs the other mode: `play on` / `play off`, or `--exit-play` for refresh / unity-tests. |
| `UnicodeEncodeError` in a console | set `PYTHONIOENCODING=utf-8` (the .bat helpers do). |
| a `.bat` hangs from an agent | `AUTOTEST_NO_PAUSE=1` was not set. |
