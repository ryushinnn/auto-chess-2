"""Writes docs/SYNERGIES.md: every realm / role synergy with its params filled into the description.

Source: Assets/_main/Data/Destinies/*/*.asset (description, checkpoints, destinyParams) and GameData/game_data.json
(members). Run it after changing the synergies (balance.py apply runs it too):

  py Tools/balance/synergy_doc.py
"""
import codecs
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DESTINIES = ROOT / "Assets" / "_main" / "Data" / "Destinies"
DATA = ROOT / "GameData" / "game_data.json"
OUT = ROOT / "docs" / "SYNERGIES.md"

NAMES = {
    "Mortal": "Phàm nhân", "Divine": "Thánh thần", "Nether": "Âm ti", "Mecha": "Cơ giới", "Chaos": "Hỗn mang",
    "Duelist": "Kiếm sĩ", "Sorcerer": "Pháp sư", "Marksman": "Xạ thủ", "Assassin": "Thích khách",
    "Bruiser": "Quyền Vương", "Cultist": "Tín đồ",
}
ROLE_BITS = {"Duelist": 1, "Sorcerer": 2, "Marksman": 4, "Assassin": 8, "Bruiser": 16, "Cultist": 32}
REPUTATION = {"Unknown": "Vô danh", "Elite": "Siêu việt", "Legendary": "Truyền thuyết"}
SPRITES = {
    "hp": "Máu", "pdmg": "Sát thương vật lý", "mdmg": "Sát thương phép", "arm": "Giáp", "res": "Kháng phép",
    "aspd": "Tốc độ đánh", "crc": "Tỉ lệ chí mạng", "crd": "Sát thương chí mạng", "ppen": "Xuyên giáp",
    "mpen": "Xuyên kháng phép", "ls": "Hút máu", "ere": "Hiệu quả hồi năng lượng", "ten": "Kháng hiệu ứng",
}
# How the code reads what the description leaves open (Core/Combat/TraitEffects.cs).
NOTES = {
    "Assassin": ["Chỉ tính đòn chí mạng của chính Thích khách; sát thương chuẩn bỏ qua giáp / kháng phép."],
    "Bruiser": ["Giáp / kháng phép bật khi máu trên ngưỡng và tắt khi xuống dưới ngưỡng (bật lại nếu được hồi máu)."],
    "Cultist": [
        "Vật chủ = Tín đồ mạnh nhất (sao cao hơn, rồi danh tiếng cao hơn, rồi máu nhiều hơn).",
        "Ngoài chỉ số chung, CHÚA QUỶ nhận thêm lợi thế theo role còn lại của vật chủ: Pháp sư +40% hiệu quả hồi "
        "năng lượng và đầy năng lượng ngay; Kiếm sĩ +30% tốc độ đánh; Thích khách +40% sát thương chí mạng; "
        "Xạ thủ +25% tỉ lệ chí mạng; Quyền Vương +40 giáp / kháng phép; chỉ là Tín đồ: +20% máu.",
    ],
    "Duelist": ["Tham số đầu (min_hp_threshold) hiện không dùng."],
    "Marksman": ["Mỗi Xạ thủ tử trận cộng thêm một lượt tốc độ đánh riêng (cộng dồn được), hết hạn theo thời gian."],
    "Sorcerer": ["Lần thi triển thêm: năng lượng được nạp đầy ngay sau lần thi triển đầu tiên."],
    "Chaos": ["Cần cả A Chốc Nhật (Aatrox_Light) và A Chốc Nguyệt (Aatrox_Dark) trên sân."],
    "Divine": ["Chỉ áp dụng cho đòn đánh thường."],
    "Mecha": [
        "Mỗi Cơ giới tự đếm sát thương gây ra / nhận vào; mốc 4 tên lửa có thể chí mạng.",
        "Tên lửa gây sát thương vật lý hay phép tuỳ chỉ số phòng thủ nào của mục tiêu thấp hơn.",
    ],
    "Mortal": ["Mỗi tướng chỉ tính một lần dù có nhiều bản sao trên sân."],
    "Nether": ["Nợ máu giảm 15% mỗi giây (trừ ở mốc cuối)."],
}


def read_asset(path):
    text = path.read_text(encoding="utf-8")
    params = [(k, float(v), p == "1") for k, v, p in
              re.findall(r"- key: (\S+)\n    value: (\S+)\n    isPercentage: (\d)", text)]
    hexes = re.search(r"checkpoints: (\S*)", text).group(1)
    checkpoints = [int.from_bytes(bytes.fromhex(hexes[i:i + 8]), "little") for i in range(0, len(hexes), 8)]
    m = re.search(r'  description: "(.*?)"\n  \w', text, re.S)
    desc = m.group(1) if m else ""
    desc = re.sub(r"\\\n\s*", "", desc)          # YAML line continuation
    desc = re.sub(r"\n\s+", " ", desc)            # YAML folding
    desc = codecs.decode(desc.encode("latin-1", "backslashreplace"), "unicode_escape")
    return params, checkpoints, desc


def number(value, percent):
    v = value * 100 if percent else value
    s = f"{v:.2f}".rstrip("0").rstrip(".")
    return s + ("%" if percent else "")


def render(desc, params, checkpoints):
    s = re.sub(r"#\[(\d+)\]", lambda m: number(params[int(m.group(1))][1], params[int(m.group(1))][2]) + " ", desc)
    s = re.sub(r"&\[(\d+)\]", lambda m: f"{checkpoints[int(m.group(1))]} tướng", s)
    s = s.replace("<b>", "**").replace("</b>", "**")
    s = re.sub(r"</?color[^>]*>", "", s)
    s = re.sub(r"<(\d+)>", "\n- ", s)
    s = re.sub(r"</\d+>", "", s)
    # adjacent stat icons ("arm res") read as "Giáp / Kháng phép"
    s = re.sub(r"(<sprite name=\w+>)\s*(?=<sprite)", r"\1 / ", s)
    s = re.sub(r"<sprite name=(\w+)>", lambda m: f"{SPRITES.get(m.group(1), m.group(1))} ", s)
    s = re.sub(r"\s*/\s*", " / ", s)
    lines = [re.sub(r"[ \t]+", " ", l).replace(" .", ".").replace(" ,", ",").replace(" )", ")").strip()
             for l in s.split("\n")]
    out, blank = [], False
    for l in lines:
        if not l:
            blank = True
            continue
        if blank and out and not l.startswith("- "):
            out.append("")
        out.append(l)
        blank = False
    return "\n".join(out)


def members(units, kind, name):
    found = []
    for u in units:
        if u.get("summoned") or u.get("reputation") in (None, "None"):
            continue
        if kind == "Realms" and u.get("realm") == name or kind == "Roles" and u.get("roles", 0) & ROLE_BITS[name]:
            found.append(u)
    order = {"Unknown": 0, "Elite": 1, "Legendary": 2}
    found.sort(key=lambda u: (order.get(u["reputation"], 9), u["id"]))
    return ", ".join(f"{u['name']} ({u['id']}, {REPUTATION.get(u['reputation'], u['reputation'])})" for u in found)


def main():
    units = json.loads(DATA.read_text(encoding="utf-8"))["units"]
    md = ["# Synergies (realm / role)", "",
          "Sinh tự động bởi `py Tools/balance/synergy_doc.py` từ `Assets/_main/Data/Destinies` (đừng sửa tay; số liệu "
          "chỉnh trong `Tools/balance/balance_config.json` rồi `balance.py apply`). Logic: "
          "`Assets/_main/Scripts/Core/Combat/TraitEffects.cs`.", "",
          "Ký hiệu chỉ số: " + ", ".join(f"`{k}` = {v}" for k, v in SPRITES.items()) + ".", ""]
    for kind, title in (("Realms", "Realm"), ("Roles", "Role")):
        md += [f"## {title}", ""]
        for path in sorted((DESTINIES / kind).glob("*.asset")):
            name = path.stem
            params, checkpoints, desc = read_asset(path)
            md += [f"### {NAMES.get(name, name)} ({name})", "",
                   f"Mốc kích hoạt: {' / '.join(str(c) for c in checkpoints)} tướng. "
                   f"Thành viên: {members(units, kind, name) or '-'}.", "",
                   render(desc, params, checkpoints), ""]
            notes = NOTES.get(name)
            if notes:
                md += ["Ghi chú triển khai:", ""] + [f"- {n}" for n in notes] + [""]
            md += ["Tham số: " + ", ".join(f"`{k}` = {number(v, p)}" for k, v, p in params) + ".", ""]
    OUT.write_text("\n".join(md), encoding="utf-8", newline="\n")
    print(f"written: {OUT}")


if __name__ == "__main__":
    main()
