#!/usr/bin/env python3
"""Download the upstream Lua, FreeType and OpenAL source releases for local builds."""
from pathlib import Path
import hashlib
import tarfile
import urllib.request

ROOT = Path(__file__).resolve().parents[1] / "android" / "native"
SOURCES = {
    "lua-5.1.5": ("https://www.lua.org/ftp/lua-5.1.5.tar.gz", "2640fc56a795f29d28ef15e13c34a47e223960b0240e8cb0a82d9b0738695333"),
    "freetype-2.13.2": ("https://download.savannah.gnu.org/releases/freetype/freetype-2.13.2.tar.xz", "12991c4e55c506dd7f9b765933e62fd2be2e06d421505d7950a132e4f1bb484d"),
    "openal-soft-1.23.1": ("https://github.com/kcat/openal-soft/archive/refs/tags/1.23.1.tar.gz", "dfddf3a1f61059853c625b7bb03de8433b455f2f79f89548cbcbd5edca3d4a4a"),
}

def main():
    source_dir = ROOT / "src"
    source_dir.mkdir(parents=True, exist_ok=True)
    for name, (url, expected) in SOURCES.items():
        if (source_dir / name).is_dir():
            continue
        archive = source_dir / (name + (".tar.xz" if url.endswith(".xz") else ".tar.gz"))
        if not archive.exists():
            print("Downloading", name, flush=True)
            with urllib.request.urlopen(url, timeout=60) as response:
                archive.write_bytes(response.read())
        actual = hashlib.sha256(archive.read_bytes()).hexdigest()
        if actual != expected:
            raise RuntimeError(f"Checksum mismatch for {archive}. Remove the incomplete download and retry.")
        with tarfile.open(archive) as tar:
            for member in tar.getmembers():
                target = (source_dir / member.name).resolve()
                if not target.is_relative_to(source_dir.resolve()):
                    raise RuntimeError("Archive contains an unsafe path")
            tar.extractall(source_dir, filter="data")
        print(name, actual, flush=True)

if __name__ == "__main__":
    main()
