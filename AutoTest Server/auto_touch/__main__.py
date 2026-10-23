import sys

if sys.version_info < (3, 10):
    sys.exit("auto_touch needs Python 3.10+ - run it with the `py` launcher")

if __package__ in (None, ""):
    # Launched as a plain script path - bootstrap the package from our own location.
    import pathlib
    sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))
    from auto_touch import cli
else:
    from . import cli

sys.exit(cli.main())
