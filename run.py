#!/usr/bin/env python
"""Build (optionally) and launch the Open.DataGrid demo application.

Usage:
    python run.py                 # build Debug, then run
    python run.py -c Release      # build Release, then run
    python run.py --no-build      # run the existing build only
    python run.py --wait          # wait until the demo window is closed
"""
import argparse
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
DEMO_PROJECT = ROOT / "src" / "Open.DataGrid.Demo" / "Open.DataGrid.Demo.csproj"
TARGET_FRAMEWORK = "net462"
EXE_NAME = "Open.DataGrid.Demo.exe"


def build(config: str) -> None:
    print(f"=== Build {DEMO_PROJECT.name} ({config}) ===", flush=True)
    result = subprocess.run(
        ["dotnet", "build", str(DEMO_PROJECT), "-c", config, "-nologo", "-v:minimal"],
        cwd=ROOT,
    )
    if result.returncode != 0:
        sys.exit(f"Build failed (exit code {result.returncode}).")


def main() -> None:
    parser = argparse.ArgumentParser(description="Run the Open.DataGrid demo application.")
    parser.add_argument("-c", "--config", default="Debug", choices=["Debug", "Release"],
                        help="build configuration (default: Debug)")
    parser.add_argument("--no-build", action="store_true", help="skip building")
    parser.add_argument("--wait", action="store_true", help="wait for the demo to exit")
    args = parser.parse_args()

    if not args.no_build:
        build(args.config)

    exe = DEMO_PROJECT.parent / "bin" / args.config / TARGET_FRAMEWORK / EXE_NAME
    if not exe.is_file():
        sys.exit(f"Demo executable not found: {exe}\nBuild it first (run without --no-build).")

    print(f"=== Run {exe.relative_to(ROOT)} ===", flush=True)
    proc = subprocess.Popen([str(exe)], cwd=exe.parent)
    if args.wait:
        sys.exit(proc.wait())


if __name__ == "__main__":
    main()
