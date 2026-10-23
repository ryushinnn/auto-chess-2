"""Creep stages from Tools/stages/creep_stages.json -> Assets/_main/Data/Stages/<name>.asset (Stage ScriptableObjects,
creeps: 1) and the ClientConfig stage list (Stage 1 first, then these in order). Re-running rewrites the assets and
keeps their GUIDs. Re-export the game data afterwards (Unity menu AutoChess/Export Game Data).

  py Tools/stages/make_stages.py
"""
import json
import re
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CONFIG = ROOT / "Tools" / "stages" / "creep_stages.json"
STAGES = ROOT / "Assets" / "_main" / "Data" / "Stages"
ITEMS = ROOT / "Assets" / "_main" / "Data" / "Items"
CLIENT_CONFIG = ROOT / "Assets" / "_main" / "Data" / "_DB" / "Resources" / "ClientConfig.asset"
STAGE_SCRIPT = "f3ef0f974f644ec384cf929bd0bafac9"
RANKS = {"B": 0, "A": 1, "S": 2}
REPUTATIONS = {"Unknown": 1, "Elite": 2, "Legendary": 3}

META = """fileFormatVersion: 2
guid: {guid}
NativeFormatImporter:
  externalObjects: {{}}
  mainObjectFileID: 11400000
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def guid_of(path: Path) -> str:
    return re.search(r"guid: ([0-9a-f]{32})", path.with_name(path.name + ".meta").read_text(encoding="utf-8")).group(1)


def reward(text: str) -> str:
    parts = text.split()
    kind, coins, lo, hi, rep = parts[0], 0, 0, 0, 0
    if kind == "coin":
        coins = int(parts[1])
    elif kind == "random_coin":
        lo, hi = int(parts[1]), int(parts[2])
    elif kind == "hero":
        rep = REPUTATIONS[parts[1]]
    elif kind not in ("raw_item", "forged_item"):
        raise SystemExit(f"unknown reward '{text}'")
    return (f"    - type: {kind}\n      coins: {coins}\n      minCoins: {lo}\n      maxCoins: {hi}\n"
            f"      reputation: {rep}\n      randomRewards: []\n")


def rewards(key: str, items: list) -> str:
    return f"    {key}: []\n" if not items else f"    {key}:\n" + "".join(reward(r) for r in items)


def stage_yaml(stage: dict) -> str:
    out = ["%YAML 1.1", "%TAG !u! tag:unity3d.com,2011:", "--- !u!114 &11400000", "MonoBehaviour:",
           "  m_ObjectHideFlags: 0", "  m_CorrespondingSourceObject: {fileID: 0}", "  m_PrefabInstance: {fileID: 0}",
           "  m_PrefabAsset: {fileID: 0}", "  m_GameObject: {fileID: 0}", "  m_Enabled: 1", "  m_EditorHideFlags: 0",
           f"  m_Script: {{fileID: 11500000, guid: {STAGE_SCRIPT}, type: 3}}", f"  m_Name: {stage['name']}",
           "  m_EditorClassIdentifier: ", "  creeps: 1", "  matches:"]
    text = "\n".join(out) + "\n"
    hp, dmg = stage.get("hp", 1), stage.get("dmg", 1)
    for rnd in stage["rounds"]:
        text += "  - enemies:\n"
        for unit in rnd["units"]:
            unit_id, rank, (row, col) = unit[0], unit[1], unit[2]
            items = ["+".join(sorted(i.split("+"))) for i in (unit[3] if len(unit) > 3 else [])]  # ids are sorted halves
            text += f"    - heroId: {unit_id}\n      rank: {RANKS[rank]}\n      gridPosition: {{x: {row}, y: {col}}}\n"
            if items:
                text += "      items:\n" + "".join(
                    f"      - {{fileID: 11400000, guid: {guid_of(ITEMS / (i + '.asset'))}, type: 2}}\n" for i in items)
            else:
                text += "      items: []\n"
            text += f"      hpScale: {rnd.get('hp', hp)}\n      damageScale: {rnd.get('dmg', dmg)}\n"
        text += rewards("rewards", rnd.get("rewards", []))
        text += rewards("winRewards", rnd.get("win", []))
    return text


def main():
    config = json.loads(CONFIG.read_text(encoding="utf-8"))
    guids = []
    for stage in config["stages"]:
        path = STAGES / f"{stage['name']}.asset"
        meta = path.with_name(path.name + ".meta")
        if not meta.exists():
            meta.write_text(META.format(guid=uuid.uuid4().hex), encoding="utf-8", newline="\n")
        path.write_text(stage_yaml(stage), encoding="utf-8", newline="\n")
        guids.append(guid_of(path))
        print("written", path.name, f"({len(stage['rounds'])} rounds)")

    raw = CLIENT_CONFIG.read_bytes()
    nl = "\r\n" if b"\r\n" in raw else "\n"
    text = raw.decode("utf-8").replace("\r\n", "\n")
    m = re.search(r"(?m)^  stages:\n((?:  - \{[^\n]*\}\n)*)", text)
    first = guid_of(STAGES / "Stage 1.asset")
    entries = [first] + guids
    block = "  stages:\n" + "".join(f"  - {{fileID: 11400000, guid: {g}, type: 2}}\n" for g in entries)
    text = text[:m.start()] + block + text[m.end():]
    CLIENT_CONFIG.write_bytes(text.replace("\n", nl).encode("utf-8"))
    print("ClientConfig stages:", len(entries))


if __name__ == "__main__":
    main()
