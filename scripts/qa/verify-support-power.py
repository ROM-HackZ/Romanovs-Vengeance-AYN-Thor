"""Exercise Spy Plane unlock, cancellation and directional controller targeting in a Soviet test match."""
import argparse
import json
import time
from device import Device, ROOT


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", required=True)
    parser.add_argument("--device", required=True)
    args = parser.parse_args()
    d = Device(args.adb, args.device)
    d.key(106)
    d.wait("cursor centred over battlefield", lambda s: abs(s["CursorX"] - s["Width"] / 2) < 3 and abs(s["CursorY"] - s["Height"] / 2) < 3)
    selection = [a["Id"] for a in d.state()["Selected"]]

    def choose(entry_id):
        d.tap(1040, 244, 4)
        s = d.wait("powers tab", lambda s: s["DeckTab"] == 2)
        index = next(i for i, e in enumerate(s["Powers"]) if e["Id"] == entry_id)
        for _ in range(index // 8):
            d.tap(1120, 840, 4)
        slot = index % 8
        d.tap(710 + slot % 2 * 340, 390 + slot // 2 * 112, 4)

    def spy(s):
        return next((e for e in s["Powers"] if e["Category"] == 8 and e["Label"] == "Spy Plane"), None)

    s = d.state()
    if not spy(s):
        upgrade = next(e for e in s["Powers"] if e["Id"].endswith(":commanders_power.spy_plane") and e["Enabled"])
        choose(upgrade["Id"])
    s = d.wait("native upgrade unlocks a ready support power", lambda s: spy(s) and spy(s)["Ready"])
    power_id = spy(s)["Id"]
    d.capture("polished-powers.png", True)
    choose(power_id)
    d.wait("power button enters native directional targeting", lambda s: s["Targeting"] == "SelectDirectionalTarget")
    held = d.probe("keys", 96, 5000, background=True)
    try:
        d.wait("A begins directional target drag", lambda s: s["Dragging"])
        d.key(97)
        d.wait("B cancels held airstrike without spending it", lambda s: not s["Dragging"] and s["Targeting"] == "UnitOrderGenerator" and spy(s)["Ready"])
        assert [a["Id"] for a in d.state()["Selected"]] == selection, "Cancelling airstrike cleared selection"
    finally:
        held.wait(timeout=7)
    choose(power_id)
    d.wait("ready power can be targeted again", lambda s: s["Targeting"] == "SelectDirectionalTarget")
    before = d.state()
    held = d.probe("keys", 96, 5000, background=True)
    try:
        d.wait("direction drag active", lambda s: s["Dragging"])
        d.probe("axes", 1, 0, 0, 0, 0, 0, 500)
        moved = d.wait("left stick chooses airstrike direction", lambda s: s["CursorX"] > before["CursorX"] + 20)
        assert (moved["CameraX"], moved["CameraY"]) == (before["CameraX"], before["CameraY"]), "Direction drag panned the map"
        d.capture("polished-airstrike-direction.png")
    finally:
        held.wait(timeout=7)
    d.wait("releasing A launches and starts native recharge", lambda s: s["Targeting"] == "UnitOrderGenerator" and spy(s) and not spy(s)["Ready"] and spy(s)["Progress"] < 1)
    (ROOT / "artifacts/support-power-verification.json").write_text(json.dumps({"checks": d.checks, "state": d.state()}, indent=2), encoding="utf-8")
    print("PASS support-power verification complete", flush=True)


if __name__ == "__main__":
    main()
