#!/usr/bin/env python3
"""Record the local APK identity and check that retail archives stay outside it."""
from pathlib import Path
import hashlib
import json
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    apk = ROOT / "artifacts/Romanovs-Thor.apk"
    with zipfile.ZipFile(apk) as archive:
        names = archive.namelist()
        retail = {"ra2.mix", "ra2md.mix", "language.mix", "langmd.mix", "theme.mix", "thememd.mix"}
        if any(Path(name).name.lower() in retail for name in names):
            raise SystemExit("Retail archive found in APK")
        if "assets/openra/global mix database.dat" not in names:
            raise SystemExit("APK is missing the archive filename index")
    project = ET.parse(ROOT / "android/OpenRA.Android/OpenRA.Android.csproj").getroot()
    hasher = hashlib.sha256()
    with apk.open("rb") as stream:
        while block := stream.read(1024 * 1024):
            hasher.update(block)
    digest = hasher.hexdigest()
    upstream = json.loads((ROOT / "upstream.json").read_text(encoding="utf-8"))
    info = {
        "version": project.findtext(".//ApplicationDisplayVersion"),
        "apk": apk.name, "sha256": digest, "bytes": apk.stat().st_size,
        "rv_base": upstream["rv"]["revision"],
        "engine_base": upstream["engine"]["revision"],
        "android_platform_base": upstream["android"]["revision"],
        "integration_sources": {
            path.relative_to(ROOT).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
            for folder in ("android", "patches") for path in sorted((ROOT / folder).rglob("*"))
            if path.is_file() and path.suffix in {".cs", ".csproj", ".props", ".patch"}
            and not {"bin", "obj", "src", "build"}.intersection(path.relative_to(ROOT).parts)
        }
    }
    (ROOT / "artifacts/build-info.json").write_text(json.dumps(info, indent=2) + "\n", encoding="utf-8")
    print("APK SHA-256:", digest)


if __name__ == "__main__":
    main()
