"""Writes docs/ITEMS.md: every component and combined item with its stats and passive (numbers filled in).

  py Tools/items/item_doc.py

Source: GameData/game_data.json (names, recipes, stats) + Tools/items/items.json (passive text and params).
"""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT / "GameData" / "game_data.json"
CONFIG = ROOT / "Tools" / "items" / "items.json"
OUT = ROOT / "docs" / "ITEMS.md"

STATS = {
    "MaxHp": "Máu", "PhysicalDamage": "Sát thương vật lý", "MagicalDamage": "Sát thương phép", "Armor": "Giáp",
    "Resistance": "Kháng phép", "AttackSpeed": "Tốc độ đánh", "CriticalChance": "Tỉ lệ chí mạng",
    "CriticalDamage": "Sát thương chí mạng", "EnergyRegenEfficiency": "Hiệu quả hồi năng lượng",
    "PhysicalPenetration": "Xuyên giáp", "MagicalPenetration": "Xuyên kháng phép", "LifeSteal": "Hút máu",
    "Tenacity": "Kháng hiệu ứng",
}
FRACTIONS = {"CriticalChance", "CriticalDamage", "EnergyRegenEfficiency", "PhysicalPenetration", "MagicalPenetration",
             "LifeSteal", "Tenacity"}
SPRITES = {"hp": "Máu", "pdmg": "Sát thương vật lý", "mdmg": "Sát thương phép", "arm": "Giáp", "res": "Kháng phép",
           "aspd": "Tốc độ đánh", "crc": "Tỉ lệ chí mạng", "crd": "Sát thương chí mạng", "ere": "Hiệu quả hồi năng lượng",
           "ls": "Hút máu", "ten": "Kháng hiệu ứng", "eng": "năng lượng", "rng": "Tầm đánh"}
CARRIERS = {"ad": "tướng sát thương vật lý", "ap": "tướng sát thương phép", "tank": "tướng đỡ đòn",
            "attack": "tướng đánh thường nhiều", "any": "mọi tướng"}


def stat_text(m):
    v = m["value"]
    pct = m["type"] == "Percent" or m["stat"] in FRACTIONS
    num = f"{v * 100:.4g}%" if pct else f"{v:.4g}"
    return f"+{num} {STATS.get(m['stat'], m['stat'])}"


def render(text, params):
    def number(m):
        key, value, pct = params[int(m.group(1))]
        return f"{abs(value) * 100:.4g}%" if pct else f"{abs(value):.4g}"
    s = re.sub(r"#\[(\d+)\]", number, text)
    s = re.sub(r"(<sprite name=\w+>)\s*(?=<sprite)", r"\1 / ", s)
    s = re.sub(r"<sprite name=(\w+)>", lambda m: " " + SPRITES.get(m.group(1), m.group(1)), s)
    s = re.sub(r"\s*/\s*", " / ", s)
    s = re.sub(r" +", " ", s)
    return s.replace(" .", ".").replace(" ,", ",").replace("( ", "(")


def main():
    data = json.loads(DATA.read_text(encoding="utf-8"))
    cfg = json.loads(CONFIG.read_text(encoding="utf-8"))
    items = {i["id"]: i for i in data["items"]}
    design = {i["id"]: i for i in cfg["items"]}
    md = ["# Trang bị", "",
          "Sinh tự động bởi `py Tools/items/item_doc.py` (đừng sửa tay). Thiết kế và số liệu: `Tools/items/items.json` "
          "(menu *AutoChess/Items/Apply Item Config* ghi vào `Data/Items`), logic nội tại: "
          "`Assets/_main/Scripts/Core/Combat/ItemEffects.cs`. Mỗi tướng mang tối đa 3 trang bị; trang bị hoàn chỉnh = "
          "chỉ số của 2 nguyên liệu + nội tại.", "", "## Nguyên liệu", "", "| Nguyên liệu | Chỉ số |", "|---|---|"]
    for cid in cfg["components"]:
        item = items[cid]
        md.append(f"| {item['name']} (`{cid}`) | {', '.join(stat_text(m) for m in item['modifiers'])} |")
    md += ["", "## Trang bị hoàn chỉnh", ""]
    for iid in sorted(design):
        item = items[iid]
        d = design[iid]
        recipe = " + ".join(items[x]["name"] for x in item["ingredients"])
        passives = render(d["description"], d["params"]).split("\n")
        md += [f"### {item['name']} (`{iid}`)", "",
               f"- Công thức: {recipe}",
               f"- Chỉ số: {', '.join(stat_text(m) for m in item['modifiers'])}",
               f"- Hợp với: {CARRIERS.get(d.get('carrier'), d.get('carrier'))}"]
        md += [f"- Nội tại{f' {k + 1}' if len(passives) > 1 else ''}: {p}" for k, p in enumerate(passives)]
        md.append("")
    OUT.write_text("\n".join(md), encoding="utf-8", newline="\n")
    print(f"written: {OUT}")


if __name__ == "__main__":
    main()
