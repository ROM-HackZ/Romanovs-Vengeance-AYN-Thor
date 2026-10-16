#!/usr/bin/env python3
"""Install the Thor APK and import owned retail archives over USB. PC files are read only."""
import argparse
import hashlib
from pathlib import Path
import shutil
import subprocess

PACKAGE = "org.romanovsthor.app"
FILES = ("ra2.mix", "language.mix", "ra2md.mix", "langmd.mix", "theme.mix", "thememd.mix")
ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("game", type=Path, help="Your RA2 + Yuri's Revenge installation folder")
    parser.add_argument("--device", required=True, help="ADB device serial")
    parser.add_argument("--adb", default=shutil.which("adb") or "adb")
    parser.add_argument("--apk", type=Path, default=ROOT / "artifacts/Romanovs-Thor.apk")
    args = parser.parse_args()
    contents = {p.name.lower(): p for p in args.game.iterdir() if p.is_file()}
    missing = [name for name in FILES if name not in contents]
    if missing:
        raise SystemExit("Missing retail archives: " + ", ".join(missing))

    def adb(*command, capture=False):
        result = subprocess.run([args.adb, "-s", args.device, *command], check=True,
                                capture_output=capture, text=True)
        return result.stdout.strip() if capture else None

    adb("install", "-r", str(args.apk))
    destination = f"/sdcard/Android/data/{PACKAGE}/files/support/Content/ra2"
    adb("shell", "mkdir", "-p", destination)
    for name in FILES:
        source = contents[name]
        remote = destination + "/" + name
        hasher = hashlib.sha256()
        with source.open("rb") as stream:
            while block := stream.read(1024 * 1024):
                hasher.update(block)
        digest = hasher.hexdigest()
        existing = subprocess.run([args.adb, "-s", args.device, "shell", "sha256sum", remote],
                                  capture_output=True, text=True)
        if existing.returncode == 0 and existing.stdout.split()[0] == digest:
            print("Already imported:", name, flush=True)
            continue
        print("Importing:", name, flush=True)
        adb("push", str(source), remote + ".partial")
        actual = adb("shell", "sha256sum", remote + ".partial", capture=True).split()[0]
        if actual != digest:
            raise SystemExit("Transfer checksum mismatch: " + name)
        adb("shell", "mv", remote + ".partial", remote)
    adb("shell", "monkey", "-p", PACKAGE, "-c", "android.intent.category.LAUNCHER", "1")
    print("Installed. Open Romanov's Vengeance — Thor.")


if __name__ == "__main__":
    main()
