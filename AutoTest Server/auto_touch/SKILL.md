# auto_touch - scenario authoring reference

Purpose: write, check and fix AutoTest scenarios for auto-chess-2, so that the
`/autotest` router (`../skills/autotest/SKILL.md`, installed as
`.claude/skills/autotest/`) can run them and report PASS/FAIL with evidence.

**Language.** Everything in this toolkit is English: scenario files (`note`, `label`),
docs, console output. Only the human-facing verdict in chat is written in the user's
language (this user writes Vietnamese).

**Token budget.** Read this file, then only the one reference the job needs:

| you need to | read |
|---|---|
| know every top-level key, step action, operator, templating rule, watchdog and report field | `references/scenarios.md` |
| go from "test feature X" to a file that proves it (observe, write, validate, judge) | `references/authoring.md` |
| run variants (`matrix`) or many files as one verdict (campaign) | `references/campaigns.md` |
| look up a CLI command, an MCP tool, an exit code or a config key | `references/cli.md` |
| know what the Unity bridge answers on the wire | `../PROTOCOL.md` |

## Ten-line summary

1. A scenario is JSON(C) in `auto_touch/scenarios/`: `note` (required) + `actions`
   (required) + optional `setup` / `teardown` (always runs) / `matrix` / `params`.
2. Steps are `{"action": ...}`: play, wait, timescale, refresh, op, state, assert,
   wait_state, click, drag, wait_ui, assert_ui, screenshot, logs, invariants, reflect,
   menu, repeat, include.
3. Paths are dotted into `/state` (`player.gold`, `units[0].hp`), `$var...` for saved
   values, `@last...` for the last op/click/drag/reflect result.
4. One operator per check: equals, notEquals, gt, gte, lt, lte, in, contains, exists,
   length, approx (+tolerance), matches.
5. `${name}` placeholders come from params / matrix / saveAs; an exact placeholder keeps
   its type; an undeclared one makes the file invalid (exit 3).
6. Every mutating step is followed by the red-log and invariants watchdogs.
7. `optional` -> warn, `knownIssue` -> known_issue (and "resolved" when it passes),
   `allowRedLogs` -> accept expected errors.
8. First failure stops the run (teardown still runs); `--no-stop` keeps going.
9. Validate offline: `py -m auto_touch validate <file>`.
10. Run: `run.bat <file>` / `py -m auto_touch run <file>`; evidence in
    `auto_touch/out/diagnostics/*.json` and `out/reports/*.md`.

## Game specifics

The ops, state paths and target ids are defined by the game adapter
(`IAutoTestGameAdapter` and friends in the game's editor code, see `../PROTOCOL.md`
section 2). Always discover them live (`ops`, `state`, `targets`, `ui`) before writing
steps; the router skill's "Game specifics (auto-chess)" section lists the stable ones
once the game author has filled it in.
