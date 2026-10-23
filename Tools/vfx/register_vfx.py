"""Adds every prefab of Assets/_main/Prefabs/VFX to the Arena's VfxPool.vfxPrefabs (scene YAML, GUIDs kept).

  py Tools/vfx/register_vfx.py
"""
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCENE = ROOT / "Assets" / "_main" / "Scenes" / "Arena.unity"
VFX = ROOT / "Assets" / "_main" / "Prefabs" / "VFX"


def root_object(prefab: Path) -> str:
    """fileID of the prefab's root GameObject (the one named like the file)."""
    text = prefab.read_text(encoding="utf-8")
    for block in re.split(r"(?m)^--- ", text):
        m = re.match(r"!u!1 &(-?\d+)\n", block)
        if m and re.search(rf"(?m)^  m_Name: {re.escape(prefab.stem)}$", block):
            return m.group(1)
    raise SystemExit(f"no root GameObject in {prefab}")


def main():
    raw = SCENE.read_bytes()
    newline = "\r\n" if b"\r\n" in raw else "\n"
    scene = raw.decode("utf-8").replace("\r\n", "\n")
    m = re.search(r"(?m)^  vfxPrefabs:\n((?:  - \{[^\n]*\}\n)*)", scene)
    if not m:
        raise SystemExit("no vfxPrefabs list in the Arena scene")
    entries = m.group(1)
    added = []
    for prefab in sorted(VFX.glob("*.prefab")):
        guid = re.search(r"guid: (\w+)", (prefab.parent / (prefab.name + ".meta")).read_text(encoding="utf-8")).group(1)
        if guid in entries:
            continue
        entries += f"  - {{fileID: {root_object(prefab)}, guid: {guid}, type: 3}}\n"
        added.append(prefab.stem)
    if added:
        scene = scene[:m.start(1)] + entries + scene[m.end(1):]
        SCENE.write_text(scene, encoding="utf-8", newline=newline)
    print("added:", ", ".join(added) or "nothing")


if __name__ == "__main__":
    main()
