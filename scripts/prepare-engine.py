#!/usr/bin/env python3
"""Fetch the pinned engine and apply the Thor platform patch, without retail data."""
from pathlib import Path
import json
import subprocess

ROOT = Path(__file__).resolve().parents[1]
ENGINE = ROOT / "engine"
SOURCE = json.loads((ROOT / "upstream.json").read_text(encoding="utf-8"))["engine"]
REVISION = SOURCE["revision"]


def git(*args, capture=False):
    result = subprocess.run(["git", "-C", str(ENGINE), *args], check=True,
                            capture_output=capture, text=True)
    return result.stdout.strip() if capture else None


def main():
    if not ENGINE.exists():
        subprocess.run(["git", "-c", "credential.helper=", "clone", "--depth", "1",
                        f"https://github.com/{SOURCE['repository']}.git", str(ENGINE)], check=True)
        git("fetch", "--depth", "1", "origin", REVISION)
        git("checkout", "--detach", REVISION)
    if git("rev-parse", "HEAD", capture=True) != REVISION:
        raise SystemExit("Engine revision differs from the pinned Thor build; refusing to overwrite it.")
    patch = ROOT / "patches" / "thor-platform.patch"
    if patch.exists():
        reverse = subprocess.run(["git", "-C", str(ENGINE), "apply", "--reverse", "--check", str(patch)],
                                 capture_output=True)
        if reverse.returncode:
            git("apply", "--check", str(patch))
            git("apply", str(patch))
    (ENGINE / "VERSION").write_text(REVISION + "\n", encoding="utf-8")
    print("Engine ready:", REVISION)


if __name__ == "__main__":
    main()
