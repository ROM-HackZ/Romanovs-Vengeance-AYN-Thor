# Building the Thor preview

The Android app is arm64 only. Current tested toolchain: Ubuntu under WSL2, .NET SDK 10.0.402, Android workload 36.1.2, Android platform 36, NDK 27.2.12479018, JDK 17, CMake and Ninja. Python 3.10+ runs the build and USB importer; native source extraction also needs tarfile's `filter` support, present in updated Python 3.10 installations and Python 3.12+.

Install those tools first. `global.json` selects the .NET SDK. By default the build looks for .NET in `~/.local/share/thor-dotnet`, Android SDK in `~/Android/Sdk`, and Java in `/usr/lib/jvm/java-17-openjdk-amd64`. Override `THOR_DOTNET_ROOT`, `ANDROID_HOME`, `ANDROID_NDK_HOME`, `JAVA_HOME` and `CMAKE` for your machine. CMake defaults to `~/.local/bin/cmake`; `CMAKE=cmake` uses PATH. Ninja must be on PATH.

## APK

From Windows, with Python and WSL Ubuntu installed:

```powershell
python scripts/build-android.py
```

This stages the source into `~/.cache/romanovs-thor/build` inside WSL and builds there. It avoids running .NET's file-heavy Android build directly through OneDrive or `/mnt/c`. `THOR_WSL_DISTRO` selects another distro. Build outputs return to `artifacts/Romanovs-Thor.apk` and `logs/build-android.log` on Windows.

On Linux, from a native Linux filesystem:

```sh
bash scripts/build-android.sh
```

The script fetches the pinned RV engine and applies `patches/thor-platform.patch`. If the native libraries are absent, it downloads the pinned Lua, FreeType and OpenAL releases, checks their hashes and builds them with the NDK. No retail game files are needed to compile the app.

The private preview retains the development signing key used by the tested APK. Keep that key outside the repository to update existing installs. A build signed by a different key cannot replace the installed app; the installer stops rather than uninstalling it and losing saves. A fresh source build normally uses that computer's own debug key.

## Install over USB

For development, use ADB from Android Platform Tools:

```sh
python scripts/install-thor.py "/path/to/owned/RA2-and-Yuri" --device YOUR_SERIAL --adb /path/to/adb
```

This installs the APK, imports the six required archives with SHA-256 checks, and launches the app. `--apk` chooses another build. The source folder must contain `ra2.mix`, `language.mix`, `ra2md.mix`, `langmd.mix`, `theme.mix` and `thememd.mix`. The installer copies only these archives, into the app's own external-files directory.

## Desktop UI

Node.js 22+ and npm are required. From the repository root:

```sh
npm ci --prefix installer
npm start --prefix installer
npm test --prefix installer
```

The packaged installer includes the locally built APK. Examples:

```sh
node installer/package-app.js win32 x64 portable
node installer/package-app.js linux x64 deb
node installer/package-app.js darwin arm64
```

macOS packaging requires `codesign` on a Mac, or `THOR_RCODESIGN` pointing to rcodesign when packaging elsewhere. These produce locally signed bundles, without Developer ID notarization. Windows packaging is the tested path so far. Output goes into `artifacts/installer`.

The GUI can find existing ADB tools or download Google's Platform Tools after accepting their terms. A packaged APK and existing ADB tools allow offline installation. The repository is currently private, so `installer/release.json` leaves automatic downloading disabled. Use the bundled APK or download the APK through a signed-in GitHub browser session and choose it under Advanced. No GitHub credentials are bundled in the installer.

## Repository checks

```sh
python scripts/check-project.py
python -m unittest discover -s scripts/tests -v
npm test --prefix installer
```

GitHub runs these checks on Windows, macOS and Linux, plus a separate check that the engine patch applies cleanly and can be prepared twice. These jobs don't build a release APK or replace the Thor device checks below. Release packaging is manual so the tested APK keeps its signing key. See [updating and releasing](UPDATING.md).

## Live verification

`scripts/verify-thor.py` drives the real app and checks engine state, not screenshots alone. It changes the test match: leave the controls untouched and use a disposable skirmish. Start from the default two-player lobby or a fresh match with the MCV under the centre cursor.

```sh
python scripts/verify-thor.py --adb /path/to/adb --device YOUR_SERIAL --start-lobby
```

Coordinates currently target the Thor's 1920×1080 top display and 1240×1080 bottom display. The script discovers physical screenshot displays; `--bottom-display` defaults to 4. Add `--check-graphics` with Pillow installed to compare battlefield pixels across a Home/return cycle, as well as checking that the match survives. Reports and screenshots stay in the ignored `artifacts` directory on the computer.

The controller writes `thor-state.json` atomically in the app's external-files directory for this check. Save games and retail archives are separate from bundled mod data. Never clear app storage to update a build.

For the additional gesture, wheel, menu and solo-pause checks, build the ADB-only input probe with the Android SDK and JDK, then push it temporarily:

```sh
bash scripts/qa/build-probe.sh
adb -s YOUR_SERIAL push artifacts/thor-input-probe.jar /data/local/tmp/romanovs-thor-input-probe.jar
python scripts/qa/verify-polish.py --adb /path/to/adb --device YOUR_SERIAL
adb -s YOUR_SERIAL shell rm /data/local/tmp/romanovs-thor-input-probe.jar
```

Run this after the main check has built a base and assigned group 1. The probe runs with ADB shell permissions, needs no root and isn't included in the app or installer. It sends real Android multi-touch and controller events. These scripts leave the game running for inspection; close it when finished so it can release the displays.

In a Soviet test match with an unspent commander point, `scripts/qa/verify-support-power.py` exercises Spy Plane unlock, B cancellation, approach-direction control and activation. Run it before removing the input probe. This spends the test match's commander point and fires the power.

`scripts/qa/verify-catalog.py` checks a French base with Aircraft Training already unlocked and a Barracks. It checks zero-point explanations, retained choices in the deck and wheel, and actual GI training with a soldier or nothing selected. Run it with `--purchase` on a fresh French match instead to check the transition from one point to zero, including holding for a description without buying. These checks change the test match. Save first, keep the device unlocked, and restore your save afterward.
