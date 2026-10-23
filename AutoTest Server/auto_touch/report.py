"""Reports: diagnostics JSON, Markdown reports, campaign CSV, console lines.

Folders (under config out_dir, default auto_touch/out/):
  diagnostics/<stem>[_<case>]_<ts>.json   full machine-readable run (every step)
  reports/<stem>[_<case>]_<ts>.md         human-readable run report
  reports/<stem>_campaign_<ts>.md|.csv    campaign verdict table
  unity-tests/<ts>.md|.json               Unity Test Runner results
"""
from __future__ import annotations

import csv
import json
from datetime import datetime
from pathlib import Path
from typing import Any

from .config import SERVER_DIR, Config

STATUS_TAG = {"pass": "pass", "fail": "FAIL", "warn": "warn", "known_issue": "known", "skipped": "skip"}


def ts_now() -> str:
    return datetime.now().strftime("%Y%m%d_%H%M%S")


def rel(p: Any) -> str:
    if not p:
        return ""
    path = Path(p)
    try:
        return path.resolve().relative_to(SERVER_DIR).as_posix()
    except ValueError:
        return str(path)


def clip(value: Any, limit: int = 20_000) -> Any:
    """The value itself when its JSON is small enough, else a clipped preview."""
    try:
        text = json.dumps(value, ensure_ascii=False, default=str)
    except (TypeError, ValueError):
        text = repr(value)
    if len(text) <= limit:
        return value
    return {"_clipped": True, "size": len(text), "preview": text[:limit]}


def compact_log(e: dict) -> dict:
    stack = str(e.get("stackTrace") or "").strip().splitlines()
    out = {"index": e.get("index"), "type": e.get("type"), "message": str(e.get("message") or "")[:500]}
    if stack:
        out["stack"] = stack[:4]
    return out


def unique_path(folder: Path, base: str, ext: str) -> Path:
    p = folder / f"{base}{ext}"
    n = 2
    while p.exists():
        p = folder / f"{base}_{n}{ext}"
        n += 1
    return p


def prune(folder: Path, keep: int, pattern: str = "*.json") -> int:
    if keep <= 0 or not folder.is_dir():
        return 0
    files = sorted(folder.glob(pattern), key=lambda f: f.stat().st_mtime, reverse=True)
    removed = 0
    for f in files[keep:]:
        try:
            f.unlink()
            removed += 1
        except OSError:
            pass
    return removed


def _md(text: Any) -> str:
    return str(text if text is not None else "").replace("|", "\\|").replace("\n", " ").strip()


# ---- one case -------------------------------------------------------------------

def step_line(row: dict) -> str:
    tag = STATUS_TAG.get(row.get("status"), row.get("status"))
    label = f" '{row['label']}'" if row.get("label") else ""
    indent = "  " * (1 + int(row.get("depth") or 0))
    msg = f" - {row['message']}" if row.get("message") else ""
    return (f"{indent}[{tag}] #{row.get('index')} {row.get('where')} {row.get('action')}{label}"
            f" ({row.get('durationMs', 0)} ms){msg}")


def console_summary(rep: dict, verbose: bool = False) -> list[str]:
    c = rep.get("counts") or {}
    extras = []
    for key, word in (("warn", "warn"), ("known_issue", "known issue"), ("skipped", "skipped")):
        if c.get(key):
            extras.append(f"{c[key]} {word}")
    secs = (rep.get("durationMs") or 0) / 1000.0
    head = f"{rep['verdict']} {rep['name']} ({c.get('total', 0)} steps{', ' + ', '.join(extras) if extras else ''}, {secs:.1f} s)"
    lines: list[str] = []
    if rep["verdict"] == "PASS":
        lines.append(head + (f" - report: {rel(rep.get('markdownPath'))}" if rep.get("markdownPath") else ""))
    else:
        f = rep.get("failure") or {}
        if f.get("index") is not None:
            lines.append(f"{head}: step #{f['index']} {f.get('where')} {f.get('action')} - {f.get('message')}")
        else:
            lines.append(f"{head}: {f.get('message') or rep.get('abort') or 'failed'}")
        if not verbose and f.get("index") is not None:
            steps = rep.get("steps") or []
            pos = next((i for i, r in enumerate(steps) if r["index"] == f["index"]), None)
            if pos is not None:
                for r in steps[max(0, pos - 2): pos + 1]:
                    lines.append(step_line(r))
    resolved = [a for a in rep.get("anomalies") or [] if a.get("type") == "known_issue_resolved"]
    for a in resolved:
        lines.append(f"  note: step #{a['step']} passed although it is marked knownIssue {a['knownIssue']!r}")
    if rep.get("reportPath") and rep["verdict"] != "PASS":
        lines.append(f"  report: {rel(rep.get('markdownPath'))}  (JSON: {rel(rep['reportPath'])})")
    return lines


def case_markdown(rep: dict) -> str:
    c = rep.get("counts") or {}
    out = [f"# {rep['verdict']} - {rep['name']}", ""]
    note = str(rep.get("note") or "").strip()
    if note:
        out += [f"> {line}" for line in note.splitlines()] + [""]
    out += [
        f"- Scenario: `{rel(rep['scenario'])}`" + (f" - case `{rep['case']}`" if rep.get("case") else ""),
        f"- Started: {rep.get('startedAt')} - duration {(rep.get('durationMs') or 0) / 1000:.1f} s",
        f"- Exit code: {rep.get('exitCode')}",
        f"- Steps: {c.get('total', 0)} total, {c.get('pass', 0)} pass, {c.get('fail', 0)} fail, "
        f"{c.get('warn', 0)} warn, {c.get('known_issue', 0)} known issue, {c.get('skipped', 0)} skipped",
    ]
    server = rep.get("server") or {}
    if server.get("unityVersion"):
        adapter = server.get("adapter") or {}
        out.append(f"- Unity {server.get('unityVersion')}, bridge {server.get('serverVersion')}, "
                   f"game {adapter.get('gameId') or '-'}, scene {server.get('activeScene') or '-'}")
    if rep.get("params"):
        out.append("- Params: " + ", ".join(f"`{k}={json.dumps(v)}`" for k, v in rep["params"].items()))
    f = rep.get("failure")
    if f:
        out += ["", "## Failure", "",
                f"Step #{f.get('index')} `{f.get('where')}` **{f.get('action')}**"
                + (f" ({f.get('label')})" if f.get("label") else "") + f": {f.get('message')}"]
    out += ["", "## Steps", "", "| # | phase | where | action | label | status | ms | message |",
            "|---|---|---|---|---|---|---|---|"]
    for r in rep.get("steps") or []:
        out.append(f"| {r['index']} | {r['phase']} | `{_md(r['where'])}` | {r['action']} | {_md(r.get('label'))} | "
                   f"{STATUS_TAG.get(r['status'], r['status'])} | {r.get('durationMs', 0)} | {_md(r.get('message'))[:300]} |")
    anomalies = rep.get("anomalies") or []
    if anomalies:
        out += ["", "## Anomalies", ""]
        for a in anomalies:
            detail = a.get("message") or ""
            if a.get("entries"):
                detail = "; ".join(f"{e.get('type')}: {e.get('message', '')[:160]}" for e in a["entries"][:3])
            if a.get("findings"):
                detail = "; ".join(f"{x.get('rule')} [{x.get('severity')}]: {x.get('detail')}" for x in a["findings"][:3])
            step = f" (step #{a['step']})" if a.get("step") is not None else ""
            ki = f" `{a['knownIssue']}`" if a.get("knownIssue") else ""
            out.append(f"- **{a.get('type')}**{step}{ki}: {_md(detail)[:400]}")
    shots = rep.get("screenshots") or []
    if shots:
        out += ["", "## Screenshots", ""]
        out += [f"- step #{s['step']}: [{s['name']}]({Path(s['path']).as_uri() if Path(s['path']).is_absolute() else s['path']})"
                for s in shots]
    out += ["", f"Diagnostics: `{rel(rep.get('reportPath'))}`", ""]
    return "\n".join(out)


def write_case(cfg: Config, rep: dict, suffix: str = "") -> tuple[Path, Path]:
    base = f"{rep['stem']}{suffix}_{ts_now()}"
    diag = cfg.out_sub("diagnostics")
    json_path = unique_path(diag, base, ".json")
    md_path = cfg.out_sub("reports") / (json_path.stem + ".md")
    rep["reportPath"], rep["markdownPath"] = str(json_path), str(md_path)
    json_path.write_text(json.dumps(rep, indent=2, ensure_ascii=False, default=str), encoding="utf-8")
    md_path.write_text(case_markdown(rep), encoding="utf-8")
    prune(diag, cfg.keep_diagnostics)
    return json_path, md_path


# ---- campaign ---------------------------------------------------------------------

CSV_COLUMNS = ["scenario", "case", "verdict", "exitCode", "pass", "fail", "warn", "known_issue",
               "skipped", "steps", "durationMs", "failingStep", "message", "report"]


def campaign_row(rep: dict) -> dict:
    c = rep.get("counts") or {}
    f = rep.get("failure") or {}
    failing = ""
    if f.get("index") is not None:
        failing = f"#{f['index']} {f.get('where')} {f.get('action')}"
    return {"scenario": rep.get("stem") or Path(str(rep.get("scenario"))).stem, "case": rep.get("case") or "",
            "verdict": rep.get("verdict"), "exitCode": rep.get("exitCode"),
            "pass": c.get("pass", 0), "fail": c.get("fail", 0), "warn": c.get("warn", 0),
            "known_issue": c.get("known_issue", 0), "skipped": c.get("skipped", 0), "steps": c.get("total", 0),
            "durationMs": rep.get("durationMs", 0), "failingStep": failing,
            "message": (f.get("message") or rep.get("abort") or "")[:300], "report": rel(rep.get("reportPath"))}


def campaign_markdown(camp: dict) -> str:
    rows = camp["rows"]
    passed = sum(1 for r in rows if r["verdict"] == "PASS")
    out = [f"# {camp['verdict']} - campaign {camp['stem']}", ""]
    note = str(camp.get("note") or "").strip()
    if note:
        out += [f"> {line}" for line in note.splitlines()] + [""]
    out += [f"- File: `{rel(camp['path'])}`",
            f"- Started: {camp.get('startedAt')} - duration {(camp.get('durationMs') or 0) / 1000:.1f} s",
            f"- Result: {passed}/{len(rows)} passed - exit code {camp['exitCode']}", "",
            "| scenario | case | verdict | steps | fail | warn | known | ms | failing step | report |",
            "|---|---|---|---|---|---|---|---|---|---|"]
    for r in rows:
        report_link = f"`{r['report']}`" if r.get("report") else ""
        out.append(f"| {_md(r['scenario'])} | {_md(r['case'])} | {r['verdict']} | {r['steps']} | {r['fail']} | "
                   f"{r['warn']} | {r['known_issue']} | {r['durationMs']} | {_md(r['failingStep'])} | {report_link} |")
    bad = [r for r in rows if r["verdict"] != "PASS"]
    if bad:
        out += ["", "## Failures", ""]
        for r in bad:
            name = r["scenario"] + (f"[{r['case']}]" if r["case"] else "")
            out.append(f"- **{name}** {r['verdict']}: {r['failingStep']} {_md(r['message'])}")
    out.append("")
    return "\n".join(out)


def write_campaign(cfg: Config, camp: dict) -> tuple[Path, Path]:
    folder = cfg.out_sub("reports")
    md_path = unique_path(folder, f"{camp['stem']}_campaign_{ts_now()}", ".md")
    csv_path = md_path.with_suffix(".csv")
    camp["reportPath"], camp["csvPath"] = str(md_path), str(csv_path)
    md_path.write_text(campaign_markdown(camp), encoding="utf-8")
    with csv_path.open("w", newline="", encoding="utf-8") as fh:
        w = csv.DictWriter(fh, fieldnames=CSV_COLUMNS)
        w.writeheader()
        for r in camp["rows"]:
            w.writerow({k: r.get(k, "") for k in CSV_COLUMNS})
    return md_path, csv_path


# ---- Unity Test Runner -----------------------------------------------------------

def unity_tests_markdown(st: dict, meta: dict) -> str:
    out = [f"# Unity tests {meta.get('mode')}: {st.get('passed', 0)} passed, {st.get('failed', 0)} failed, "
           f"{st.get('skipped', 0)} skipped", "",
           f"- Run id: `{meta.get('runId')}` - filter `{meta.get('filter') or '-'}` - assembly `{meta.get('assembly') or '-'}`",
           f"- Total {st.get('total', 0)} in {float(st.get('durationSec') or 0):.1f} s", ""]
    results = st.get("results") or []
    failed = [r for r in results if str(r.get("result", "")).lower().startswith("fail")]
    if failed:
        out += ["## Failures", ""]
        for r in failed:
            out.append(f"- **{r.get('fullName') or r.get('name')}**: {_md(r.get('message'))[:600]}")
        out.append("")
    if results:
        out += ["## Results", "", "| test | result | s |", "|---|---|---|"]
        for r in results:
            out.append(f"| {_md(r.get('fullName') or r.get('name'))} | {r.get('result')} | {float(r.get('durationSec') or 0):.2f} |")
        out.append("")
    return "\n".join(out)


def write_unity_tests(cfg: Config, st: dict, meta: dict) -> tuple[Path, Path]:
    folder = cfg.out_sub("unity-tests")
    md_path = unique_path(folder, ts_now(), ".md")
    json_path = md_path.with_suffix(".json")
    md_path.write_text(unity_tests_markdown(st, meta), encoding="utf-8")
    json_path.write_text(json.dumps({"meta": meta, "status": st}, indent=2, ensure_ascii=False, default=str),
                         encoding="utf-8")
    return md_path, json_path
