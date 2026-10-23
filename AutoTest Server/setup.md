# Setting up the AutoTest rig

Checklist for a new machine or a fresh clone of auto-chess-2. Everything the Python side
writes stays under `AutoTest Server/` (outputs are gitignored); the only files it writes
outside are `.claude/skills/autotest/` and `.mcp.json` at the repo root.

## 1. Requirements

* **Windows + Python 3.10+ with the `py` launcher** (python.org installer, "py launcher"
  ticked). Check: `py -V`. No pip packages: the toolkit is stdlib-only.
* **Unity** with this project open and the bridge compiled (`Assets/AutoTest/Editor`,
  asmdef `AutoTest.Editor`). It starts on load; menu **Tools > AutoTest** has
  Start Server / Stop Server / Autostart Server / Open Output Folder.
* For game scenarios: the game adapter (a class implementing `IAutoTestGameAdapter`, and
  optionally `IAutoTestGameOps`, `IAutoTestInvariants`, `IAutoTestTargets`) in the
  game's editor code. Without it only game-agnostic scenarios (smoke) run.
* Claude Code (optional) for `/autotest` and the MCP tools.

## 2. One-time setup

```
AutoTest Server\setup.bat
```

It checks `py`, runs `install_skill.bat` (copies `skills/autotest/` to
`<repo>/.claude/skills/autotest/`), registers the MCP server in `<repo>/.mcp.json`
(merging with any servers already there), then runs `py -m auto_touch status`.

```jsonc
// <repo>/.mcp.json (the entry setup.bat adds)
{"mcpServers": {"autotest": {"command": "py", "args": ["AutoTest Server/auto_touch/mcp_server.py"]}}}
```

Restart the Claude Code session so it loads the skill and the MCP server. From an agent
or CI, run the helpers with `AUTOTEST_NO_PAUSE=1` (otherwise they wait for a key):

```bash
PATH="/c/Windows/System32:$PATH" AUTOTEST_NO_PAUSE=1 PYTHONIOENCODING=utf-8 cmd //c "AutoTest Server/setup.bat"; echo "exit=$?"
```

Exit 0 = done and the bridge answers; 2 = done but Unity is not reachable yet (open it,
enable the server, run `status.bat`); 5 = Unity has compile errors; 1 = `py` missing;
3 = `.mcp.json` is not valid JSON (fix it by hand - it is never overwritten).

## 3. Verify

```
cd "AutoTest Server"
py -m auto_touch status                 # bridge answers, adapter present?
py -m auto_touch.run_tests              # offline toolkit tests (no Unity)
run.bat auto_touch\scenarios\smoke_editor.json
```

## 4. Ports and other rigs

* This rig: **8766** (Unity `EditorPrefs` int `AutoTest.Port`, Python `port` in
  `auto_touch/autotest.config.json`, env `AUTOTEST_PORT`, CLI `--port`).
* The chirpy-sort rig uses **8765** and can run at the same time in another editor.
* To move the port, change it on both sides.

## 5. Adding game scenarios

1. With Unity in Play mode, discover what the adapter exposes:
   `py -m auto_touch ops`, `state`, `targets`, `ui`, `invariants`.
2. Write `auto_touch/scenarios/<feature>_<behaviour>.json` (format:
   `auto_touch/references/scenarios.md`, workflow: `auto_touch/references/authoring.md`).
3. `py -m auto_touch validate <file>`, then `run.bat <file> --verbose`.
4. Group them in a `*_campaign.json` (see `auto_touch/references/campaigns.md`).
5. Fill the "Game specifics (auto-chess)" section of `skills/autotest/SKILL.md` and run
   `install_skill.bat` again.

## 6. Updating the skill

`skills/autotest/SKILL.md` is the source of truth; `.claude/skills/autotest/` is a
generated copy. Edit the source, run `install_skill.bat`, restart the session.
