"""
balance.py - unit / synergy balance from a design formula, calibrated by battle simulation.

  py Tools/balance/balance.py report                 # simulate the current GameData/game_data.json
  py Tools/balance/balance.py report --candidate     # simulate the config applied to the current data
  py Tools/balance/balance.py calibrate [--iterations 10]
                                                     # adjust the per-unit knobs (and tier scales / speed)
                                                     # until units of a tier are even, save the config
  py Tools/balance/balance.py apply                  # write the stats / evolutions / synergy params into the
                                                     # HeroTrait and Destinies assets (YAML text, GUIDs kept);
                                                     # then export the game data (Unity: AutoChess/Export Game Data)

Design (balance_config.json):
  stats = role template (average of the unit's roles; Cultist has none) x tier x realm x knob, attack speed =
  template + tier bonus, then x "speed"; damage goes to the unit's own damage type(s) (its current physical /
  magic split); 2 / 3-star evolutions raise health, the unit's damage type(s) and attack speed.
  Synergy params (Data/Destinies) are listed per trait, in asset order.
The simulator is Assets/_main/Tests/EditMode/BalanceReportTests.cs (via Tools/core_tests): Temp/balance_report.md / .json.
"""
import argparse
import copy
import json
import math
import os
import re
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
DATA = ROOT / "GameData" / "game_data.json"
CONFIG = HERE / "balance_config.json"
CANDIDATE = ROOT / "Temp" / "balance_candidate.json"
REPORT_JSON = ROOT / "Temp" / "balance_report.json"
REPORT_MD = ROOT / "Temp" / "balance_report.md"
HEROES = ROOT / "Assets" / "_main" / "Data" / "HeroTraits"
DESTINIES = ROOT / "Assets" / "_main" / "Data" / "Destinies"

ROLE_BITS = {"Duelist": 1, "Sorcerer": 2, "Marksman": 4, "Assassin": 8, "Bruiser": 16, "Cultist": 32}
TIER_NAMES = {1: "Unknown", 2: "Elite", 3: "Legendary"}
FIGHT_TARGET = (17.0, 23.0)  # median team fight at level 7, seconds


def load_config():
    return json.loads(CONFIG.read_text(encoding="utf-8"))


def save_config(cfg):
    CONFIG.write_text(json.dumps(cfg, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def roles_of(bits):
    return [name for name, bit in ROLE_BITS.items() if bits & bit]


# ------------------------------------------------------------------ formula

def unit_stats(unit, cfg):
    """Design stats of one unit (dict of the game_data.json unit fields)."""
    roles = [r for r in roles_of(unit["roles"]) if r != "Cultist"] or ["Duelist"]
    templates = [cfg["roles"][r] for r in roles]

    def avg(key):
        return sum(t[key] for t in templates) / len(templates)

    tier = cfg["tiers"][unit["reputation"]]
    realm = cfg["realms"].get(unit["realm"], 1.0)
    knob = cfg["knobs"].get(unit["id"], 1.0)
    hp = avg("hp") * tier["hp"] * realm * knob
    dmg = avg("dmg") * tier["dmg"] * realm * knob
    p, m = unit["physicalDamage"], unit["magicalDamage"]
    top = max(p, m) or 1
    ad, ap = (dmg * p / top, dmg * m / top) if (p or m) else (dmg, 0)
    return {
        "maxHp": round(hp),
        "physicalDamage": round(ad),
        "magicalDamage": round(ap),
        "armor": round(avg("armor") + tier["def"]),
        "resistance": round(avg("res") + tier["def"]),
        "attackSpeed": round((avg("as") + tier["as"]) * cfg.get("speed", 1.0), 3),
        "energyPerAttack": round(avg("ePerAtk"), 1),
        "energyPerHit": round(avg("ePerHit"), 1),
        "startingEnergy": max(cfg["roles"][r]["startEnergy"] for r in roles),
    }


def evolution_stats(unit, stats, cfg):
    """[(rank, [(stat, value), ...])] for ranks 1 (2-star) and 2 (3-star)."""
    evo = cfg["evolutions"]
    out = []
    for i in range(2):
        mods = [("MaxHp", evo["hp"][i])]
        if stats["physicalDamage"] > 0:
            mods.append(("PhysicalDamage", evo["dmg"][i]))
        if stats["magicalDamage"] > 0:
            mods.append(("MagicalDamage", evo["dmg"][i]))
        mods.append(("AttackSpeed", evo["as"][i]))
        out.append((i + 1, mods))
    return out


STAT_KEYS = {"MaxHp": "max_hp", "PhysicalDamage": "p_dmg", "MagicalDamage": "m_dmg", "AttackSpeed": "atk_spd"}


def candidate_data(cfg):
    data = json.loads(DATA.read_text(encoding="utf-8"))
    for unit in data["units"]:
        if unit.get("summoned"):
            continue
        stats = unit_stats(unit, cfg)
        unit["energyPerAttack"] = stats.pop("energyPerAttack")
        unit["energyPerHit"] = stats.pop("energyPerHit")
        unit.update(stats)
        # evolutions: replace the core modifiers, keep the unit's extras (crit, lifesteal...)
        evolutions = unit.get("evolutions") or [[], [], []]
        for rank, mods in evolution_stats(unit, stats, cfg):
            extras = [e for e in evolutions[rank] if e["stat"] not in STAT_KEYS]
            evolutions[rank] = [{"stat": s, "value": v, "type": "Percent"} for s, v in mods] + extras
        unit["evolutions"] = evolutions
    for trait in data["traits"]:
        params = cfg["traits"].get(trait["value"])
        if params:
            assert len(params) == len(trait["params"]), (trait["value"], len(params), len(trait["params"]))
            trait["params"] = params
    return data


# ------------------------------------------------------------------ simulation

def run_report(data_path):
    env = dict(os.environ, AUTOCHESS_BALANCE="1", AUTOCHESS_GAME_DATA=str(data_path))
    proc = subprocess.run([sys.executable, str(ROOT / "Tools" / "core_tests" / "run_core_tests.py"), "--filter", "BalanceReport"],
                          cwd=str(ROOT), env=env, capture_output=True, text=True, encoding="utf-8")
    if proc.returncode != 0:
        print(proc.stdout[-3000:])
        raise SystemExit("simulation failed")
    return json.loads(REPORT_JSON.read_text(encoding="utf-8"))


def write_candidate(cfg):
    CANDIDATE.parent.mkdir(parents=True, exist_ok=True)
    CANDIDATE.write_text(json.dumps(candidate_data(cfg), indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return CANDIDATE


def summary(report):
    heroes = report["heroes"]
    spread = {}
    for h in heroes.values():
        spread.setdefault(h["tier"], []).append(h["ownTier"])
    tiers = report["tiers"]
    fights = report["teamFights"]
    parts = [f"tiers 2v1 {tiers['2v1']:.0%} 3v2 {tiers['3v2']:.0%} 3v1 {tiers['3v1']:.0%}"]
    powers = {}
    for h in heroes.values():
        powers.setdefault(h["tier"], []).append(h["power"])
    for t, v in sorted(powers.items()):
        parts.append(f"{TIER_NAMES[t]} power {min(v):+.2f}..{max(v):+.2f}")
    parts.append("fights " + " / ".join(f"L{k} {v['median']:.0f}s" for k, v in sorted(fights.items(), key=lambda kv: int(kv[0]))))
    return ", ".join(parts)


def calibrate(cfg, iterations):
    for it in range(iterations):
        report = run_report(write_candidate(cfg))
        print(f"[{it}] {summary(report)}", flush=True)
        # units of a tier even: nudge each knob against its same-tier power (signed margin, 0 = even)
        for hero_id, h in report["heroes"].items():
            knob = cfg["knobs"].get(hero_id, 1.0) * math.exp(-0.6 * h["power"])
            cfg["knobs"][hero_id] = round(min(2.2, max(0.45, knob)), 3)
        # pace: attack speed scales the length of fights
        median = report["teamFights"]["7"]["median"]
        if median > FIGHT_TARGET[1] or median < FIGHT_TARGET[0]:
            cfg["speed"] = round(cfg.get("speed", 1.0) * (median / sum(FIGHT_TARGET) * 2) ** 0.5, 3)
        save_config(cfg)
    report = run_report(write_candidate(cfg))
    print(f"[final] {summary(report)}")
    return report


# ------------------------------------------------------------------ assets

def set_field(text, key, value):
    pattern = rf"^(  {key}: ).*$"
    if re.search(pattern, text, re.M):
        return re.sub(pattern, rf"\g<1>{value}", text, count=1, flags=re.M)
    # new field (startingEnergy): right after energyRegenPerHit
    return re.sub(r"^(  energyRegenPerHit: .*)$", rf"\1\n  {key}: {value}", text, count=1, flags=re.M)


def fmt(v):
    return f"{v:g}"


def write_evolutions(text, mods_by_rank):
    """Rewrites the evolutions block: core modifiers from the formula, other keys kept."""
    start = text.index("  evolutions:\n")
    end = text.index("\n  skillName:", start) + 1
    block = text[start:end]
    ranks = {}
    for rank_text in re.split(r"(?m)^  - rank: ", block)[1:]:
        rank = int(rank_text.split("\n", 1)[0])
        extras = []
        for key, value, kind in re.findall(r"key: (\S+)\n      value: (\S+)\n      type: (\d)", rank_text):
            if key not in STAT_KEYS.values():
                extras.append((key, value, kind))
        ranks[rank] = extras
    lines = ["  evolutions:"]
    for rank, mods in mods_by_rank:
        lines += [f"  - rank: {rank}", "    modifiers:"]
        entries = [(STAT_KEYS[s], fmt(v), "1") for s, v in mods] + ranks.get(rank, [])
        for key, value, kind in entries:
            lines += ["    - id: <will be auto generated>", f"      key: {key}", f"      value: {value}", f"      type: {kind}"]
    return text[:start] + "\n".join(lines) + "\n" + text[end:]


def apply_assets(cfg):
    data = json.loads(DATA.read_text(encoding="utf-8"))
    by_id = {u["id"]: u for u in data["units"]}
    changed = []
    for path in sorted(HEROES.glob("*.asset")):
        text = path.read_text(encoding="utf-8")
        unit_id = re.search(r"^  id: (.*)$", text, re.M).group(1).strip()
        unit = by_id.get(unit_id)
        if unit is None or unit.get("summoned"):
            continue
        stats = unit_stats(unit, cfg)
        new = text
        for key in ("maxHp", "physicalDamage", "magicalDamage", "armor", "resistance", "attackSpeed", "startingEnergy"):
            new = set_field(new, key, fmt(stats[key]))
        new = set_field(new, "energyRegenPerAttack", fmt(stats["energyPerAttack"]))
        new = set_field(new, "energyRegenPerHit", fmt(stats["energyPerHit"]))
        new = write_evolutions(new, evolution_stats(unit, stats, cfg))
        if new != text:
            path.write_text(new, encoding="utf-8", newline="\n")
            changed.append(path.name)
    for name, params in cfg["traits"].items():
        matches = list(DESTINIES.glob(f"*/{name}.asset"))
        if not matches:
            raise SystemExit(f"no Destinies asset for {name}")
        path = matches[0]
        text = path.read_text(encoding="utf-8")
        values = iter(params)
        new = re.sub(r"(- key: \S+\n    value: )(\S+)", lambda m: m.group(1) + fmt(next(values)), text)
        if new != text:
            path.write_text(new, encoding="utf-8", newline="\n")
            changed.append(path.name)
    print("updated:", ", ".join(changed) or "nothing")
    print("next: export the game data (Unity menu AutoChess/Export Game Data, or AutoTest 'menu AutoChess/Export Game Data')")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="cmd", required=True)
    rep = sub.add_parser("report")
    rep.add_argument("--candidate", action="store_true", help="simulate the config applied to the data")
    cal = sub.add_parser("calibrate")
    cal.add_argument("--iterations", type=int, default=10)
    sub.add_parser("apply")
    args = parser.parse_args()
    cfg = load_config()
    if args.cmd == "report":
        report = run_report(write_candidate(cfg) if args.candidate else DATA)
        print(summary(report))
        print(f"full report: {REPORT_MD}")
    elif args.cmd == "calibrate":
        calibrate(cfg, args.iterations)
        print(f"config saved: {CONFIG}; full report: {REPORT_MD}")
    else:
        apply_assets(cfg)


if __name__ == "__main__":
    main()
