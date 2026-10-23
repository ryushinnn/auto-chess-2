"""
run_core_tests.py - run the AutoChess.Core NUnit tests without Unity.

Builds AutoChess.Core and AutoChess.Core.Tests with Unity's Roslyn (via compile_check), compiles a tiny
reflection-based runner for the .NET 6 runtime that ships with the editor, and runs the tests in-process.
The same tests also run inside Unity (Test Runner window, or `AutoTest Server/unity_tests.bat`).

Usage (from anywhere):
  py Tools/core_tests/run_core_tests.py                     # all tests
  py Tools/core_tests/run_core_tests.py --filter Combat     # tests whose full name contains "Combat"
  py Tools/core_tests/run_core_tests.py --json out.json     # also write machine-readable results
Exit code: 0 = all passed, 1 = failures, 2 = setup / compile problem.

Real-data tests read GameData/game_data.json (menu AutoChess/Export Game Data in the editor); they are
skipped when the file is missing.
"""
import json
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE.parent / "compile_check"))
import compile_check as cc  # noqa: E402

RUNTIME_VERSION_GLOB = "Editor/Data/NetCoreRuntime/shared/Microsoft.NETCore.App/*"


def runtime_dir(unity: Path) -> Path:
    candidates = sorted(unity.glob(RUNTIME_VERSION_GLOB))
    if not candidates:
        raise cc.SetupError(f"no .NET runtime under {unity}/Editor/Data/NetCoreRuntime")
    return candidates[-1]


def framework_refs(unity: Path):
    rt = runtime_dir(unity)
    return rt, [p for p in rt.glob("*.dll") if p.name.startswith(("System.", "Microsoft.CSharp", "mscorlib", "netstandard"))
                and "Native" not in p.name]


def csc(unity: Path, out: Path, sources, refs, target="library", defines=()):
    rsp = out.with_suffix(".rsp")
    lines = [f"-target:{target}", "-nologo", "-noconfig", "-nostdlib", "-langversion:9.0", "-optimize+", "-nowarn:1701,1702",
             f'-out:"{out.as_posix()}"']
    lines += [f"-define:{d}" for d in defines]
    lines += [f'-r:"{Path(r).as_posix()}"' for r in refs]
    lines += [f'"{Path(src).as_posix()}"' for src in sources]
    rsp.write_text("\n".join(lines) + "\n", encoding="utf-8")
    code, output = cc.run_csc(unity, rsp)
    errors = [l for l in output.splitlines() if "error CS" in l]
    for l in errors[:40]:
        print(l)
    if code != 0:
        if not errors:
            print(output[-3000:])
        raise cc.SetupError(f"could not build {out.name}")


def build(out_dir: Path):
    """Core (Unity's compile settings) + NUnit shim + tests + runner, all for the editor's .NET runtime."""
    targets, project, guid_to_name = cc.plan(["AutoChess.Core"])
    ok, built = cc.build(targets, project, guid_to_name, quiet=True)
    if not ok:
        raise cc.SetupError("AutoChess.Core does not compile")
    unity = cc.unity_editor_root()
    rt, refs = framework_refs(unity)
    core = out_dir / "AutoChess.Core.dll"
    shutil.copy2(built["AutoChess.Core"], core)

    shim = out_dir / "nunit.framework.dll"
    csc(unity, shim, [HERE / "NUnitShim.cs"], refs)

    tests_asm = project.get("AutoChess.Core.Tests")
    if tests_asm is None:
        raise cc.SetupError("AutoChess.Core.Tests.asmdef not found")
    tests = out_dir / "AutoChess.Core.Tests.dll"
    csc(unity, tests, tests_asm.sources, refs + [core, shim], defines=("UNITY_INCLUDE_TESTS",))

    runner = out_dir / "Runner.dll"
    csc(unity, runner, [HERE / "Runner.cs"], refs, target="exe")
    (out_dir / "Runner.runtimeconfig.json").write_text(json.dumps({
        "runtimeOptions": {"tfm": "net6.0", "framework": {"name": "Microsoft.NETCore.App", "version": rt.name},
                           "rollForward": "LatestMinor"}
    }, indent=2), encoding="utf-8")
    return unity, runner, tests


def main(argv=None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)
    passthrough = []
    for flag in ("--filter", "--json"):
        if flag in argv:
            i = argv.index(flag)
            value = argv[i + 1]
            if flag == "--json":
                value = str(Path(value).resolve())
            passthrough += [flag, value]
    out_dir = cc.BUILD_DIR / "core_tests"
    out_dir.mkdir(parents=True, exist_ok=True)
    try:
        unity, runner, tests = build(out_dir)
    except cc.SetupError as e:
        print(f"[core_tests] {e}")
        return 2
    dotnet = unity / "Editor" / "Data" / "NetCoreRuntime" / "dotnet.exe"
    proc = subprocess.run([str(dotnet), str(runner), str(tests), *passthrough], cwd=str(ROOT))
    return 0 if proc.returncode == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
