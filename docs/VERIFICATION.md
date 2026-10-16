# Preview verification

Checked on 2026-10-15: AYN Thor, Android 13, Snapdragon 8 Gen 2 / Adreno 740. APK `0.1.0-dev.5`, SHA-256 `64f5ce443757fee6cda169fa163310b69d55a4a9d08cab69ab9846d36ee0087d`. The installed APK hash and integration source hashes match the local build. Retail data came from the owner's Steam RA2 + Yuri installation.

This build passed 26 core gameplay checks, 36 polish checks and 12 support-power checks on the device. Earlier unit-training and save-creation checks are marked below rather than presented as fresh results.

| Check | Result |
| --- | --- |
| Native launch and rendering | GLES 3.2 initializes; both displays render correctly. The early-scene 60 FPS measurement was taken in dev.4; the debug FPS overlay is now hidden. |
| A selection and orders | Select MCV, pan with injected Android joystick input, press A on ground; selected MCV changes world position. |
| Selection rectangles | One-finger drag selects the base through the native selection box. Holding A with left-stick dragging and LT + L3 selecting three Conscripts were also checked in dev.4. |
| Deploy | Bottom command transforms MCV into a Construction Yard. |
| Control groups | Hold bottom group to assign, B deselect, tap group to recall. |
| Production | Queue and cancel a reactor; build to READY, enter placement, B cancels without losing the ready item, place on battlefield; native owned-actor state confirms reactor exists. |
| Unit training | Earlier dev.4 check: build Barracks through the bottom screen, train three Conscripts, and assign/recall them as group 2. |
| Minimap | Tapping and dragging on the bottom map change actual camera coordinates. RB toggles map controls. |
| Wheels | Analog RT and LB open and release correctly. Opening has no default highlight. Map controls cannot issue orders under a wheel. Releasing a deflected wheel stick cannot pan the camera. Hat-axis D-pad changes pages; the two-slot group page maps down to group 10. Both layouts inspected on the top display. |
| Bottom screen | Native cameos and costs, separate production and power catalogs, readable layout, persistent radar and no navigation-bar shrink. Select cycles tabs; controller input works after touching the lower display. |
| Top-screen gestures | Two-finger pan preserves selection; pinch and right-stick up/down zoom in the expected directions; two-finger tap deselects. |
| Native menus | Start opens the pause menu. Stick input moves its cursor without panning the map; D-pad reaches Resume and A activates it. Gameplay wheels stay closed in menus. First menu touch after a selection drag works. |
| Support powers | Unlock Spy Plane through the bottom screen; a native READY power appears. A starts directional targeting, B cancels without spending it or clearing selection, left stick chooses an approach without panning, and releasing A activates the power and starts recharge. Direction-arrow and power-card screenshots inspected. |
| Home / return | Solo game pauses in the background and its tick stays fixed after the synchronized pause settles. Return resumes the same process; actual battlefield pixels remain visible after surface recreation. |
| Restart | Native Restart returns to a fresh MCV match without restarting the app process. |
| Save / load | The native `.orasav` created in dev.4 loads in dev.5, restoring the built base and group. Resume advances the simulation. |
| Windows portable installer | Execute final EXE, detect Steam path and Thor, click Install through the real UI. Bundled APK matches installed build; six archives verified and skipped; saves remain present. |
| Installer recovery checks | Nine backend tests pass, including interrupted/corrupt transfer, cancellation, signature mismatch, offline checksum validation and archive path handling. |
| Package contents | Community filename index present; the six retail archives are absent from the APK. |

`scripts/verify-thor.py --check-graphics` produces the core report. `scripts/qa/verify-polish.py` checks gestures, wheels, menus and background pause; `scripts/qa/verify-support-power.py` checks the Soviet recon-power flow. Save/load and restart were additionally exercised through native menus. Local reports, source hashes, build logs and screenshots are under ignored `artifacts` and `logs` directories on the computer. Screenshots weren't saved onto the Thor. The temporary ADB input probe was removed, and the game was stopped after testing.

The return test originally preserved the process but lost indexed world textures. The Android surface layer now keeps the EGL context current on a small offscreen surface while the window is absent, and rebinds before rendering resumes. The test checks battlefield pixels as well as process and simulation state.

Ready-building placement originally ran outside OpenRA's local UI dispatch boundary. Controller dispatch now uses the same unsynced boundary as native widgets, with simulation changes issued as orders. The engine bridge also stops exposing a disposed world during map transitions.

Not yet verified: every faction's powers and upgrades, a complete match under heavy combat, multiplayer, human confirmation of audio and physical stick feel, macOS/Linux installer execution, or other Android hardware. These results describe this preview and device, not every OpenRA or RV feature.

Repository setup on 2026-10-16 adds six upstream-check tests, including notification retries and duplicate prevention. The installer was repackaged for this repository with the same tested APK. GitHub checks cover the installer backend and source metadata on Windows, macOS and Linux, plus clean application of the pinned engine patch; these are separate from the device results above.
