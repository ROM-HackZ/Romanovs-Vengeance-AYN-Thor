#!/usr/bin/env python3
"""Wrap the upstream PNG artwork in Windows/macOS icon containers without changing pixels."""
from pathlib import Path
import shutil
import struct

root = Path(__file__).resolve().parents[1]
art = root / "packaging/artwork"
out = root / "installer"
png = (art / "icon_256x256.png").read_bytes()
(out / "icon.ico").write_bytes(struct.pack("<HHH", 0, 1, 1) +
                              struct.pack("<BBBBHHII", 0, 0, 0, 0, 1, 32, len(png), 22) + png)
png = (art / "icon_512x512.png").read_bytes()
chunk = b"ic09" + struct.pack(">I", len(png) + 8) + png
(out / "icon.icns").write_bytes(b"icns" + struct.pack(">I", len(chunk) + 8) + chunk)
shutil.copyfile(art / "icon_256x256.png", out / "icon.png")
