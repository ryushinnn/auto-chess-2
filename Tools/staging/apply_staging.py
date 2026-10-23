"""
Applies .staging/ to the project: renames (git mv, .meta included so GUIDs and serialized references
survive), deletes (git rm with .meta), then copies every staged file over its target. Empty folders left
behind are removed with their .meta. Run compile_check --overlay .staging first.

  py Tools/staging/apply_staging.py [--dry-run]
"""
import json
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
STAGING = ROOT / ".staging"


def git(*args):
    subprocess.run(["git", *args], cwd=ROOT, check=True, capture_output=True, text=True)


def tracked(path: Path) -> bool:
    r = subprocess.run(["git", "ls-files", "--error-unmatch", str(path.relative_to(ROOT))], cwd=ROOT,
                       capture_output=True, text=True)
    return r.returncode == 0


def move(src: Path, dst: Path, dry: bool):
    dst.parent.mkdir(parents=True, exist_ok=True)
    if dry:
        print(f"mv {src.relative_to(ROOT)} -> {dst.relative_to(ROOT)}")
        return
    if tracked(src):
        git("mv", str(src.relative_to(ROOT)), str(dst.relative_to(ROOT)))
    else:
        shutil.move(str(src), str(dst))


def remove(path: Path, dry: bool):
    if not path.exists():
        return
    if dry:
        print(f"rm {path.relative_to(ROOT)}")
        return
    if tracked(path):
        git("rm", "-q", str(path.relative_to(ROOT)))
    else:
        path.unlink()


def prune_empty_dirs(base: Path, dry: bool):
    for d in sorted((p for p in base.rglob("*") if p.is_dir()), key=lambda p: len(p.parts), reverse=True):
        if not d.exists() or any(d.iterdir()):
            continue
        meta = d.with_name(d.name + ".meta")
        if dry:
            print(f"rmdir {d.relative_to(ROOT)}")
            continue
        d.rmdir()
        remove(meta, dry)
    # folders git already removed (it deletes emptied directories) leave their .meta behind
    for meta in base.rglob("*.meta"):
        folder = meta.with_name(meta.name[:-5])
        if not folder.exists() and not meta.name.endswith(".cs.meta") and "." not in folder.name:
            remove(meta, dry)


def main() -> int:
    dry = "--dry-run" in sys.argv
    manifest = json.loads((STAGING / "manifest.json").read_text(encoding="utf-8"))
    for old, new in manifest["renames"]:
        src, dst = ROOT / old, ROOT / new
        if not src.exists():
            print(f"skip rename, missing: {old}")
            continue
        move(src, dst, dry)
        src_meta, dst_meta = Path(str(src) + ".meta"), Path(str(dst) + ".meta")
        if src_meta.exists():
            move(src_meta, dst_meta, dry)
    for path in manifest["deletes"]:
        remove(ROOT / path, dry)
        remove(Path(str(ROOT / path) + ".meta"), dry)

    copied = 0
    for staged in STAGING.rglob("*"):
        if not staged.is_file() or staged.name == "manifest.json":
            continue
        target = ROOT / staged.relative_to(STAGING)
        if dry:
            print(f"write {target.relative_to(ROOT)}")
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(staged, target)
        copied += 1
    prune_empty_dirs(ROOT / "Assets" / "_main" / "Scripts", dry)
    print(f"{'would apply' if dry else 'applied'}: {len(manifest['renames'])} renames, {len(manifest['deletes'])} deletes, "
          f"{copied} files written")
    return 0


if __name__ == "__main__":
    sys.exit(main())
