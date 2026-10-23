"""Offline toolkit tests (no Unity needed) - a thin wrapper over unittest discovery.

    py -m auto_touch.run_tests            # same as: py -m unittest discover -s auto_touch/tests -t .
    py -m auto_touch.run_tests -v -k campaign

Exit code 0 when every test passes, 4 otherwise.
"""
from __future__ import annotations

import argparse
import sys
import unittest
from pathlib import Path


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(prog="py -m auto_touch.run_tests", description=__doc__.splitlines()[0])
    ap.add_argument("-v", "--verbose", action="store_true", help="one line per test")
    ap.add_argument("-k", action="append", metavar="PATTERN", help="only tests whose name contains PATTERN")
    args = ap.parse_args(argv)
    here = Path(__file__).resolve().parent
    if str(here.parent) not in sys.path:
        sys.path.insert(0, str(here.parent))
    loader = unittest.TestLoader()
    if args.k:
        loader.testNamePatterns = [f"*{k}*" for k in args.k]
    suite = loader.discover(str(here / "tests"), top_level_dir=str(here.parent))
    result = unittest.TextTestRunner(verbosity=2 if args.verbose else 1).run(suite)
    return 0 if result.wasSuccessful() else 4


if __name__ == "__main__":
    sys.exit(main())
