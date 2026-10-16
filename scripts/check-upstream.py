#!/usr/bin/env python3
"""Compare reviewed upstream versions; optionally maintain one GitHub tracking issue."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import urllib.error
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
TITLE = "[upstream] Updates to review"


def github(path, method="GET", payload=None):
    headers = {"Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28", "User-Agent": "Romanovs-Thor-upstream-check"}
    if os.environ.get("GH_TOKEN"):
        headers["Authorization"] = "Bearer " + os.environ["GH_TOKEN"]
    data = None if payload is None else json.dumps(payload).encode("utf-8")
    if data is not None:
        headers["Content-Type"] = "application/json"
    request = urllib.request.Request("https://api.github.com/" + path, data=data, headers=headers, method=method)
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)


def pages(path):
    separator = "&" if "?" in path else "?"
    page = 1
    while True:
        items = github(f"{path}{separator}per_page=100&page={page}")
        yield from items
        if len(items) < 100:
            return
        page += 1


def head(component):
    branch = urllib.parse.quote(component["branch"], safe="")
    revision = github(f"repos/{component['repository']}/commits/{branch}")["sha"]
    if not re.fullmatch(r"[0-9a-f]{40}", revision):
        raise ValueError("Upstream returned an invalid commit")
    return revision


def changes_since(pins):
    changes = []

    def commit_change(label, source, latest):
        old = source["revision"]
        if latest != old:
            changes.append((label, old, latest, f"https://github.com/{source['repository']}/compare/{old}...{latest}"))

    rv = head(pins["rv"])
    commit_change("Romanov's Vengeance", pins["rv"], rv)
    # Follow the engine RV actually requires, rather than an unrelated default
    # branch in its maintainer's engine fork.
    config = github(f"repos/{pins['rv']['repository']}/contents/mod.config?ref={rv}")
    text = base64.b64decode(config["content"]).decode("utf-8")
    required = re.search(r'^ENGINE_VERSION="([0-9a-f]{40})"$', text, re.M)
    if not required:
        raise ValueError("RV's engine requirement could not be read; review its mod.config")
    commit_change("Engine required by RV", pins["engine"], required.group(1))
    commit_change("Android platform base", pins["android"], head(pins["android"]))
    releases = [r for r in github(f"repos/{pins['openra']['repository']}/releases?per_page=30") if not r["draft"]]
    for key, prerelease in (("release", False), ("playtest", True)):
        candidates = [r for r in releases if r["prerelease"] == prerelease]
        if not candidates:
            raise ValueError("OpenRA release information is incomplete")
        latest = max(candidates, key=lambda r: r["published_at"] or r["created_at"])
        if latest["tag_name"] != pins["openra"][key]:
            tag = latest["tag_name"]
            changes.append(("OpenRA " + key, pins["openra"][key], tag,
                            f"https://github.com/{pins['openra']['repository']}/releases/tag/{urllib.parse.quote(tag, safe='')}"))
    return changes


def render(changes):
    if not changes:
        return "All watched upstream versions match `upstream.json`.\n"
    lines = ["Upstream changes are available for review. The Thor build stays pinned until we integrate and test them.", "", "| Project | Reviewed version | Available version |", "| --- | --- | --- |"]
    for label, old, new, url in changes:
        lines.append(f"| {label} | `{old}` | [{new}]({url}) |")
    lines += ["", "RV's engine requirement is the compatibility target. Main OpenRA releases are tracked for awareness.",
              "", "Follow [the update procedure](docs/UPDATING.md) before publishing a Thor update."]
    return "\n".join(lines) + "\n"


def notify(project, body, changed):
    issue = next((i for i in pages(f"repos/{project}/issues?state=open") if i["title"] == TITLE and "pull_request" not in i), None)
    if issue is None:
        if changed:
            github(f"repos/{project}/issues", "POST", {"title": TITLE, "body": body})
            return "Created the upstream tracking issue."
        return "No upstream changes."
    number = issue["number"]
    if issue.get("body") == body:
        return "Tracking issue already contains this report."
    # A comment generates a notification for subscribers. Its marker makes a
    # retry safe if the subsequent issue update fails.
    marker = "<!-- thor-upstream:" + hashlib.sha256(body.encode("utf-8")).hexdigest() + " -->"
    comments = pages(f"repos/{project}/issues/{number}/comments")
    if not any(marker in (c.get("body") or "") for c in comments):
        github(f"repos/{project}/issues/{number}/comments", "POST", {"body": body + "\n" + marker})
    github(f"repos/{project}/issues/{number}", "PATCH", {"body": body, "state": "open" if changed else "closed"})
    return "Updated the upstream tracking issue." if changed else "Closed the resolved upstream tracking issue."


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--notify", action="store_true", help="Create or update the issue; needs GH_TOKEN with issue-write access")
    args = parser.parse_args()
    pins = json.loads((ROOT / "upstream.json").read_text(encoding="utf-8"))
    if args.notify:
        if not os.environ.get("GH_TOKEN"):
            raise SystemExit("--notify requires GH_TOKEN")
        if os.environ.get("GITHUB_REPOSITORY", pins["project"]) != pins["project"]:
            raise SystemExit("Update upstream.json's project before enabling notifications in another fork")
    changes = changes_since(pins)
    body = render(changes)
    print(body)
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        Path(os.environ["GITHUB_STEP_SUMMARY"]).write_text(body, encoding="utf-8")
    if args.notify:
        # Make the documentation link work from an issue as well as the repo.
        issue_body = body.replace("](docs/UPDATING.md)", f"](https://github.com/{pins['project']}/blob/main/docs/UPDATING.md)")
        print(notify(pins["project"], issue_body, bool(changes)))


if __name__ == "__main__":
    try:
        main()
    except (urllib.error.URLError, ValueError, KeyError) as error:
        raise SystemExit(f"Upstream check failed; no up-to-date result was recorded: {error}")
