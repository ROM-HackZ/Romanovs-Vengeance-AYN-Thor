"""Exercise version detection, notification deduplication and retry recovery."""
import base64
import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("upstream", Path(__file__).resolve().parents[1] / "check-upstream.py")
upstream = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(upstream)
PINS = {
    "rv": {"repository": "game/rv", "branch": "master", "revision": "a" * 40},
    "engine": {"repository": "game/engine", "revision": "b" * 40},
    "android": {"repository": "game/android", "branch": "master", "revision": "c" * 40},
    "openra": {"repository": "game/openra", "release": "stable", "playtest": "test"},
}


def fixture(path, method="GET", payload=None):
    if path == "repos/game/rv/commits/master":
        return {"sha": "a" * 40}
    if path == "repos/game/android/commits/master":
        return {"sha": "c" * 40}
    if "/contents/mod.config?" in path:
        return {"content": base64.b64encode(('ENGINE_VERSION="' + "b" * 40 + '"\n').encode()).decode()}
    if "/releases?" in path:
        return [{"tag_name": tag, "draft": False, "prerelease": pre, "published_at": date} for tag, pre, date in
                [("test", True, "2026-10-11"), ("old-test", True, "2026-02-22"), ("stable", False, "2025-03-30")]]
    raise AssertionError("Unexpected request: " + path)


class UpstreamChecks(unittest.TestCase):
    def test_matching_versions_have_no_alert(self):
        with patch.object(upstream, "github", side_effect=fixture):
            self.assertEqual(upstream.changes_since(PINS), [])

    def test_rv_engine_requirement_is_used(self):
        def changed(path, *args):
            if "/contents/mod.config?" in path:
                return {"content": base64.b64encode(('ENGINE_VERSION="' + "d" * 40 + '"\n').encode()).decode()}
            return fixture(path, *args)
        with patch.object(upstream, "github", side_effect=changed):
            changes = upstream.changes_since(PINS)
        self.assertEqual(changes[0][0], "Engine required by RV")
        self.assertIn("game/engine/compare/", changes[0][3])

    def test_incomplete_upstream_data_cannot_report_success(self):
        def broken(path, *args):
            if "/contents/mod.config?" in path:
                return {"content": base64.b64encode(b"unknown engine format").decode()}
            return fixture(path, *args)
        with patch.object(upstream, "github", side_effect=broken):
            with self.assertRaises(ValueError):
                upstream.changes_since(PINS)

    def test_repeated_report_does_not_notify_again(self):
        with patch.object(upstream, "pages", return_value=iter([{"number": 1, "title": upstream.TITLE, "body": "same"}])), patch.object(upstream, "github") as api:
            upstream.notify("owner/repo", "same", True)
            api.assert_not_called()

    def test_changed_report_comments_once_even_after_retry(self):
        issue = {"number": 1, "title": upstream.TITLE, "body": "old"}
        comments = []
        def listed(path):
            return iter(comments if "/comments" in path else [issue])
        calls = []
        def api(path, method, payload):
            calls.append((path, method, payload))
            if method == "POST":
                comments.append(payload)
            elif len(calls) == 2:
                raise OSError("Interrupted after the comment was created")
        with patch.object(upstream, "pages", side_effect=listed), patch.object(upstream, "github", side_effect=api):
            with self.assertRaises(OSError):
                upstream.notify("owner/repo", "new", True)
            upstream.notify("owner/repo", "new", True)
        self.assertEqual(sum(method == "POST" for _, method, _ in calls), 1)
        self.assertEqual(calls[-1][2], {"body": "new", "state": "open"})

    def test_integrated_updates_close_the_tracking_issue(self):
        issue = {"number": 1, "title": upstream.TITLE, "body": "old"}
        with patch.object(upstream, "pages", side_effect=lambda path: iter([] if "/comments" in path else [issue])), patch.object(upstream, "github") as api:
            upstream.notify("owner/repo", "All current", False)
        self.assertEqual(api.call_args.args[2]["state"], "closed")


if __name__ == "__main__":
    unittest.main()
