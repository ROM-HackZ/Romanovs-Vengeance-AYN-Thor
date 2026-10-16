"""Check commander choices and global training on a disposable French skirmish.

Default: load a French base with Aircraft Training unlocked and a Barracks.
--purchase: start a fresh French match with an unspent commander point instead.
The ADB input probe must be present. These tests spend points or train units.
"""
import argparse
import json
import time
from device import Device, ROOT


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", required=True)
    parser.add_argument("--device", required=True)
    parser.add_argument("--purchase", action="store_true")
    args = parser.parse_args()
    d = Device(args.adb, args.device)
    d.adb("shell", "input", "keyevent", "224")
    d.adb("shell", "wm", "dismiss-keyguard")
    frame = d.state()["RenderFrame"]
    d.wait("game is rendering live diagnostic state", lambda s: s["RenderFrame"] > frame)
    upgrade = "commanders_power.harrier_training"
    d.wait("French match ready", lambda s: s["Battle"] and s["Faction"] == "France")
    if d.state()["InMenu"]:
        d.button("Resume")
    d.wait("native menu closed", lambda s: not s["InMenu"])

    def item(s, name, catalog="Powers"):
        return next((e for e in s[catalog] if e["Id"].endswith(":" + name)), None)

    def choose(name, catalog="Powers", duration=120):
        # Switching away first resets pagination, even when this tab was active.
        tab = 2 if catalog == "Powers" else 1
        d.tap(220, 244, 4)
        d.wait("commands tab resets catalog page", lambda s: s["DeckTab"] == 0)
        d.tap(1040 if tab == 2 else 620, 244, 4)
        s = d.wait("catalog tab active", lambda s: s["DeckTab"] == tab)
        index = next(i for i, e in enumerate(s[catalog]) if e["Id"].endswith(":" + name))
        for _ in range(index // 8):
            d.tap(1120, 840, 4)
            time.sleep(.15)
        slot = index % 8
        d.tap(710 + slot % 2 * 340, 390 + slot // 2 * 112, 4, duration)

    if args.purchase:
        before = d.wait("commander point available", lambda s: s["CommanderPoints"] == 1 and item(s, upgrade)["Enabled"])
        choices = {e["Id"] for e in before["Powers"]}
        choose(upgrade, duration=850)
        d.wait("holding a choice explains it without buying", lambda s: "Makes Harriers" in s["Feedback"] and s["CommanderPoints"] == 1 and upgrade not in s["Owned"])
        choose(upgrade)
        after = d.wait("purchase spends one point and keeps upgrade visible", lambda s: s["CommanderPoints"] == 0 and item(s, upgrade) and item(s, upgrade)["Unlocked"])
        assert choices == {e["Id"] for e in after["Powers"]}, "Spending a point removed choices"
        d.checks.append({"check": "spending last point retains entire commander catalog"})

    s = d.wait("unlocked training remains at zero points", lambda s: s["CommanderPoints"] == 0 and item(s, upgrade) and item(s, upgrade)["Unlocked"])
    assert not item(s, upgrade)["Enabled"] and not item(s, upgrade)["CanCancel"]
    assert any("rank 3" in e["Detail"] for e in s["Powers"])
    locked = item(s, "commanders_power.gun_turret")
    assert not locked["Enabled"] and "commander point" in locked["Detail"]
    choose("commanders_power.gun_turret")
    d.wait("unavailable choice explains missing point without purchasing", lambda s: "Requires: commander point" in s["Feedback"] and "commanders_power.gun_turret" not in s["Owned"] and s["CommanderPoints"] == 0)
    choose(upgrade)
    d.wait("unlocked choice explains effect and cannot be bought twice", lambda s: "UNLOCKED" in s["Feedback"] and "Makes Harriers" in s["Feedback"] and s["Owned"].get(upgrade) == 1)
    d.capture("catalog-powers-purchased.png" if args.purchase else "catalog-powers-restored.png", True)

    held = d.probe("keys", 105, 12000, background=True)
    try:
        s = d.wait("RT opens command wheel", lambda s: s["Wheel"] == "Commands")
        found = False
        for _ in range(s["WheelPages"]):
            s = d.state()
            if any(e["Id"].endswith(":" + upgrade) and e["Unlocked"] for e in s["WheelEntries"]):
                found = True
                break
            old_page = s["Page"]
            d.key(22)
            d.wait("D-pad changes native wheel page", lambda s: s["Page"] != old_page)
        assert found, "Wheel lost the unlocked upgrade"
        d.capture("catalog-unlocked-wheel.png")
        d.checks.append({"check": "wheel retains unlocked commander choices"})
    finally:
        held.wait(timeout=14)
    d.wait("RT release closes wheel", lambda s: s["Wheel"] == "")

    if not args.purchase:
        for selected in (False, True):
            d.key(21 if selected else 97)
            d.wait("soldier selected" if selected else "nothing selected", lambda s: bool(s["Selected"]) == selected and (not selected or s["Selected"][0]["Type"] == "e1"))
            before = d.state()
            assert item(before, "e1", "Production")["Enabled"], "Training depends on selection"
            ids = [a["Id"] for a in before["Selected"]]
            count = before["Owned"].get("e1", 0)
            choose("e1", "Production")
            after = d.wait("GI trained with soldier selected" if selected else "GI trained with nothing selected", lambda s: s["Owned"].get("e1", 0) > count, 30)
            assert [a["Id"] for a in after["Selected"]] == ids, "Training changed selection"
        d.capture("catalog-train-with-unit-selected.png", True)

    report = "catalog-purchase-verification.json" if args.purchase else "catalog-restored-verification.json"
    (ROOT / "artifacts" / report).write_text(json.dumps({"checks": d.checks, "state": d.state()}, indent=2), encoding="utf-8")
    d.key(108)
    d.wait("test match left paused", lambda s: s["InMenu"] and s["Paused"])
    print("PASS catalog verification complete", flush=True)


if __name__ == "__main__":
    main()
