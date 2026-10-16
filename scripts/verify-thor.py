#!/usr/bin/env python3
"""Drive the installed preview through Android input and read actual engine state."""
import argparse
import json
from pathlib import Path
import re
import shutil
import subprocess
import time

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = "org.romanovsthor.app"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", default=shutil.which("adb") or "adb")
    parser.add_argument("--device", required=True)
    parser.add_argument("--bottom-display", type=int, default=4)
    parser.add_argument("--check-graphics", action="store_true", help="Also compare battlefield pixels across Home/return (requires Pillow)")
    parser.add_argument("--start-lobby", action="store_true", help="Start an Easy-AI skirmish from the two-slot lobby")
    args = parser.parse_args()
    prefix = [args.adb, "-s", args.device]
    report = []
    output = ROOT / "artifacts"
    output.mkdir(exist_ok=True)

    def adb(*command):
        if command[:3] in (("shell", "input", "keyevent"), ("shell", "input", "keycombination")):
            command = (*command[:2], "-d", "0", *command[2:])
        return subprocess.check_output([*prefix, *command], encoding="utf-8", errors="replace")

    def state():
        return json.loads(adb("shell", "cat", f"/sdcard/Android/data/{PACKAGE}/files/thor-state.json"))

    def wait(label, predicate, seconds=12):
        deadline = time.monotonic() + seconds
        last = None
        while time.monotonic() < deadline:
            try:
                last = state()
                if predicate(last):
                    report.append({"check": label, "tick": last["Tick"]})
                    print("PASS", label, flush=True)
                    return last
            except json.JSONDecodeError:
                pass
            time.sleep(.12)
        raise AssertionError(label + ": " + json.dumps(last))

    def tap(x, y, display=0, duration=150):
        if display == 4:
            display = args.bottom_display
        adb("shell", "input", "-d", str(display), "swipe", str(x), str(y), str(x), str(y), str(duration))

    def battlefield_picture(name):
        from PIL import Image
        displays = re.findall(r"Display (\d+)", adb("shell", "dumpsys", "SurfaceFlinger", "--display-id"))
        path = output / name
        path.write_bytes(subprocess.check_output([*prefix, "exec-out", "screencap", "-p", "-d", displays[0]]))
        # Centre battlefield only: exclude sidebar, minimap and UI text, which
        # can keep drawing even when indexed world textures are broken.
        image = Image.open(path).convert("RGB").crop((400, 200, 1500, 850))
        pixels = list(image.get_flattened_data() if hasattr(image, "get_flattened_data") else image.getdata())
        return sum(max(p) > 32 and max(p) - min(p) > 15 for p in pixels) / len(pixels)

    if args.start_lobby:
        time.sleep(1)
        tap(659, 326)
        time.sleep(.3)
        tap(537, 447)
        time.sleep(.3)
        tap(1240, 847)
    game = wait("live skirmish", lambda s: s["Battle"] and s["Tick"] > 10, 45)
    assert adb("shell", "pidof", PACKAGE).strip()
    first_tick = game["Tick"]
    wait("simulation advances", lambda s: s["Tick"] > first_tick + 4)
    adb("shell", "input", "keyevent", "96")
    selected = wait("A selects the MCV", lambda s: any(("mcv" in a["Type"] or a["Type"] == "pcv") for a in s["Selected"]))
    unit = next(a for a in selected["Selected"] if ("mcv" in a["Type"] or a["Type"] == "pcv"))
    old_camera = (selected["CameraX"], selected["CameraY"])
    try:
        adb("shell", "input", "joystick", "motionevent", "MOVE", "-0.65", "0")
        time.sleep(.4)
    finally:
        adb("shell", "input", "joystick", "motionevent", "MOVE", "0", "0")
    wait("left stick input pans the camera", lambda s: (s["CameraX"], s["CameraY"]) != old_camera)
    adb("shell", "input", "keyevent", "96")
    wait("A orders the selected MCV to move", lambda s: any(a["Id"] == unit["Id"] and (a["X"], a["Y"]) != (unit["X"], unit["Y"]) for a in s["Selected"]))
    adb("shell", "input", "keyevent", "100")
    adb("shell", "input", "keyevent", "106")
    tap(650, 355, 4)
    yard = wait("bottom-screen deploy creates a yard", lambda s: any("cnst" in a["Type"] for a in s["Selected"]))
    assert yard["Production"], "Construction Yard has no production choices"
    tap(90, 940, 4, 850)
    wait("hold group assigns current selection", lambda s: s["Groups"][0] > 0)
    adb("shell", "input", "keyevent", "97")
    wait("B deselects", lambda s: not s["Selected"])
    tap(90, 940, 4)
    wait("bottom group recalls the yard", lambda s: any("cnst" in a["Type"] for a in s["Selected"]))
    adb("shell", "input", "keyevent", "103")
    wait("RB opens map controls", lambda s: s["MapMode"])
    adb("shell", "input", "keyevent", "97")
    wait("B exits map controls", lambda s: not s["MapMode"])
    production = state()["Production"]
    index = next(i for i, entry in enumerate(production) if re.search(r":(?:ga|na|ya)powr$", entry["Id"]))
    target = production[index]
    tap(620, 244, 4)
    for _ in range(index // 8): tap(1120, 840, 4)
    item = index % 8
    x, y = 710 + (item % 2) * 340, 390 + (item // 2) * 112
    tap(x, y, 4)
    wait("bottom button queues a power plant", lambda s: any(e["Id"] == target["Id"] and e["Queued"] > 0 for e in s["Production"]))
    tap(x, y, 4, 850)
    wait("hold production cancels the queue", lambda s: any(e["Id"] == target["Id"] and e["Queued"] == 0 for e in s["Production"]))
    tap(x, y, 4)
    wait("power plant finishes construction", lambda s: any(e["Id"] == target["Id"] and e["Ready"] for e in s["Production"]), 50)
    tap(x, y, 4)
    wait("READY opens building placement", lambda s: s["Targeting"] == "PlaceBuildingOrderGenerator")
    adb("shell", "input", "keyevent", "97")
    wait("B cancels placement without losing the ready building", lambda s: s["Targeting"] == "UnitOrderGenerator" and any(e["Id"] == target["Id"] and e["Ready"] for e in s["Production"]))
    tap(90, 940, 4)
    adb("shell", "input", "keyevent", "106")
    tap(x, y, 4)
    power_type = target["Id"].rsplit(":", 1)[1]
    for px, py in ((1230, 690), (1230, 540), (700, 690), (960, 780), (700, 540)):
        tap(px, py, 0)
        time.sleep(1.1)
        if state()["Owned"].get(power_type, 0):
            break
    wait("placing a ready building creates the power plant", lambda s: s["Owned"].get(power_type, 0) > 0)
    tap(220, 244, 4)
    before = state()
    tap(150, 430, 4)
    first = wait("bottom minimap tap changes camera", lambda s: (s["CameraX"], s["CameraY"]) != (before["CameraX"], before["CameraY"]))
    adb("shell", "input", "-d", str(args.bottom_display), "swipe", "150", "430", "410", "610", "900")
    wait("bottom minimap drag pans camera", lambda s: (s["CameraX"], s["CameraY"]) != (first["CameraX"], first["CameraY"]))
    for key, name in ((105, "Commands"), (102, "Groups")):
        press = subprocess.Popen([*prefix, "shell", "input", "keyevent", "--longpress", str(key)], stdout=subprocess.DEVNULL)
        try:
            wait(name + " wheel opens", lambda s: s["Wheel"] == name, 3)
        finally:
            press.wait(timeout=5)
        wait(name + " wheel releases", lambda s: s["Wheel"] == "", 3)
    tap(90, 940, 4)
    adb("shell", "input", "keyevent", "106")
    time.sleep(.4)
    if args.check_graphics:
        before_picture = battlefield_picture("before-return.png")
        assert before_picture > .15, "Starting battlefield is not visibly rendered"
    pid = adb("shell", "pidof", PACKAGE).strip()
    adb("shell", "input", "keyevent", "3")
    time.sleep(1)
    adb("shell", "am", "start", "-n", PACKAGE + "/crc6408daf6e3312f3722.MainActivity")
    resumed = wait("return keeps the running match", lambda s: s["Battle"] and s["Tick"] > first_tick)
    assert adb("shell", "pidof", PACKAGE).strip() == pid, "Returning restarted the process"
    assert not resumed["Dragging"] and not resumed["Wheel"], "Held input remained active"
    adb("shell", "input", "keyevent", "106")
    tick = state()["Tick"]
    wait("resumed simulation continues", lambda s: s["Tick"] > tick + 3)
    if args.check_graphics:
        after_picture = battlefield_picture("after-return.png")
        assert after_picture > before_picture * .8, f"Battlefield vanished on return: {before_picture:.3f} -> {after_picture:.3f}"
        report.append({"check": "battlefield remains visible after return", "before": before_picture, "after": after_picture})
        print("PASS battlefield remains visible after return", flush=True)
    assert adb("shell", "pidof", PACKAGE).strip()
    (output / "verification.json").write_text(json.dumps({"checks": report, "state": state()}, indent=2))
    displays = re.findall(r"Display (\d+)", adb("shell", "dumpsys", "SurfaceFlinger", "--display-id"))
    for n, display in enumerate(displays):
        (output / f"verified-screen-{n}.png").write_bytes(subprocess.check_output([*prefix, "exec-out", "screencap", "-p", "-d", display]))
    print("Verified installed preview; report:", output / "verification.json")


if __name__ == "__main__":
    main()
