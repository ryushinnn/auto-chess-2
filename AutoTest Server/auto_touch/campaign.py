"""Campaign: run several scenarios (every matrix case of each) as one verdict.

    {
      "note": "What this campaign covers",
      "scenarios": ["smoke_editor.json", {"path": "shop_buy.json", "params": {"gold": 20}, "case": "slot=1"}],
      "stopOnFail": false,      // stop at the first scenario that does not pass
      "freshPlay": false,       // false: one Play session for everything; true: re-enter Play per scenario
      "params": {"seed": 7}     // ${...} defaults handed to every scenario
    }

Paths are relative to the campaign file, then scenarios_dir, then AutoTest Server/.
Writes out/reports/<stem>_campaign_<ts>.md + .csv; exit 0 only if every case passes.
"""
from __future__ import annotations

import time
from dataclasses import replace
from datetime import datetime
from pathlib import Path
from typing import Callable

from . import EXIT_INVALID, EXIT_OK, EXIT_UNREACHABLE, report, schema, worst_exit
from .bridge import Bridge, BridgeError
from .config import Config
from .scenario import VERDICT, RunOptions, Runner, preflight, resolve_scenario


def run_campaign(cfg: Config, ref: str | Path, opts: RunOptions | None = None, bridge: Bridge | None = None,
                 out: Callable[[str], None] | None = None) -> dict:
    opts = opts or RunOptions()
    out = out or (lambda s: print(s, flush=True))
    bridge = bridge or Bridge(cfg)
    search_dirs = schema.default_search_dirs(cfg)
    t0 = time.monotonic()
    started = datetime.now()
    path = resolve_scenario(cfg, ref)
    result = {"path": str(path or ref), "stem": Path(str(ref)).stem, "note": "", "rows": [], "problems": [],
              "startedAt": started.isoformat(timespec="seconds")}

    def finish(code: int) -> dict:
        result["exitCode"] = code
        result["verdict"] = VERDICT.get(code, "FAIL")
        result["durationMs"] = int((time.monotonic() - t0) * 1000)
        if result["rows"] or code == EXIT_OK:
            try:
                md, csv_path = report.write_campaign(cfg, result)
                out(f"campaign report: {report.rel(md)}  ({report.rel(csv_path)})")
            except OSError as e:
                out(f"warning: could not write the campaign report: {e}")
        return result

    if path is None:
        result["problems"] = [f"campaign file not found: {ref}"]
        out(f"INVALID {ref}: campaign file not found")
        return finish(EXIT_INVALID)
    result["stem"] = path.stem
    v = schema.validate_path(path, search_dirs, external=set(opts.params))
    if v.kind != "campaign":
        v.errors.insert(0, f"{path.name} is not a campaign file (it has no 'scenarios' list)")
    if v.errors:
        result["problems"] = v.errors
        out(f"INVALID {path.name}")
        for e in v.errors:
            out(f"  - {e}")
        return finish(EXIT_INVALID)
    doc = schema.load_doc(path)
    result["note"] = doc.get("note", "")
    code, _st, msg = preflight(bridge)
    if code is not None:
        result["problems"] = [msg]
        out(f"{VERDICT.get(code, 'ERROR')} campaign {path.stem}: {msg}")
        return finish(code)

    entries = schema.campaign_entries(doc)
    stop_on_fail = bool(doc.get("stopOnFail", False))
    fresh = bool(doc.get("freshPlay", False))
    base_params = dict(doc.get("params") or {})
    codes: list[int] = []
    out(f"campaign {path.stem}: {len(entries)} scenario file(s), {v.cases} case(s)")
    for i, entry in enumerate(entries):
        target = schema.resolve_ref(entry["ref"], path.parent, search_dirs)
        sub_opts = replace(opts, exit_play=False, case=entry.get("case") or opts.case,
                           params={**base_params, **opts.params, **entry["params"]})
        if fresh and opts.enter_play is None:
            sub_opts = replace(sub_opts, enter_play=True)
        runner = Runner(cfg, bridge, sub_opts, out)
        if fresh and i > 0:
            try:
                if bridge.status().get("isPlaying"):
                    runner.set_play(False)
            except BridgeError as e:
                out(f"warning: could not leave Play mode before {target.name}: {e}")
        res = runner.run_path(target, do_preflight=False)
        codes.append(res["exitCode"])
        if res["cases"]:
            result["rows"].extend(report.campaign_row(rep) for rep in res["cases"])
        else:
            result["rows"].append({"scenario": target.stem, "case": "", "verdict": res["verdict"],
                                   "exitCode": res["exitCode"], "pass": 0, "fail": 0, "warn": 0,
                                   "known_issue": 0, "skipped": 0, "steps": 0, "durationMs": 0,
                                   "failingStep": "", "message": "; ".join(res.get("problems") or [])[:300],
                                   "report": ""})
        if res["exitCode"] == EXIT_UNREACHABLE or (stop_on_fail and res["exitCode"] != EXIT_OK):
            for rest in entries[i + 1:]:
                result["rows"].append({"scenario": Path(str(rest["ref"])).stem, "case": "", "verdict": "NOT RUN",
                                       "exitCode": "", "pass": 0, "fail": 0, "warn": 0, "known_issue": 0,
                                       "skipped": 0, "steps": 0, "durationMs": 0, "failingStep": "",
                                       "message": "not run: " + ("server unreachable" if res["exitCode"] == EXIT_UNREACHABLE
                                                                 else "stopOnFail"), "report": ""})
            break
    if opts.exit_play:
        try:
            if bridge.status().get("isPlaying"):
                Runner(cfg, bridge, opts, out).set_play(False)
        except BridgeError as e:
            out(f"warning: could not exit Play mode: {e}")
    final = worst_exit(codes)
    passed = sum(1 for r in result["rows"] if r["verdict"] == "PASS")
    out(f"{VERDICT.get(final, 'FAIL')} campaign {path.stem}: {passed}/{len(result['rows'])} case(s) passed")
    return finish(final)
