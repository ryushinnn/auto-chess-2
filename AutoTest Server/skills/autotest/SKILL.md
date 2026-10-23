---
name: autotest
description: "Auto-test the auto-chess-2 Unity game from the editor: the user names what to test (a scenario, a campaign, a feature, the Unity unit tests, a compile check) and this skill picks the right helper in 'AutoTest Server/', runs it, and reports PASS/FAIL with evidence. ALWAYS use for 'auto test', 'autotest', 'run scenario/campaign/smoke', 'unity tests', 'is Unity compiling', or to setup/start the AutoTest rig. Users often write in Vietnamese ('test tự động', 'chạy test', 'chạy scenario', 'chạy kịch bản', 'kịch bản test', 'kiểm tra lỗi compile', 'chụp màn hình game'). Not for writing manual test cases."
argument-hint: "<what to test: scenario name, campaign, feature, 'unity tests', 'compile'>"
---

<!-- Source of truth: AutoTest Server/skills/autotest/SKILL.md. The copy under
     .claude/skills/autotest/ is written by AutoTest Server/install_skill.bat
     (setup.bat calls it). Edit the source, then run the installer again. -->

# /autotest - router for the `AutoTest Server/*.bat` helpers

The user says "auto test ..." (or `/autotest ...`) plus what to test, and expects the
right helper to **run**, not a command to copy. Pick it from section 2, run it with the
section 1 recipe, read the exit code and the report it wrote, answer as in section 4.
Input: `$ARGUMENTS`.

**Language.** The toolkit is English-only (skills, docs, scenario notes, console output).
Only the verdict you write in chat is in the user's language - this user writes
Vietnamese, so the report in section 4 is Vietnamese; keep file names, step names and
log messages verbatim.

**Token budget.** Writing or fixing a scenario is authoring: read
`AutoTest Server/auto_touch/SKILL.md` (it indexes `auto_touch/references/*.md`) and then
only the one reference the job needs.

## 1. Run a helper - the recipe that works on this machine

```bash
cd "<repo root>"   # every helper does cd /d "%~dp0" itself, so any cwd works
PATH="/c/Windows/System32:$PATH" AUTOTEST_NO_PAUSE=1 PYTHONIOENCODING=utf-8 cmd //c "AutoTest Server/run.bat" <args...>; echo "exit=$?"
```

- **Bash tool, not PowerShell** (PowerShell can misreport this folder). Git Bash turns
  `/c` into a path, so cmd needs `//c`. Forward slashes are fine.
- **`AUTOTEST_NO_PAUSE=1`** - every helper ends in `pause`; without it the call hangs.
- **`PYTHONIOENCODING=utf-8`** - avoids `UnicodeEncodeError` on non-ASCII output.
- **`PATH="/c/Windows/System32:$PATH"`** - the Windows tools (`where`, `xcopy`) first.
- **Exit code = verdict.** Capture `$?` on the same line, never behind a pipe. Long
  output: redirect to a scratchpad file and read only what you need.
- **Direct CLI** (for the rows marked "py"): from `AutoTest Server/`,
  `PYTHONIOENCODING=utf-8 py -m auto_touch <command> ...; echo "exit=$?"`.
- **Timeouts**: status/compile seconds; entering Play 5-60 s; a scenario usually under a
  minute (foreground, 600000 ms timeout); a campaign or Unity test run can take minutes -
  use `run_in_background` and then read the newest report.
- **Paths** are relative to `AutoTest Server/`, to `auto_touch/scenarios/`, or absolute.
  `run.bat` with no argument lists what exists; files with a top-level `scenarios` list
  are campaigns (`run.bat` runs them as a campaign too).

## 2. Route the request

| intent | run | notes |
|---|---|---|
| "auto test" with no target | preflight (2.1), then `py -m auto_touch list` and ask which one | a feature/bug description instead of a file = authoring: `auto_touch/SKILL.md`, then come back to run it |
| run a scenario / case / smoke | `run.bat auto_touch/scenarios/<X>.json [--enter-play] [--exit-play] [--timescale N] [--no-stop] [--case k=v] [--verbose]` | match fuzzy names against `list`; several matches -> ask. Matrix: `--list-cases` (no Unity) / `--case` |
| campaign / suite / "test everything" / release gate | `campaign.bat auto_touch/scenarios/<X>_campaign.json [--exit-play]` | one Play session for all; long -> background |
| Unity unit tests (Test Runner) | `unity_tests.bat [--mode EditMode\|PlayMode] [--filter F] [--assembly A] [--exit-play]` | starts from edit mode: add `--exit-play` when Unity is playing. 4 = failures, 3 = nothing matched |
| game logic tests without Unity ("core tests", "test logic", after editing `Assets/_main/Scripts/Core`) | from the repo root: `py Tools/core_tests/run_core_tests.py [--filter Combat]` | offline (Unity's Roslyn + .NET 6, NUnit-compatible shim), ~5 s, the same tests as `unity_tests.bat`. 1 = failures, 2 = compile problem |
| C# compile check without Unity | from the repo root: `py Tools/compile_check/compile_check.py --editor` | builds every project asmdef + Assembly-CSharp(-Editor) with Unity's Roslyn; exit 1 = errors (printed `file(line,col): error CSxxxx`) |
| toolkit self-test (the Python side) | py: `py -m auto_touch.run_tests` | offline, no Unity, ~30 s |
| compile check / "does it compile" / after editing C# | py: `py -m auto_touch refresh --wait` (`--exit-play` if playing) or `compile-status` | 5 = errors: list them `file(line,col): message` |
| setup / install / "server not answering" | `setup.bat` | installs this skill, registers MCP in `.mcp.json`, prints status. 2 = Unity not open or server off |
| start a session / enter Play | `start.bat [--scene Assets/Scenes/X.unity]` | status, then Play on |
| status / is Unity up | `status.bat` or py: `py -m auto_touch --json status` | |
| console logs / errors | py: `py -m auto_touch logs --level error` | `--since N` with the previous `nextIndex` |
| screenshot / "what is on screen" | py: `py -m auto_touch screenshot --name <x>` then Read the PNG it prints | Play only |
| game state / a value | py: `py -m auto_touch state [--path player.gold]` | |
| game op / cheat a state | py: `py -m auto_touch ops`, then `py -m auto_touch op <name> key=value ...` | mutating, never retried |
| click / drag / UI inventory | py: `targets`, `ui`, `find`, `click --target shop:1`, `drag --from-target bench:0 --to-target board:0` | |
| call game code directly | py: `reflect --op types\|members\|get\|set\|call --type X ...` | exit 4 = it threw (a finding) |
| validate a scenario file | py: `py -m auto_touch validate <file>` | offline, exit 3 = problems |

The `autotest_*` MCP tools (server `autotest`) call the same code; use them for
follow-ups (state, screenshot, logs) and authoring. When the user asks to *run*
something, the .bat helper is the entry point.

### 2.1 Preflight - before anything that needs Unity

```bash
cd "<repo>/AutoTest Server" && PYTHONIOENCODING=utf-8 py -m auto_touch --json status; echo "exit=$?"
```

- exit 2 -> Unity is not open, or **Tools > AutoTest > Start Server** is off, or the port
  is wrong (this rig uses **8766**; 8765 is the chirpy-sort rig). Report and stop.
- exit 5 / `lastCompileErrors > 0` -> report the compile errors (`compile-status`)
  instead of running: Unity refuses Play.
- `isPlaying: false` -> fine: scenarios enter Play by themselves (config
  `auto_enter_play`); pass `--enter-play` if that was turned off.
- `adapter.present: false` -> game ops / state / targets / invariants answer 501; only
  game-agnostic scenarios (smoke) can pass.
- offline jobs (validate, run_tests, list, --list-cases) skip the preflight.

## 3. Exit codes

| code | meaning | say |
|---|---|---|
| 0 | PASS / ok | name the report |
| 2 | bridge unreachable | Unity closed / server off / port |
| 3 | invalid input (scenario, campaign, arguments, no test matched) | quote the validator lines |
| 4 | FAIL (assert, step, red log, invariant, Unity test) | failing step + reason from the report |
| 5 | Unity compile errors | the errors, file:line |

A red exit code is the verdict. Do not rerun hoping for green; open the report and name
the failing step.

## 4. Report

Write the prose in the user's language (Vietnamese here); keep helper names, file paths,
step names and log messages verbatim. Shape:

```
[PASS | FAIL | ERROR] <helper> <args> - exit <code> (<what that code means>)
Evidence: auto_touch/out/reports/<file>.md (JSON: auto_touch/out/diagnostics/<file>.json)
<3-6 lines: the failing step (#index, where, action, label) + its message, red logs /
 invariant findings if any; or the totals (pass / warn / known issue / skipped)>
Next: <one concrete action>
```

- Read the diagnostics JSON (`failure`, `steps[]`, `anomalies[]`, `screenshots[]`) to
  name the step - the console tail is not evidence.
- Mention `known_issue` steps (known bugs, not failures) and any `known_issue_resolved`
  anomaly (the bug looks fixed: suggest dropping `knownIssue`).
- Campaign: give "x/y passed" and list the failing rows from
  `out/reports/<stem>_campaign_<ts>.md`. Unity tests: totals + failing test names from
  `out/unity-tests/<ts>.md`.
- Screenshots: give the PNG path from the report (`file:///...` link).

## 5. Hand-offs

- Write or fix a scenario -> `AutoTest Server/auto_touch/SKILL.md` (+ one reference).
- Wire protocol / Unity bridge behaviour -> `AutoTest Server/PROTOCOL.md`.
- Installing on another machine: pull, run `AutoTest Server/setup.bat`, restart the
  Claude Code session. Checklist: `AutoTest Server/setup.md`.

## Game specifics (auto-chess)

- **Adapter** `auto-chess` = `Assets/_main/Scripts/Editor/AutoTest/AutoChessAutoTestAdapter.cs`. Scene
  `Assets/_main/Scenes/Arena.unity`. All gameplay runs in the engine-free `AutoChess.Core` simulation
  (`Assets/_main/Scripts/Core`); the adapter goes through the same `IGameSession` the UI uses.
- **Fresh match without leaving Play**: `op restart seed=<n>` (same seed = same shop / starter rolls).
  Scenarios include `autochess/_fresh_match.json` (restart + pick starter offer 0 -> preparation,
  unit id 1 on `bench:0`, 0 gold, level 1).
- **Phases** (`state.phase`): `Setup` (starter pack) -> `Preparation` -> `Transition` (5 s, boards
  auto-filled, battle built; battle units appear after 2 s) -> `Battle` (<= 60 s) -> `Summary` (2 s)
  -> `Preparation` ... -> `Finished`. Preparation lasts "forever": end it with `op ready`.
- **State paths**: `phase`, `roundLabel` ("1-2"), `tick`, `phaseTimeLeft`, `flags.isGameOver`,
  `player.{gold,level,xp,xpToNext,boardCount,boardMax,unitCount,itemCount,wins,losses,lastOutcome}`,
  `player.units[i].{id,unit,rank,location,items}` (creation order; location `bench:<slot>` /
  `board:<row>,<col>`), `player.bench[slot]` (unit id or null), `player.items[i].{id,count}`,
  `player.shop.{slots,locked,insurance}`, `player.starterOffers[i].{unit,item}`, `player.traits[]`,
  `battle.{finished,outcome,homeAlive,awayAlive,units[i].{id,unit,side,hp,maxHp,energy,cell,alive}}`,
  `views.{roster,battle,battleVisible,boardVisible,ui.{starterPack,arena,shop,inventory,destinies,heroInfo}}`,
  `stateHash` (determinism fingerprint).
- **Ops** (`py -m auto_touch ops`): gameplay `buy slot=`, `refresh`, `lock on=`, `buy-xp`,
  `move unit= to=bench:3|board:1,4`, `equip item= unit=`, `ready`, `choose-starter offer=`,
  `reroll-starter offer=`; cheats `gold amount=`, `xp amount=`, `item item= count=`,
  `unit unit=<HeroId> rank=B|A|S`, `skip-phase`, `set-shop units=Ashe,Zed,-`; tools `restart seed=`,
  `fast-forward seconds=|ticks=`, `speed value=`, `can-move unit= to=`, `unit-screen unit=`,
  `cell-screen at=`, `save-recording`, `missing-scripts`. Gameplay ops reply `{ok, error}`; a refused
  command is `ok:false` (e.g. `NotEnoughGold`, `BoardFull`, `WrongPhase`), not an HTTP error.
  Edit-mode tools are menu items: `menu "AutoChess/Export Game Data"`, `menu "AutoChess/Migrate Scene and Prefabs"`.
- **Targets**: `shop:<0-4>`, `starter:<0-2>`, `item:<itemId>` (item bench, open), `bench:<0-8>`,
  `board:<row>,<col>` (rows 0-3), `unit:<id>` (owned unit view). Real input: `click --target unit:1`
  (info panel), `drag --from-target unit:1 --to-target board:1,3`, `drag --from-target item:_armor --to-target unit:1`.
  HUD paths: `ArenaUIManager/Arena/shop` (toggle shop), `ArenaUIManager/Arena/level` (Buy XP),
  `ArenaUIManager/Shop/Refresh`, `ArenaUIManager/Shop/Rates/Lock`, `ArenaUIManager/StarterPack/confirm`.
- **Invariants**: `gold_non_negative`, `level_bounds`, `board_within_limit`, `roster_slots_consistent`,
  `unit_items_valid`, `item_bench_valid`, `shop_slots_valid`, `roster_views_match_model`,
  `battle_hp_bounds`, `battle_cells_unique`, `battle_views_match_model`, `phase_timer_valid`.
- **Scenarios** (`auto_touch/scenarios/autochess/`): `ac_boot_smoke`, `ac_starter_pack_ui`,
  `ac_shop_buy_merge`, `ac_board_drag`, `ac_item_equip_drag`, `ac_buy_xp_level`, `ac_shop_lock_refresh`,
  `ac_hero_info`, `ac_round_flow`, `ac_battle_win`, `ac_full_stage`; campaign
  `ac_regression_campaign.json` (all of them, ~2-3 min).
- **Release gate** = `py Tools/core_tests/run_core_tests.py` + `unity_tests.bat` +
  `campaign.bat auto_touch/scenarios/autochess/ac_regression_campaign.json`.
