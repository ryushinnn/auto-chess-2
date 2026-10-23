# Scenario file format

A scenario is one JSON file (JSONC: `//` and `/* */` comments and trailing commas are
allowed) under `auto_touch/scenarios/`. It drives the Unity Editor bridge
(`../../PROTOCOL.md`) step by step and ends PASS or FAIL. Check a file offline with
`py -m auto_touch validate <file>` (exit 3 on problems) before running it.

```jsonc
{
  "note": "Buying a unit costs its price and puts it on the bench.",   // required
  "tags": ["shop", "economy"],
  "requiresPlay": true,                  // default true
  "scene": "Assets/Scenes/Main.unity",   // optional: opened before entering Play
  "timescale": 2,                        // optional: Time.timeScale for the run (restored after)
  "params": {"price": 3},                // optional: defaults for ${...} placeholders
  "matrix": {"slot": [0, 1, 2]},         // optional: one case per combination
  "setup": [ /* steps */ ],
  "actions": [ /* steps - required, non-empty */ ],
  "teardown": [ /* steps - always run, even after a failure */ ]
}
```

## 1. Top-level keys

| key | type | meaning |
|---|---|---|
| `note` | string, **required** | What the scenario proves, in one or two sentences. The first line is what `list` shows. |
| `tags` | list of strings | Free labels (shown in reports). |
| `requiresPlay` | bool, default `true` | The run needs Play mode. When the editor is not playing, the runner enters Play itself if `--enter-play` is given or config `auto_enter_play` is true (default); otherwise the run fails at preflight. `false` = the steps work in either mode (the runner does not touch Play). |
| `scene` | asset path | With `requiresPlay`: if the active scene differs, the runner leaves Play, opens this scene and enters Play again. |
| `timescale` | number 0-100 | Applied after Play is up; CLI `--timescale` wins, config `default_timescale` is the fallback. The value read at the start is restored at the end. |
| `params` | object | Default values for `${name}` placeholders. A file meant to be `include`d declares the parameters it expects here. |
| `setup` | step list | Runs first. A failing setup skips `actions` (they are reported `skipped`). |
| `actions` | step list, **required** | The test itself. |
| `teardown` | step list | Always runs - after a pass, a failure or a skip. Teardown failures fail the run but never stop the teardown. Skipped only when the bridge became unreachable. |
| `matrix` | object of lists, or list of case objects | See `campaigns.md` section 1. |
| `matrixSkip` | list of partial cases | Combinations to drop. |

Keys starting with `_` or `//` are comments and ignored everywhere (top level and steps).

## 2. Common step keys

Every step is `{"action": "<kind>", ...}`. These keys work on every action:

| key | meaning |
|---|---|
| `label` | Short human name, shown in console lines and reports. |
| `optional` | `true`: a failure becomes `warn` and the run continues. Children of an optional `repeat`/`include` inherit it. |
| `knownIssue` | `"BUG-123 short text"`: a failure becomes `known_issue` (reported, does not fail the run). If the step **passes**, the report carries a `known_issue_resolved` anomaly and the console prints a note: the bug looks fixed, drop the key. Inherited by children of `repeat`/`include`. |
| `allowRedLogs` | `true` or a list of regexes: red logs (Error/Assert/Exception) raised during this step that match are accepted instead of failing it. |
| `timeoutMs` | Per-step time budget: how long `wait_state`/`wait_ui`/`click`/`drag`/`play`/`refresh`/`screenshot` may wait; for `op`/`reflect` it raises the HTTP timeout of that one call. |

## 3. Actions

| action | keys | does / passes when |
|---|---|---|
| `play` | `on` (default true), `scene` | Enter/exit Play and wait until the transition is over. Already in that mode = pass. Re-applies the run's timescale after entering. |
| `wait` | `ms` | Sleep. Prefer `wait_state` / `wait_ui` whenever there is something to wait *for*. |
| `timescale` | `value` | Set Time.timeScale (restored at the end of the run). |
| `refresh` | `exitPlay` | AssetDatabase.Refresh + recompile, wait for it. Compile errors fail the step and abort the run with exit 5. |
| `op` | `op`, `params{}`, `expect`, `expectError`, `saveAs` | Call a game op (`POST /game-op`). Fails when the bridge rejects it (adapter threw: message + stack in the report). `expect` = recursive subset match against the op's `result` (section 5). `expectError`: `true`, an error kind (`bad_request`) or a message substring - the op must fail. `saveAs` stores the result as a variable. |
| `state` | `path`, `saveAs` | Read `/state` (or one path of it), store it as a snapshot in the report, optionally save it. Fails when the path does not exist. |
| `assert` | `path` + one operator | Evaluate a path once (section 4). |
| `wait_state` | `path` + one operator, `timeoutMs` (10000), `pollMs` (config poll_ms) | Poll until the operator holds. Unavailable state / 409 / a domain reload are polled through. |
| `click` | one of `path`, `text`, `target`, `x`+`y`; `expectExecuted` (default true) | Click through the EventSystem (`/ui/click`). `target` = an adapter target id from `/targets` (clicked at its `center`). A click that did not execute is retried until `timeoutMs` (default 2000), which absorbs a button that appears a moment later. `expectExecuted: false` asserts the click is blocked (single attempt). |
| `drag` | start one of `from` (path), `fromTarget`, `x0`+`y0`; end one of `to`, `toTarget`, `x1`+`y1`; `steps`, `frames`, `expect`, `expectExecuted` | Multi-frame drag, waits for `/input/drag-status` done. `expect` is a subset match on the result `{executed, dragTarget, dropTarget, fired[]}`. |
| `wait_ui` | one of `path`, `text`, `match`; `state` (default `active`), `timeoutMs` (5000) | Poll `/ui/find` until the object is `exists` (found), `active` (active in hierarchy), `interactable` (active and interactable), `inactive` (absent or not active), `absent` (not found). |
| `assert_ui` | one of `path`, `text`, `match`; `expect{found, active, interactable, caption}` | One `/ui/find`. `caption` is a substring check. Not found fails unless `expect.found` is false. |
| `screenshot` | `name` | Capture the Game view (Play only), wait for the file, list it in the report. Default name `<stem>[_<case>]_<step#>`. |
| `logs` | `level` (all), `failOnRed` (true), `match` | Read the console since the run started. Fails on red logs no earlier step accounted for (minus `red_log_ignore` / `allowRedLogs`), or when `match` (regex) matches no entry. |
| `invariants` | `minSeverity` (high), `rules` | Run `/invariants` now; fails on findings at or above `minSeverity`. Fails when the adapter has no invariants (501). |
| `reflect` | `op` (`types`/`members`/`get`/`set`/`call`), `type`, `member`, `method`, `args`, `value`, `static`, `path`, `expect`, `expectThrows`, `saveAs` | Call game code by reflection. A throwing call fails the step (exception + stack in the report) unless `expectThrows` (`true` or a substring of the exception). The result (`value` for get, `returned` for call, the list for types/members) feeds `expect`, `saveAs`, `@last`. |
| `menu` | `item` | Execute an editor menu item (edit mode). Fails when it did not execute. |
| `repeat` | `times`, `actions[]`, `until{path + operator}`, `untilRequired`, `as` | Run the block up to `times` times. `until` is checked before every iteration and after the last: when it holds the loop stops early (`untilRequired: true` fails the step if it never held). `as: "i"` sets `${i}` to the 0-based iteration. A failing child stops the loop (unless `--no-stop`). |
| `include` | `scenario`, `params{}` | Run another scenario file inline: its `setup`, `actions`, then its `teardown` (always). Path relative to the including file, then `scenarios_dir`. Its `params` are the defaults, the include's `params` override them; variables it saves are visible afterwards. Its `requiresPlay`, `scene`, `timescale` and `matrix` are ignored. |

### Read-only steps

`state`, `assert`, `assert_ui`, `logs`, `invariants`, `screenshot`, `repeat` / `include`
themselves (their children are checked individually) and `reflect` types/members/get.
Every other step is followed by the watchdogs (section 6).

## 4. Paths and operators

A `path` is dotted with list indexes: `phase`, `player.gold`, `player.bench.0.unitId`,
`units[3].hp`, `items.-1` (last element). The root is `/state`'s `state` object unless it
starts with:

* `$name` - a saveAs variable or a parameter: `$buy.gold`, `$units[0]`.
* `@last` - the last result of `op`, `click`, `drag` or `reflect`: `@last.executed`.

Exactly one operator per `assert`, `wait_state` and `until`:

| operator | passes when |
|---|---|
| `equals` / `notEquals` | deep equality (numbers compare by value, `1 != true`, `"1" != 1`) |
| `gt` `gte` `lt` `lte` | number vs number, or string vs string |
| `in` | the value is one of a list (`"in": ["shop", "combat"]`) |
| `contains` | a list contains the element (deep equality), a string contains the substring, an object has the key |
| `exists` | `true`: the path exists (even if null); `false`: it does not |
| `length` | `"length": 3`, or a nested comparison `"length": {"gte": 2}` - lists, strings, objects |
| `approx` | `abs(value - approx) <= tolerance` (`tolerance` default 1e-6) |
| `matches` | regex search in the value (non-strings are matched as JSON text) |

A missing path fails every operator except `exists: false`.

## 5. Templating, variables and `expect`

`${name}` / `${name.path}` / `${name[0].x}` are replaced before a step runs, from (later
wins): file `params` < campaign / `--param` values < matrix case < `saveAs` / `repeat.as`
variables < an include's `params` (inside the included file).

* A string that is **exactly** one placeholder keeps the value's type:
  `"amount": "${n}"` sends the number `5`, `"expect": "${before}"` compares a whole object.
* Inside a longer string the value is spliced in as text: `"id": "unit-${i}"`.
* A placeholder that no matrix parameter, `params`, `saveAs`, `repeat.as` or include
  declares makes the file **invalid** (exit 3, checked before Unity is touched). A
  declared variable that is not set yet when the step runs (the step that saves it failed
  or was optional) fails that step.

There is no arithmetic in placeholders (`${gold} - 3` is just text): compare relatively
(`"lt": "${goldBefore}"`) or let a game op return the computed value and `expect` it.

`expect` (on `op`, `drag`, `reflect`) is a recursive subset match: every key of an expected
object must match (extra keys in the result are fine), lists must have the same length
and match element-wise, scalars compare with `equals`. An object made only of operator
keys is applied as an operator: `"expect": {"gold": {"gte": 3}, "items": {"length": 2}}`.

## 6. Watchdogs and verdict

After every step that is not read-only:

1. **Red-log watchdog** - console entries of type Error / Assert / Exception logged since
   the previous step. Entries matching config `red_log_ignore` or the step's
   `allowRedLogs` are accepted; any other one fails the step when `fail_on_red_logs` is
   true (default), otherwise it is only an anomaly. The log ring restarting after a domain
   reload (Play / recompile) is detected and read from the start.
2. **Invariants watchdog** - while playing and `invariants_every_step` is true: a rule
   that *starts* firing fails the step when its severity is at or above
   `fail_on_invariants` (default `high`; `never` = report only). A rule that keeps firing
   is reported once. An adapter without invariants (501) switches the watchdog off.

Statuses: `pass`, `fail`, `warn` (optional), `known_issue` (knownIssue), `skipped`. The
run is **FAIL (exit 4)** when any step ends `fail`; by default the first `fail` stops the
run (`--no-stop` keeps going) and teardown still runs. Exit 2 = bridge unreachable,
3 = invalid scenario, 5 = compile errors (preflight or a `refresh` step).

## 7. Reports

* `auto_touch/out/diagnostics/<stem>[_<case>]_<YYYYmmdd_HHMMSS>.json` - everything: every
  step `{index, phase, where, action, label, ok, status, message, durationMs, data}`,
  `failure`, `anomalies` (red_logs, invariants, known_issue, known_issue_resolved, ...),
  `screenshots`, `snapshots` (from `state` steps), `finalState`, `vars`, `server`.
  The newest `keep_diagnostics` files are kept.
* `auto_touch/out/reports/<same name>.md` - the human report (steps table, failure,
  anomalies, screenshot links).
* Console: one PASS line, or the failing step plus the two before it (`--verbose` prints
  every step as it runs).

`where` locates a step: `actions[3]`, `actions[2]#1[0]` (repeat iteration 1, child 0),
`actions[0]<buy_unit>.actions[1]` (inside an included file).

## 8. Example

```jsonc
{
  "note": "Buying from shop slot ${slot} costs 3 gold and fills the bench.",
  "matrix": {"slot": [0, 1, 2]},
  "setup": [
    {"action": "op", "op": "add-gold", "params": {"amount": 10}},
    {"action": "state", "path": "gold", "saveAs": "goldBefore"}
  ],
  "actions": [
    {"action": "click", "target": "shop:${slot}", "label": "buy"},
    {"action": "wait_state", "path": "bench", "length": {"gte": 1}, "timeoutMs": 3000},
    {"action": "assert", "path": "gold", "lt": "${goldBefore}", "label": "gold was spent"},
    {"action": "drag", "fromTarget": "bench:0", "toTarget": "board:0,0"},
    {"action": "screenshot"}
  ],
  "teardown": [{"action": "op", "op": "reset-run", "optional": true}]
}
```

(The op names, state paths and target ids above are illustrative: the real ones come from
the game adapter - `py -m auto_touch ops`, `state`, `targets`.)
