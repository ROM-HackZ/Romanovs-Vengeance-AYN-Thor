#!/usr/bin/env python3
"""Build on Windows/WSL without traversing OneDrive through WSL's per-file bridge."""
from pathlib import Path, PurePosixPath
import json
import os
import shutil
import subprocess
import sys
import time
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SKIP = {".git", "node_modules", "bin", "obj", "artifacts", "logs", ".local", "__pycache__"}


def inside(root, name):
    target = (root / name).resolve()
    if not target.is_relative_to(root.resolve()):
        raise ValueError("Build input escapes its directory: " + name)
    return target


def unpack(archive, destination):
    destination = Path(destination).resolve()
    destination.mkdir(parents=True, exist_ok=True)
    index = destination / ".thor-build-inputs.json"
    old = json.loads(index.read_text()) if index.exists() else {}
    current = {}
    count = 0
    with zipfile.ZipFile(archive) as z:
        for entry in z.infolist():
            target = inside(destination, entry.filename)
            current[entry.filename] = entry.CRC
            if old.get(entry.filename) == entry.CRC and target.is_file():
                continue
            target.parent.mkdir(parents=True, exist_ok=True)
            with z.open(entry) as source, target.open("wb") as output:
                shutil.copyfileobj(source, output)
            count += 1
    for name in old.keys() - current.keys():
        target = inside(destination, name)
        if target.is_file():
            target.unlink()
    index.write_text(json.dumps(current))
    print(f"Build inputs ready ({count} changed files).", flush=True)


def main():
    if len(sys.argv) > 1 and sys.argv[1] == "unpack":
        unpack(sys.argv[2], sys.argv[3])
        return
    if os.name != "nt":
        subprocess.run(["bash", str(ROOT / "scripts/build-android.sh"), *sys.argv[1:]], check=True)
        return
    start = time.monotonic()
    subprocess.run([sys.executable, str(ROOT / "scripts/prepare-engine.py")], check=True)
    archive = ROOT / ".local/build-inputs.zip"
    archive.parent.mkdir(exist_ok=True)
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=1) as z:
        for directory, folders, files in os.walk(ROOT):
            relative = Path(directory).relative_to(ROOT)
            folders[:] = [name for name in folders if name not in SKIP and name != "artsrc"
                          and (relative / name).as_posix() not in {"android/native/src", "android/native/build"}]
            for name in files:
                p = Path(directory) / name
                if not p.is_symlink() and p.suffix != ".keystore":
                    z.write(p, p.relative_to(ROOT).as_posix())
    distro = os.environ.get("THOR_WSL_DISTRO", "Ubuntu")
    wsl = ["wsl.exe", "-d", distro, "--"]
    home = subprocess.check_output([*wsl, "printenv", "HOME"], text=True).strip()
    build_root = str(PurePosixPath(home) / ".cache/romanovs-thor/build")

    def linux_path(p):
        p = p.resolve()
        return "/mnt/" + p.drive[0].lower() + p.as_posix()[2:]

    subprocess.run([*wsl, "python3", linux_path(Path(__file__)), "unpack", linux_path(archive), build_root], check=True)
    result = subprocess.run([*wsl, "env", "THOR_LINUX_BUILD=1", "bash", build_root + "/scripts/build-android.sh", *sys.argv[1:]])
    (ROOT / "logs").mkdir(exist_ok=True)
    subprocess.run([*wsl, "cp", build_root + "/logs/build-android.log", linux_path(ROOT / "logs/build-android.log")], check=True)
    if result.returncode:
        raise SystemExit(result.returncode)
    (ROOT / "artifacts").mkdir(exist_ok=True)
    subprocess.run([*wsl, "cp", build_root + "/artifacts/Romanovs-Thor.apk", linux_path(ROOT / "artifacts/Romanovs-Thor.apk")], check=True)
    subprocess.run([*wsl, "cp", build_root + "/artifacts/build-info.json", linux_path(ROOT / "artifacts/build-info.json")], check=True)
    print(f"Built {ROOT / 'artifacts/Romanovs-Thor.apk'} in {time.monotonic() - start:.1f}s", flush=True)


if __name__ == "__main__":
    main()
