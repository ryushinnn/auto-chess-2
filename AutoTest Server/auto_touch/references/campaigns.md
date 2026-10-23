# Matrix scenarios and campaigns

## 1. Matrix: one file, many cases

```jsonc
"matrix": {"slot": [0, 1, 2], "stars": [1, 2]}          // cartesian product: 6 cases
"matrix": [{"label": "rich", "gold": 99}, {"gold": 0}]  // explicit cases (same keys in each)
"matrixSkip": [{"slot": 2, "stars": 2}]                 // drop combinations
```

* Each case is a full run (setup, actions, teardown) with its own report, named
  `<stem>[slot=0,stars=1]`; files are `<stem>_slot=0_stars=1_<ts>.json`. A list case's
  `label` replaces the generated name.
* Case values become `${slot}` / `${stars}` placeholders (type kept for an exact
  placeholder). A matrix parameter no step uses is a validation warning.
* All cases of a file run in the same Play session, one after the other. State carries
  over from case to case unless your `setup` resets it (a reset op is the usual first step).
* `run <file> --list-cases` prints the cases without touching Unity;
  `run <file> --case slot=1` (all `k=v` must match, compared as text) or
  `--case rich` (substring of the label) runs a subset. Max 256 cases per file.
* The exit code of a matrix run is the worst case's: 2 > 5 > 3 > 4 > 0.

## 2. Campaign: many files, one verdict

A campaign file is recognised by its `scenarios` list (it has no `actions`):

```jsonc
{
  "note": "Release gate: smoke + economy.",            // required
  "scenarios": [
    "smoke_editor.json",                               // path relative to this file,
    "economy/buy_unit.json",                           //   then scenarios_dir, then AutoTest Server/
    {"path": "economy/sell_unit.json", "params": {"stars": 2}, "case": "slot=0"}
  ],
  "stopOnFail": false,   // true: stop at the first scenario that does not pass (rest = NOT RUN)
  "freshPlay": false,    // false: one Play session for everything; true: leave and re-enter Play per scenario
  "params": {"seed": 7}  // ${...} values handed to every scenario (an entry's params win)
}
```

* Preflight once (exit 2 unreachable, 5 compile errors), then every scenario file, every
  matrix case. Play mode is entered automatically when a scenario needs it (config
  `auto_enter_play`, or `--enter-play`); `--exit-play` leaves Play at the end.
* Parameter precedence inside each scenario: its own `params` < campaign `params` < CLI
  `--param` < entry `params` < matrix case < saveAs variables.
* Output: `auto_touch/out/reports/<stem>_campaign_<ts>.md` (verdict table, failures with
  their failing step) and `.csv` (one row per case: scenario, case, verdict, exitCode,
  pass, fail, warn, known_issue, skipped, steps, durationMs, failingStep, message, report),
  plus the usual diagnostics JSON + Markdown per case.
* Exit 0 only when every case passed; otherwise the worst code (2 > 5 > 3 > 4).
* `run` on a campaign file runs it as a campaign. Campaigns do not nest.
* Name campaign files `*_campaign.json` so they are easy to tell apart in `list`.
