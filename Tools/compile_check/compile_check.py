"""
compile_check.py - compile the project's C# with Unity's own Roslyn, without asking the Unity editor.

What it builds (in dependency order):
  * every .asmdef under the project roots (PROJECT_ROOTS below), e.g. AutoChess.Core, its tests,
    the AutoTest editor server;
  * Assembly-CSharp (everything under Assets/ that no .asmdef owns and is not in an Editor folder);
  * Assembly-CSharp-Editor (with --editor, or when an editor-only assembly is requested).

How
  1. Reuses the newest Library/Bee/artifacts/*.dag/Assembly-CSharp(.Editor).rsp the editor wrote on its last
     compile: defines, framework / engine / plugin references and compiler options are taken verbatim.
  2. For an .asmdef the reference list is rebuilt the way Unity does it: engine references unless
     noEngineReferences, precompiled plugin dlls unless overrideReferences (then only the listed ones),
     plus the declared asmdef references (a fresh dll when built in this run, else the editor's .ref.dll).
  3. Runs <Unity>/Editor/Data/NetCoreRuntime/dotnet.exe <Unity>/Editor/Data/DotNetSdkRoslyn/csc.dll.

Usage (from anywhere):
  py Tools/compile_check/compile_check.py                 # all project asmdefs + Assembly-CSharp, errors only
  py Tools/compile_check/compile_check.py --editor        # also Assembly-CSharp-Editor
  py Tools/compile_check/compile_check.py --only AutoChess.Core   # one assembly (and what it needs)
  py Tools/compile_check/compile_check.py --warnings      # also print warnings
  py Tools/compile_check/compile_check.py --list          # print the assemblies and their sources
  py Tools/compile_check/compile_check.py --overlay .staging --editor   # compile a staged change before applying it
Environment:
  UNITY_EDITOR_PATH  override the editor root (default: C:/Program Files/Unity/Hub/Editor/<ProjectVersion>)
Exit code: 0 = every built assembly compiled, 1 = at least one failed, 2 = setup problem.

The editor must have compiled the project once (so the .rsp exists). Adding or removing files or asmdefs
needs no new .rsp; adding a package or a scripting define does.
"""
import json
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "Assets"
# asmdefs under these folders are built from source; any other asmdef (third party, Assets/Packages,
# Assets/Plugins, ...) is referenced through the .ref.dll the editor already produced.
PROJECT_ROOTS = [ASSETS / "_main", ASSETS / "AutoTest"]
FIRSTPASS = [ASSETS / "Plugins", ASSETS / "Standard Assets", ASSETS / "Pro Standard Assets"]
BUILD_DIR = Path(tempfile.gettempdir()) / "autochess_compile_check"
# Optional staging overlay (--overlay DIR): DIR mirrors the project root; DIR/manifest.json may list
# {"renames": [[old, new], ...], "deletes": [path, ...]} (project-relative). Lets a large change be compiled
# before it is applied to Assets/ (where the open editor would pick it up half done).
OVERLAY = None


class Overlay:
    def __init__(self, root: Path):
        self.root = root.resolve()
        manifest = self.root / "manifest.json"
        data = json.loads(manifest.read_text(encoding="utf-8")) if manifest.exists() else {}
        self.renames = {(ROOT / a).resolve(): (ROOT / b).resolve() for a, b in data.get("renames", [])}
        self.deletes = {(ROOT / d).resolve() for d in data.get("deletes", [])}
        self.files = {}
        for f in self.root.rglob("*"):
            if f.is_file() and f.name != "manifest.json":
                self.files[(ROOT / f.relative_to(self.root)).resolve()] = f

    def virtual_files(self, pattern_suffix: str):
        """(virtual project path, real file to read) for every file of the virtual tree with the suffix."""
        result = {}
        for real in ASSETS.rglob("*" + pattern_suffix):
            r = real.resolve()
            if r in self.deletes:
                continue
            virtual = self.renames.get(r, r)
            if virtual in self.deletes:
                continue
            result[virtual] = r
        for virtual, real in self.files.items():
            if virtual.suffix == pattern_suffix and is_under(virtual, ASSETS.resolve()):
                result[virtual] = real
        return result


def tree(suffix: str):
    """Virtual project tree: {project path: real path}, honouring the overlay."""
    if OVERLAY is None:
        return {p.resolve(): p.resolve() for p in ASSETS.rglob("*" + suffix)}
    return OVERLAY.virtual_files(suffix)


class SetupError(Exception):
    pass


def unity_editor_root() -> Path:
    env = os.environ.get("UNITY_EDITOR_PATH")
    if env:
        return Path(env)
    version = None
    for line in (ROOT / "ProjectSettings" / "ProjectVersion.txt").read_text(encoding="utf-8").splitlines():
        if line.startswith("m_EditorVersion:"):
            version = line.split(":", 1)[1].strip()
            break
    if not version:
        raise SetupError("ProjectVersion.txt: m_EditorVersion not found")
    root = Path("C:/Program Files/Unity/Hub/Editor") / version
    if not root.exists():
        raise SetupError(f"Unity {version} not found at {root}; set UNITY_EDITOR_PATH")
    return root


def dag_dir() -> Path:
    candidates = list((ROOT / "Library" / "Bee" / "artifacts").glob("*.dag/Assembly-CSharp.rsp"))
    if not candidates:
        raise SetupError("No Library/Bee/artifacts/*.dag/Assembly-CSharp.rsp - open the project in Unity once")
    return max(candidates, key=lambda p: p.stat().st_mtime).parent


def is_under(path: Path, folder: Path) -> bool:
    try:
        path.relative_to(folder)
        return True
    except ValueError:
        return False


def hidden(rel_parts) -> bool:
    return any(p.endswith("~") or p.startswith(".") for p in rel_parts)


class Asm:
    def __init__(self, name, folder=None, data=None, predefined=None):
        self.name = name
        self.folder = folder
        self.data = data or {}
        self.predefined = predefined  # "runtime" | "editor" | None
        self.sources = []
        self.out = BUILD_DIR / f"{name}.dll"
        self.built = False
        self.ok = None

    @property
    def editor_only(self):
        if self.predefined:
            return self.predefined == "editor"
        return self.data.get("includePlatforms") == ["Editor"]

    @property
    def references(self):
        return list(self.data.get("references", []))


def load_json(path: Path):
    text = path.read_text(encoding="utf-8-sig")
    return json.loads(text)


def discover():
    """Return (project asmdefs by name, all asmdef folders, guid->name)."""
    all_dirs = []
    guid_to_name = {}
    project = {}
    for asmdef, real in tree(".asmdef").items():
        rel = asmdef.relative_to(ASSETS.resolve()).parts
        if hidden(rel[:-1]):
            continue
        all_dirs.append(asmdef.parent)
        data = load_json(real)
        name = data.get("name", asmdef.stem)
        meta = real.with_suffix(".asmdef.meta")
        if meta.exists():
            m = re.search(r"guid:\s*([0-9a-f]+)", meta.read_text(encoding="utf-8"))
            if m:
                guid_to_name[m.group(1)] = name
        if any(is_under(asmdef, r.resolve()) for r in PROJECT_ROOTS):
            project[name] = Asm(name, asmdef.parent, data)
    for asmref in tree(".asmref"):
        all_dirs.append(asmref.parent)
    return project, all_dirs, guid_to_name


def collect_asmdef_sources(asm: Asm, all_dirs):
    nested = [d for d in all_dirs if d != asm.folder and is_under(d, asm.folder)]
    result = []
    for cs, real in tree(".cs").items():
        if not is_under(cs, asm.folder):
            continue
        rel = cs.relative_to(asm.folder).parts
        if hidden(rel[:-1]):
            continue
        if any(is_under(cs, d) for d in nested):
            continue
        result.append(real)
    return sorted(result)


def collect_predefined_sources(editor: bool, all_dirs):
    result = []
    assets = ASSETS.resolve()
    for cs, real in tree(".cs").items():
        rel = cs.relative_to(assets).parts
        if hidden(rel[:-1]):
            continue
        in_editor = any(p == "Editor" for p in rel[:-1])
        if in_editor != editor:
            continue
        if any(is_under(cs, d) for d in all_dirs):
            continue
        if any(is_under(cs, d.resolve()) for d in FIRSTPASS):
            continue
        result.append(real)
    return sorted(result)


def read_rsp(path: Path):
    lines = []
    for line in path.read_text(encoding="utf-8").splitlines():
        s = line.strip()
        if s:
            lines.append(s)
    return lines


def ref_path(line: str) -> str:
    return line[3:].strip().strip('"')


def is_engine_ref(p: str) -> bool:
    low = p.replace("\\", "/").lower()
    return "/managed/unityengine/" in low or "/playbackengines/" in low or low.endswith("/unityeditor.dll") \
        or low.endswith("/unityengine.dll")


def is_bee_ref(p: str) -> bool:
    return "library/bee/artifacts" in p.replace("\\", "/").lower()


def is_precompiled_plugin(p: str) -> bool:
    low = p.replace("\\", "/").lower()
    return low.startswith("assets/") or low.startswith("library/packagecache/")


def find_precompiled(name: str, base_lines):
    for line in base_lines:
        if line.startswith("-r:") and Path(ref_path(line)).name.lower() == name.lower():
            return ref_path(line)
    for folder in (ASSETS, ROOT / "Library" / "PackageCache"):
        for dll in folder.rglob(name):
            return dll.relative_to(ROOT).as_posix()
    return None


def options_and_defines(base_lines):
    kept = []
    for s in base_lines:
        if s.startswith(("-out:", "-refout:", "-analyzer:", "-additionalfile:", "/additionalfile:", "-pdb:", "-r:")):
            continue
        if s.startswith('"') and s.lower().endswith('.cs"'):
            continue
        kept.append(s)
    return kept


def resolve_reference(ref: str, guid_to_name, project, dag: Path):
    if ref.startswith("GUID:"):
        name = guid_to_name.get(ref[5:])
        if not name:
            return None, f"unknown asmdef GUID {ref}"
        ref = name
    if ref in project:
        return project[ref], None
    bee = dag / f"{ref}.ref.dll"
    if bee.exists():
        return bee.relative_to(ROOT).as_posix(), None
    return None, f"reference '{ref}' has no .ref.dll yet (let Unity compile once)"


def build_rsp(asm: Asm, base_lines, extra_refs, out_rsp: Path):
    lines = options_and_defines(base_lines)
    refs = []
    data = asm.data
    for line in base_lines:
        if not line.startswith("-r:"):
            continue
        p = ref_path(line)
        if asm.predefined:
            refs.append(p)
            continue
        if is_bee_ref(p):
            continue  # asmdefs only see their declared references
        if is_engine_ref(p) and data.get("noEngineReferences"):
            continue
        if is_precompiled_plugin(p) and data.get("overrideReferences"):
            continue
        refs.append(p)
    if not asm.predefined and data.get("overrideReferences"):
        for name in data.get("precompiledReferences", []):
            found = find_precompiled(name, base_lines)
            if found:
                refs.append(found)
    # replace / append fresh project references
    final = []
    seen = set()
    fresh_by_stem = {Path(r).stem: r for r in extra_refs}
    for p in refs:
        stem = Path(p).name
        stem = stem[:-8] if stem.endswith(".ref.dll") else Path(p).stem
        if stem in fresh_by_stem:
            p = fresh_by_stem.pop(stem)
        key = p.lower()
        if key not in seen:
            seen.add(key)
            final.append(p)
    for p in fresh_by_stem.values():
        if p.lower() not in seen:
            final.append(p)
    lines.insert(1, f'-out:"{asm.out.as_posix()}"')
    lines.extend(f'-r:"{p}"' for p in final)
    lines.extend(f'"{rel_or_abs(s)}"' for s in asm.sources)
    out_rsp.write_text("\n".join(lines) + "\n", encoding="utf-8")


def rel_or_abs(p: Path) -> str:
    try:
        return p.resolve().relative_to(ROOT.resolve()).as_posix()
    except ValueError:
        return p.resolve().as_posix()


def run_csc(unity: Path, rsp: Path):
    dotnet = unity / "Editor" / "Data" / "NetCoreRuntime" / "dotnet.exe"
    csc = unity / "Editor" / "Data" / "DotNetSdkRoslyn" / "csc.dll"
    for tool in (dotnet, csc):
        if not tool.exists():
            raise SetupError(f"missing {tool}")
    proc = subprocess.run([str(dotnet), str(csc), f"@{rsp}"], cwd=str(ROOT), capture_output=True, text=True,
                          encoding="utf-8", errors="replace")
    output = (proc.stdout or "") + (proc.stderr or "")
    return proc.returncode, output


def topo_order(project, guid_to_name):
    order, state = [], {}

    def visit(asm):
        if state.get(asm.name) == 2:
            return
        if state.get(asm.name) == 1:
            raise SetupError(f"asmdef cycle at {asm.name}")
        state[asm.name] = 1
        for ref in asm.references:
            name = guid_to_name.get(ref[5:], ref) if ref.startswith("GUID:") else ref
            if name in project:
                visit(project[name])
        state[asm.name] = 2
        order.append(asm)

    for asm in sorted(project.values(), key=lambda a: a.name):
        visit(asm)
    return order


def plan(only=None, editor=False):
    project, all_dirs, guid_to_name = discover()
    for asm in project.values():
        asm.sources = collect_asmdef_sources(asm, all_dirs)
    runtime = Asm("Assembly-CSharp", predefined="runtime")
    runtime.sources = collect_predefined_sources(False, all_dirs)
    editor_asm = Asm("Assembly-CSharp-Editor", predefined="editor")
    editor_asm.sources = collect_predefined_sources(True, all_dirs)

    ordered = topo_order(project, guid_to_name)
    targets = ordered + [runtime]
    if editor or (only and "Assembly-CSharp-Editor" in only):
        targets.append(editor_asm)
    if only:
        wanted = set(only)
        # pull in dependencies of the requested assemblies
        changed = True
        while changed:
            changed = False
            for asm in list(targets):
                if asm.name not in wanted:
                    continue
                deps = []
                if asm.predefined:
                    deps = [a.name for a in ordered if a.data.get("autoReferenced", True)
                            and (asm.predefined == "editor" or not a.editor_only)]
                    if asm.predefined == "editor":
                        deps.append("Assembly-CSharp")
                else:
                    for ref in asm.references:
                        deps.append(guid_to_name.get(ref[5:], ref) if ref.startswith("GUID:") else ref)
                for d in deps:
                    if d in project or d == "Assembly-CSharp":
                        if d not in wanted:
                            wanted.add(d)
                            changed = True
        targets = [a for a in targets if a.name in wanted]
    return targets, project, guid_to_name


def build(targets, project, guid_to_name, show_warnings=False, quiet=False):
    unity = unity_editor_root()
    dag = dag_dir()
    BUILD_DIR.mkdir(parents=True, exist_ok=True)
    rsp_runtime = read_rsp(dag / "Assembly-CSharp.rsp")
    rsp_editor_path = dag / "Assembly-CSharp-Editor.rsp"
    rsp_editor = read_rsp(rsp_editor_path) if rsp_editor_path.exists() else rsp_runtime
    built = {}
    all_ok = True
    for asm in targets:
        base = rsp_editor if asm.editor_only else rsp_runtime
        extra = []
        problems = []
        if asm.predefined:
            for other in project.values():
                if other.name in built and other.data.get("autoReferenced", True) \
                        and (asm.predefined == "editor" or not other.editor_only):
                    extra.append(built[other.name])
            if asm.predefined == "editor" and "Assembly-CSharp" in built:
                extra.append(built["Assembly-CSharp"])
        else:
            for ref in asm.references:
                resolved, err = resolve_reference(ref, guid_to_name, project, dag)
                if err:
                    problems.append(err)
                elif isinstance(resolved, Asm):
                    if resolved.name in built:
                        extra.append(built[resolved.name])
                    else:
                        bee = dag / f"{resolved.name}.ref.dll"
                        if bee.exists():
                            extra.append(bee.relative_to(ROOT).as_posix())
                        else:
                            problems.append(f"reference '{resolved.name}' was not built")
                else:
                    extra.append(resolved)
        if problems:
            for p in problems:
                print(f"[compile_check] {asm.name}: {p}")
            asm.ok = False
            all_ok = False
            continue
        if not asm.sources:
            if not quiet:
                print(f"[compile_check] {asm.name}: no sources, skipped")
            continue
        rsp = BUILD_DIR / f"{asm.name}.check.rsp"
        build_rsp(asm, base, extra, rsp)
        code, output = run_csc(unity, rsp)
        errors = [l for l in output.splitlines() if re.search(r"\berror CS\d+", l)]
        warnings = [l for l in output.splitlines() if re.search(r"\bwarning CS\d+", l)]
        for l in errors:
            print(l)
        if show_warnings:
            for l in warnings:
                print(l)
        asm.ok = code == 0
        if asm.ok:
            built[asm.name] = asm.out.as_posix()
        else:
            all_ok = False
            if not errors:
                print(output.strip()[-2000:])
        if not quiet or not asm.ok:
            print(f"[compile_check] {asm.name}: {len(asm.sources)} sources, {len(errors)} errors, "
                  f"{len(warnings)} warnings, exit {code}")
    return all_ok, built


def main(argv=None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)
    show_warnings = "--warnings" in argv
    list_only = "--list" in argv
    editor = "--editor" in argv
    global OVERLAY
    if "--overlay" in argv:
        OVERLAY = Overlay(Path(argv[argv.index("--overlay") + 1]))
    only = []
    if "--only" in argv:
        i = argv.index("--only")
        only = []
        for x in argv[i + 1:]:
            if x.startswith("--"):
                break
            only.append(x)
    try:
        targets, project, guid_to_name = plan(only or None, editor)
        if list_only:
            for asm in targets:
                print(f"{asm.name}: {len(asm.sources)} files"
                      + (f" ({asm.folder.relative_to(ROOT).as_posix()})" if asm.folder else ""))
                for s in asm.sources:
                    print("   " + rel_or_abs(s))
            return 0
        ok, _ = build(targets, project, guid_to_name, show_warnings)
    except SetupError as e:
        print(f"[compile_check] {e}")
        return 2
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
