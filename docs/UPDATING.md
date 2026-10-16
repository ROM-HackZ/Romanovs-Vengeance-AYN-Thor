# Updating the Thor port

The installed game uses fixed versions. An upstream commit doesn't change anyone's Thor installation.

`upstream.json` records the RV source, its required custom engine, the Android base and the reviewed main OpenRA releases. Build scripts read those source revisions. `mod.config` also names the engine because the upstream desktop tooling needs it; the project check catches a mismatch.

## Upstream notifications

The **Upstream update check** workflow runs daily at 09:17 UTC and can be run from the Actions tab. It checks:

| Source | What gets compared |
| --- | --- |
| Romanov's Vengeance | The recorded RV commit against its master branch. |
| RV's engine | The engine revision in upstream RV's `mod.config`. The engine fork's default branch isn't assumed to be compatible. |
| Android platform base | The recorded vendored revision against the Android project's master branch. |
| Main OpenRA | Reviewed stable and playtest release tags. These are maintenance information, not automatic engine upgrades. |

Changes create one issue named **[upstream] Updates to review**. A changed report adds a comment so issue subscribers can receive a notification. An identical report doesn't generate another notification. Once the recorded versions match upstream, the check closes the resolved issue. A failed network request fails the workflow instead of reporting that everything is current.

Use **Watch > Custom > Issues** on this repo to get these alerts through your GitHub notification settings. No external server or personal token is needed by the workflow; it uses the repository's GitHub Actions token with issue-write permission. GitHub schedules can run late, so this isn't an exact-time alert.

To inspect the report locally without writing an issue:

```sh
python scripts/check-upstream.py
```

Players currently get updates from our releases page. In-app update notices and downloading are planned; they aren't in this preview.

## Bring an update across

Our `origin` is `ROM-HackZ/Romanovs-Vengeance-AYN-Thor`. The `upstream` remote is `MustaphaTR/Romanovs-Vengeance`. Keep RV's history when working on this port.

1. Create an update branch and fetch upstream RV changes. Review its `mod.config` to find the matching engine revision.
2. Merge the intended RV changes. Update `upstream.json` and `mod.config` together. Prepare a separate engine checkout when changing its revision; `prepare-engine.py` deliberately refuses to overwrite an unexpected checkout.
3. Adapt `patches/thor-platform.patch` and the Android integration for any changed APIs. Keep Thor input and UI work in `android` where possible.
4. Run the repository checks, build the APK and run the device checks in [BUILDING.md](BUILDING.md). Verify menu navigation, production, support powers, save loading and app switching. Close the game afterward.
5. Record the tested upstream versions and results. Save compatibility and PC multiplayer compatibility need their own checks for each release.

The Android source is vendored. Bring useful upstream Android fixes across selectively so our controller, touch and second-screen changes survive.

## Publish a preview

Keep the existing Android signing key outside Git. Release updates need the same key and application ID as the installed preview. Source-only CI jobs don't have that key and don't publish APKs.

1. Update the Android display version and increasing version code, plus `installer/release.json`. Build and verify the APK before packaging the installer.
2. Package the installer with that APK. Check the real installer window, including an update that retains the six game archives and existing saves.
3. Run `python scripts/check-project.py`, the installer tests and upstream-check tests. Review the staged files. Retail archives, generated APKs, installers and signing keys stay out of Git.
4. Commit the source and create a version tag for the tested build. Attach the APK, Windows installer, build information, verification summary and `SHA256SUMS.txt` to a GitHub prerelease. Checksums should use the downloaded asset filenames.
5. Keep README download links and the installer release/issue links pointed at this Thor repository. While the repo is private, downloads require GitHub access; the desktop installer uses its bundled APK instead of trying to fetch private assets anonymously.

The original RA2 and Yuri's Revenge campaigns aren't part of this mod. Upstream engine updates don't supply those campaigns automatically.
