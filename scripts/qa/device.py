"""Read real engine diagnostics and drive Android input on the connected test Thor."""
import json
from pathlib import Path
import re
import subprocess
import time

ROOT = Path(__file__).resolve().parents[2]
PACKAGE = "org.romanovsthor.app"


class Device:
    def __init__(self, adb, serial):
        self.prefix = [adb, "-s", serial]
        self.checks = []

    def adb(self, *args):
        return subprocess.check_output([*self.prefix, *map(str, args)], encoding="utf-8", errors="replace")

    def state(self):
        return json.loads(self.adb("shell", "cat", f"/sdcard/Android/data/{PACKAGE}/files/thor-state.json"))

    def wait(self, label, predicate, seconds=15):
        deadline = time.monotonic() + seconds
        last = None
        while time.monotonic() < deadline:
            try:
                last = self.state()
                if predicate(last):
                    self.checks.append({"check": label, "tick": last["Tick"]})
                    print("PASS", label, flush=True)
                    return last
            except json.JSONDecodeError:
                pass
            time.sleep(.1)
        raise AssertionError(label + ": " + json.dumps(last))

    def tap(self, x, y, display=0, duration=120):
        self.adb("shell", "input", "-d", display, "swipe", x, y, x, y, duration)

    def key(self, key):
        # The Thor advertises its D-pad as joystick hat axes. Test that path
        # directly instead of relying on Android's fallback keyboard events.
        hats = {19: (0, -1), 20: (0, 1), 21: (-1, 0), 22: (1, 0)}
        if key in hats:
            self.probe("hat", *hats[key], 120)
        else:
            self.probe("keys", key, 120)

    def button(self, label):
        def find(s): return next((n for n in s.get("Navigation", []) if n["Label"].lower() == label.lower()), None)
        state = self.wait("native button available: " + label, find, 45)
        target = find(state)
        self.tap(round(target["X"] * 1920 / state["Width"]), round(target["Y"] * 1080 / state["Height"]))

    def probe(self, *args, background=False):
        command = [*self.prefix, "shell", "env", "CLASSPATH=/data/local/tmp/romanovs-thor-input-probe.jar",
                   "app_process", "/system/bin", "org.romanovsthor.qa.ThorInputProbe", *map(str, args)]
        if background:
            process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
            if process.stdout.readline().strip() != "DOWN":
                raise RuntimeError(process.communicate()[1])
            return process
        subprocess.run(command, check=True, capture_output=True, text=True)

    def capture(self, name, bottom=False):
        ids = re.findall(r"Display (\d+)", self.adb("shell", "dumpsys", "SurfaceFlinger", "--display-id"))
        target = ROOT / "artifacts" / name
        target.parent.mkdir(exist_ok=True)
        target.write_bytes(subprocess.check_output([*self.prefix, "exec-out", "screencap", "-p", "-d", ids[1 if bottom else 0]]))
        return target
