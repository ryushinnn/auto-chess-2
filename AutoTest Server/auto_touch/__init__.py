"""auto_touch - Python side of the auto-chess-2 AutoTest rig.

Drives the Unity Editor bridge (Assets/AutoTest/Editor, HTTP on 127.0.0.1:8766,
contract in ../PROTOCOL.md) from a CLI, a scenario runner and an MCP server.
Stdlib only, Python 3.10+. Run from `AutoTest Server/` as `py -m auto_touch ...`.
"""

__version__ = "1.0.0"

# Exit codes (PROTOCOL.md section 4). Every entry point returns one of these.
EXIT_OK = 0
EXIT_UNREACHABLE = 2
EXIT_INVALID = 3
EXIT_FAIL = 4
EXIT_COMPILE = 5
EXIT_NEEDS_COMPILE = 10

EXIT_MEANING = {
    EXIT_OK: "ok / PASS",
    EXIT_UNREACHABLE: "server unreachable",
    EXIT_INVALID: "invalid input (scenario file, arguments)",
    EXIT_FAIL: "FAIL (assertion / step / red logs / invariant)",
    EXIT_COMPILE: "Unity compile errors",
    EXIT_NEEDS_COMPILE: "installed, Unity must compile",
}

# When several results are combined (matrix cases, campaign rows), the most
# actionable code wins: infrastructure first, then compile, input, test failure.
_PRIORITY = (EXIT_UNREACHABLE, EXIT_COMPILE, EXIT_INVALID, EXIT_FAIL, EXIT_NEEDS_COMPILE, EXIT_OK)


def worst_exit(codes) -> int:
    """Combine exit codes: the first of _PRIORITY that occurs (0 for none)."""
    seen = set(codes)
    for code in _PRIORITY:
        if code in seen:
            return code
    return max(seen) if seen else EXIT_OK
