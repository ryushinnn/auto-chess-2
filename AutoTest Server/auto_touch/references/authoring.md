# Authoring a game scenario - workflow

## 1. Ground it in what the game exposes

Never write steps from assumptions. With Unity in Play mode:

```bash
py -m auto_touch status          # adapter present / available, gameId, scene
py -m auto_touch ops             # game ops the adapter offers (setup shortcuts)
py -m auto_touch state           # the state tree: these are your assert paths
py -m auto_touch targets         # click/drag ids (shop:2, bench:0, board:1,3)
py -m auto_touch ui --match Shop # uGUI buttons with path + caption
py -m auto_touch invariants      # rules the adapter checks after every step
py -m auto_touch screenshot      # look at the screen (open the PNG)
```

What each layer is for:

* **ops** reach a starting state fast and deterministically (give gold, set the round,
  spawn units). Use them in `setup`, not as the thing under test.
* **click / drag** are the player's path - the thing under test for UI flows. Prefer
  `target` ids (stable per state) or exact `path`s over screen coordinates.
* **state** paths are the proof. Assert the state, not the pixels; a screenshot is
  evidence for a human, not a check.
* **invariants** run by themselves after every step - you get "gold never negative"
  for free on every scenario.

## 2. Write the file

* `note` says what is proven, in plain words ("Selling a 2-star unit refunds 3x its
  price"). It is the first thing a reader of the report sees.
* Shape: `setup` (reach the state with ops), `actions` (do it like a player, then
  assert), `teardown` (undo what the next scenario must not inherit).
* Wait for things, do not sleep: `wait_state` / `wait_ui` with a `timeoutMs` instead
  of `wait`. Combat resolution, shop rerolls, animations: wait for the phase/flag.
* Capture "before" values with `state ... saveAs` and compare with `${name}`.
* A known open bug: keep the honest assert and add `knownIssue: "BUG-id text"`. Never
  weaken the assert to go green.
* `optional` only for genuinely non-essential steps (closing a popup that may not be
  there).
* Reuse: put a common flow (e.g. "start a run and reach round 2") in its own file with
  `params` defaults and `include` it.
* Variants: a `matrix` instead of copies of the file.

## 3. Validate, run, judge

```bash
py -m auto_touch validate auto_touch/scenarios/<file>.json     # offline, exit 3 on problems
py -m auto_touch run auto_touch/scenarios/<file>.json --verbose
```

* Exit 0 PASS / 4 FAIL / 3 invalid / 2 unreachable / 5 compile errors.
* On FAIL read `auto_touch/out/diagnostics/<stem>_<ts>.json`: `failure` names the step,
  `anomalies` holds red logs (with stack lines) and invariant findings, `finalState` the
  state at the end. Decide whether the game or the scenario is wrong before changing
  anything; a red exit code is a verdict, not noise to rerun away.
* `known_issue_resolved` in a report means a `knownIssue` step passed: the bug looks
  fixed - remove the key so the assert guards against regressions.

## 4. Where things live

* Scenarios and campaigns: `auto_touch/scenarios/` (subfolders are fine; `list` walks them).
* Naming: `<feature>_<behaviour>.json`, campaigns `<scope>_campaign.json`.
* Format reference: `scenarios.md`; matrix and campaigns: `campaigns.md`; commands:
  `cli.md`; wire protocol: `../../PROTOCOL.md`.
