#!/usr/bin/env python3
"""Check publishable source, dependency pins and release metadata without a game install."""
import ast
import json
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
RETAIL = {"ra2.mix", "language.mix", "ra2md.mix", "langmd.mix", "theme.mix", "thememd.mix"}
BUILD_FILES = {".apk", ".exe", ".dll", ".so", ".dylib", ".pdb", ".keystore", ".jks", ".p12", ".pfx"}
SECRET = re.compile(rb"gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{60,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----")


def main():
    pins = json.loads((ROOT / "upstream.json").read_text(encoding="utf-8"))
    for component in ("rv", "engine", "android"):
        assert re.fullmatch(r"[0-9a-f]{40}", pins[component]["revision"]), component + " has an invalid revision"
    configured_engine = re.search(r'^ENGINE_VERSION="([0-9a-f]+)"$', (ROOT / "mod.config").read_text(encoding="utf-8"), re.M).group(1)
    assert configured_engine == pins["engine"]["revision"], "mod.config and upstream.json disagree about the engine"
    project = ET.parse(ROOT / "android/OpenRA.Android/OpenRA.Android.csproj").getroot()
    release = json.loads((ROOT / "installer/release.json").read_text(encoding="utf-8"))
    assert release["version"] == project.findtext(".//ApplicationDisplayVersion"), "APK and release versions disagree"
    assert not release["sha256"] or re.fullmatch(r"[0-9a-f]{64}", release["sha256"]), "Invalid release checksum"
    package = json.loads((ROOT / "installer/package.json").read_text(encoding="utf-8"))
    assert package["homepage"] == "https://github.com/" + pins["project"], "Installer points to another project"
    names = subprocess.check_output(["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"], cwd=ROOT).decode("utf-8").split("\0")
    checked = 0
    for name in filter(None, names):
        path = ROOT / name
        if not path.is_file():
            continue
        assert path.name.lower() not in RETAIL, "Retail archive in source: " + name
        assert path.suffix.lower() not in BUILD_FILES, "Build output or signing material in source: " + name
        assert path.stat().st_size < 100 * 1024 * 1024, "File exceeds GitHub's source-file limit: " + name
        if path.suffix == ".py":
            ast.parse(path.read_text(encoding="utf-8"), filename=name)
        if path.suffix.lower() in {".py", ".cs", ".js", ".json", ".md", ".yml", ".yaml", ".sh", ".ps1", ".config", ".txt", ".properties"}:
            assert not SECRET.search(path.read_bytes()), "Credential or private key detected in: " + name
        checked += 1
    print(f"Checked {checked} source files; dependency pins and release metadata agree.")


if __name__ == "__main__":
    main()
