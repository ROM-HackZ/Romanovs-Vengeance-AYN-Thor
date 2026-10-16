# Romanov's Vengeance on AYN Thor

I wanted some RA2 on the Thor with proper controls and a reason to use the bottom screen. This is a native Android port of [Romanov's Vengeance](https://github.com/MustaphaTR/Romanovs-Vengeance), built on OpenRA.

**This preview is for skirmish. The original Red Alert 2 and Yuri's Revenge campaigns are not included.** RV recreates and expands RA2 with its own balance and content. You need your own PC copies of RA2 and Yuri's Revenge for the graphics, sounds and music.

## Download

[Get the Windows installer](https://github.com/ROM-HackZ/Romanovs-Vengeance-AYN-Thor/releases/download/v0.1.0-dev.5/Romanovs-Thor-Installer-0.1.0-Windows-x64.exe), or see the [preview release](https://github.com/ROM-HackZ/Romanovs-Vengeance-AYN-Thor/releases/tag/v0.1.0-dev.5) for the APK, checksums and known limits. The Windows installer includes the APK.

The repo is currently private. You'll need to sign into GitHub with an account that has access to download the preview.

## Get it running

1. Install RA2 + Yuri's Revenge on your computer. The Steam collection works.
2. Enable **USB debugging** on the Thor, plug it in, and allow your computer when Android asks.
3. Open **Romanovs Thor Installer**, choose your game folder, check the detected Thor, and hit **Install**. Packaged installers include the APK. The source version uses `artifacts/Romanovs-Thor.apk`.
4. Open **Romanov's Vengeance - Thor**. Give the first load a little time, then start a Skirmish and add an AI opponent.

The computer is only needed to install or update. After that, open the app and play. No Shizuku, wireless debugging, root or background companion app.

The installer reads six archives from your PC copy and checks them after transfer. It doesn't change the Steam installation. Updates keep the app's game data and saves. An interrupted transfer can be retried.

| Computer | Current status |
| --- | --- |
| Windows | Portable installer available above. GUI tested with the Steam folder and connected Thor. |
| macOS | Same installer source and file pickers. Needs a copied PC game folder; not tested on a Mac yet. |
| Linux | Same installer source and file pickers. Not tested on a Linux desktop yet. |

See [building and packaging](docs/BUILDING.md) if you're working from source.

## Controls

The camera and cursor work together: pan the map to put something under the cursor, then press A. Hold A and move the left stick to draw a selection box.

| Control | Does what |
| --- | --- |
| Left stick | Pan the camera. In menus, move the cursor. |
| Right stick up / down | Zoom in / out. The game uses a fixed isometric camera. |
| A | Select, issue an order, or place a ready building. Hold A + left stick to drag a selection box. |
| B | Cancel targeting or building placement; otherwise deselect. Back out of menus. |
| X | Attack-move targeting. Press A on the destination. |
| Y | Stop the selected units. |
| LT | Add to selection or queue an order while held. On production buttons, queue five. |
| RT | Hold for the command wheel. Left stick chooses, A confirms. |
| LB | Hold for the group wheel. Left stick chooses, A recalls. |
| RB | Toggle minimap controls: left stick aims, Y looks there, A orders there, B leaves. |
| L3 | Centre on the selection. LT + L3 selects the same unit types on screen. |
| Hold R3 + right stick | Move the cursor without panning. |
| D-pad Up | Next idle harvester. |
| D-pad Down | Construction Yard. |
| D-pad Left | Cycle units. |
| D-pad Right | Cycle production buildings. |
| Start | Game menu. |
| Select | Switch between Commands, Build and Powers on the bottom screen. |

Both wheels use the **left stick**. D-pad left/right changes pages. Choose a slot before pressing A; opening a wheel doesn't pick a command for you. The command wheel includes deploy/unload, attack move, stop, same-type selection, centre, repair and sell, followed by production and powers grouped into pages. On a production choice, A queues or places it; X cancels one queued item.

In menus, the D-pad jumps between actual buttons, A clicks and B goes back. The left stick moves the cursor there without moving the battlefield.

For a directional airstrike, hold A on the target, move the left stick to choose the approach, then release A to launch. B cancels without spending the power.

| With the group wheel open | Action |
| --- | --- |
| A | Recall the highlighted group. |
| X | Assign the current selection to it. |
| LT + X | Add the selection to it. |
| Y | Centre on it. |
| D-pad left / right | Switch between groups 1–8 and 9–10. |

## Bottom screen

| Area | Use it for |
| --- | --- |
| Commands | Large labelled action buttons, with unavailable actions dimmed. |
| Minimap | Tap to jump there; drag to pan across the map. Radar visibility follows the game rules. |
| Build | Native icons, prices, queue counts and progress. Filter buildings, defences or units. Tap to queue; tap a READY building to place it on the top screen. Hold a production button to cancel one; tap a paused queue to resume it. |
| Powers | Upgrades and support powers, with charge progress and targeting through the engine. |
| Header | Current selection, health, cash and power usage. |
| Groups 1–10 | Tap to recall; hold to assign the current selection. |

The buttons and minimap use the engine's actual state and orders. There's no screenshot detection or Android input injection.

On the top screen, tap to select or order, and drag one finger for a selection box. When targeting a directional airstrike, that drag chooses its approach instead. Drag with two fingers to pan, pinch to zoom, or briefly tap with two fingers to deselect. The isometric camera doesn't rotate.

Leaving the app releases held controls and pauses a solo skirmish. Coming back resumes it if we paused it. Multiplayer keeps running.

## What's been checked

A live skirmish on the Thor reaches 60 FPS in the tested starting scene. Selection, movement orders, MCV deployment, building production/placement/cancel, groups, minimap tap/drag, both wheel layouts, save/load, restarting and leaving/returning to the app have been exercised on the installed app. That isn't a full-match performance guarantee. See the [verification notes](docs/VERIFICATION.md).

Still needs wider playtesting: physical stick feel, longer matches, multiplayer, and other Android devices. Sound output initializes through OpenAL; listening on the handheld is still part of the playtest.

## Updates

| What | Status |
| --- | --- |
| Upstream tracking | A daily GitHub check watches RV, its required engine, the Android base and main OpenRA releases. Changes go into one tracking issue. |
| Updating this preview | Download a new Thor APK or installer from this repo's releases. Compatible app updates retain imported game files and saves. Save-format compatibility is checked separately. |
| Update notices in the game | Planned. The app doesn't check for or download new releases yet. |

To receive upstream alerts, use **Watch > Custom > Issues** on this repo. See [updating the port](docs/UPDATING.md) for how we bring changes across and test them.

## Credits

- [Romanov's Vengeance](https://github.com/MustaphaTR/Romanovs-Vengeance) — the RA2 recreation, mod and custom engine.
- [OpenRA](https://github.com/OpenRA/OpenRA) — the engine underneath it.
- [franktfye/OpenRA-android](https://github.com/franktfye/OpenRA-android) — the Android platform work this port builds on.
- [Generals Thor](https://github.com/ROM-HackZ/generals-zero-hour-ayn-thor) — our desktop installer base and controller design experience.

Thor integration by ROM-HackZ. See [source and dependency notes](docs/SOURCES.md), the [upstream README](docs/UPSTREAM-README.md), and [LICENSE](LICENSE). Retail game files belong to their owners and are imported separately.
