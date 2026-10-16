"""Exercise polish on a disposable live skirmish after verify-thor.py builds a base."""
import argparse
import json
import subprocess
import time
from device import Device, PACKAGE, ROOT


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", required=True)
    parser.add_argument("--device", required=True)
    args = parser.parse_args()
    d = Device(args.adb, args.device)
    d.wait("live base for polish checks", lambda s: s["Battle"] and not s["InMenu"] and any(k.endswith("cnst") for k in s["Owned"]))
    d.tap(82, 948, 4)
    d.key(106)
    d.wait("group recalls construction yard", lambda s: any(a["Type"].endswith("cnst") for a in s["Selected"]))

    # Default-display key input after a real touch on display 4 must reach the app.
    d.tap(620, 244, 4)
    d.wait("build tab opens", lambda s: s["DeckTab"] == 1)
    d.adb("shell", "input", "keyevent", "109")
    d.wait("controller works after touching lower screen", lambda s: s["DeckTab"] == 2)
    d.adb("shell", "input", "keyevent", "109")
    d.wait("Select cycles back to commands", lambda s: s["DeckTab"] == 0)
    s = d.state()
    assert s["Production"] and all(e["Category"] not in (7, 8) for e in s["Production"])
    assert any(e["Icon"] and "$" in e["Detail"] for e in s["Production"])
    assert s["Powers"] and all(e["Category"] in (7, 8) for e in s["Powers"])
    d.checks.append({"check": "native icons, costs, and separate production/powers catalogs"})
    d.tap(620, 244, 4)
    time.sleep(.25)
    d.capture("polished-production.png", True)
    d.tap(220, 244, 4)

    before = d.state()
    d.probe("pan-left")
    moved = d.wait("two-finger drag pans map", lambda s: (s["CameraX"], s["CameraY"]) != (before["CameraX"], before["CameraY"]))
    assert [a["Id"] for a in moved["Selected"]] == [a["Id"] for a in before["Selected"]], "Two-finger pan changed selection"
    d.checks.append({"check": "two-finger pan preserves selection"})
    before_zoom = d.state()["Zoom"]
    d.probe("pinch-out")
    zoomed = d.wait("pinch out zooms in", lambda s: s["Zoom"] > before_zoom + .03)
    d.probe("pinch-in")
    out = d.wait("pinch in zooms out", lambda s: s["Zoom"] < zoomed["Zoom"] - .03)
    d.probe("axes", 0, 0, 0, -1, 0, 0, 550)
    zoomed = d.wait("right stick up zooms in", lambda s: s["Zoom"] > out["Zoom"] + .03)
    d.probe("axes", 0, 0, 0, 1, 0, 0, 550)
    d.wait("right stick down zooms out", lambda s: s["Zoom"] < zoomed["Zoom"] - .03)
    d.probe("two-tap")
    d.wait("two-finger tap deselects", lambda s: not s["Selected"])
    d.tap(82, 948, 4)
    d.key(106)
    d.wait("group restores selection after gesture", lambda s: bool(s["Selected"]))
    d.key(97)
    d.adb("shell", "input", "-d", "0", "swipe", "700", "300", "1400", "800", "1000")
    d.wait("one-finger drag selects through native rectangle", lambda s: any(a["Type"].endswith("cnst") for a in s["Selected"]))

    # RT alone has no default command: A must not accidentally undeploy the yard.
    d.key(103)
    d.wait("map controls ready for wheel transition", lambda s: s["MapMode"])
    held = d.probe("axes", 0, 0, 0, 0, 0, 1, 8000, background=True)
    try:
        s = d.wait("analog RT opens wheel with no accidental highlight", lambda s: s["Wheel"] == "Commands" and s["Highlight"] == -1, 3)
        assert not s["MapMode"], "Opening wheel retained map order mode"
        d.key(103)
        assert not d.state()["MapMode"], "RB enabled map orders under the wheel"
        d.key(96)
        d.wait("wheel needs deliberate stick choice", lambda s: "left stick first" in s["Feedback"], 3)
        d.capture("polished-command-wheel.png")
    finally:
        held.wait(timeout=10)
    d.wait("analog RT releases wheel", lambda s: s["Wheel"] == "")
    before = d.state()
    d.probe("sequence", 1, 0, 0, 0, 0, 1, 500, 1, 0, 0, 0, 0, 0, 800)
    after = d.wait("wheel release restores normal mode", lambda s: s["Wheel"] == "")
    assert abs(after["CameraX"] - before["CameraX"]) < 3 and abs(after["CameraY"] - before["CameraY"]) < 3
    d.checks.append({"check": "deflected wheel stick cannot fling camera on release"})

    held = d.probe("keys", 102, 8000, background=True)
    try:
        d.wait("LB opens groups", lambda s: s["Wheel"] == "Groups", 3)
        d.key(22)
        d.wait("groups 9 and 10 have their own page", lambda s: s["Page"] == 1 and len(s["WheelEntries"]) == 2, 3)
        d.probe("axes", 0, 1, 0, 0, 0, 0, 300)
        d.wait("two-slot wheel matches down to group 10", lambda s: s["Highlight"] == 1, 3)
        d.capture("polished-group-wheel.png")
    finally:
        held.wait(timeout=10)
    d.wait("LB releases", lambda s: s["Wheel"] == "")

    d.key(108)
    menu = d.wait("Start opens native game menu", lambda s: s["InMenu"] and any(n["Label"] == "Resume" for n in s["Navigation"]))
    camera = (menu["CameraX"], menu["CameraY"])
    cursor = (menu["CursorX"], menu["CursorY"])
    d.probe("axes", .7, .2, 0, 0, 0, 0, 400)
    moved = d.wait("menu uses left stick for cursor", lambda s: (s["CursorX"], s["CursorY"]) != cursor)
    assert (moved["CameraX"], moved["CameraY"]) == camera, "Menu input panned battlefield"
    d.checks.append({"check": "menus protect battlefield camera and selection"})
    d.probe("axes", 0, 0, 0, 0, 0, 1, 500)
    assert d.state()["Wheel"] == "", "RT opened gameplay wheel through menu"
    # Use actual native button centres, then press A rather than touching Resume.
    for _ in range(10):
        s = d.state()
        target = next(n for n in s["Navigation"] if n["Label"] == "Resume")
        dx, dy = target["X"] - s["CursorX"], target["Y"] - s["CursorY"]
        if abs(dx) < 3 and abs(dy) < 3:
            break
        d.key(21 if dx < -3 and abs(dx) > abs(dy) else 22 if dx > 3 and abs(dx) > abs(dy) else 19 if dy < 0 else 20)
        time.sleep(.18)
    else:
        raise AssertionError("D-pad could not reach Resume")
    d.checks.append({"check": "D-pad reaches a native menu button"})
    d.key(96)
    d.wait("A activates focused Resume button", lambda s: not s["InMenu"])
    d.key(108)
    d.wait("menu reopens for touch check", lambda s: s["InMenu"])
    d.button("Resume")
    d.wait("first menu touch after a selection drag clicks normally", lambda s: not s["InMenu"])

    pid = d.adb("shell", "pidof", PACKAGE).strip()
    d.adb("shell", "input", "-d", "0", "keyevent", "3")
    paused = d.wait("background pauses solo game", lambda s: s["Paused"])
    # Pause is a synchronized order, and diagnostics update at most once a
    # second. Let the final in-flight tick and its snapshot settle first.
    time.sleep(1.15)
    paused = d.state()
    assert paused["Paused"], "Solo pause did not persist"
    time.sleep(1.3)
    assert d.state()["Tick"] == paused["Tick"], "Solo game advanced after background pause settled"
    d.adb("shell", "am", "start", "-n", PACKAGE + "/crc6408daf6e3312f3722.MainActivity")
    d.wait("return resumes solo game", lambda s: not s["Paused"] and s["Tick"] > paused["Tick"])
    assert d.adb("shell", "pidof", PACKAGE).strip() == pid
    d.checks.append({"check": "solo return keeps app process"})
    d.capture("polished-battlefield.png")
    d.capture("polished-deck.png", True)
    (ROOT / "artifacts/polish-verification.json").write_text(json.dumps({"checks": d.checks, "state": d.state()}, indent=2), encoding="utf-8")
    print("PASS polish verification complete", flush=True)


if __name__ == "__main__":
    main()
