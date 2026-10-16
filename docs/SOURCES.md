# Sources used by this preview

| Component | Source / revision |
| --- | --- |
| Romanov's Vengeance mod | `MustaphaTR/Romanovs-Vengeance`, `231059963663dcb07d07b56f607b41d173795da9` |
| RV custom OpenRA engine | `MustaphaTR/OpenRA`, `ac7864a16d00788d859eb3e9188b4ee9104e879f` |
| Android platform source | `franktfye/OpenRA-android`, `40c4a390b7fc58c11300f9c0ee53d2ecbaf22a50` |
| Installer base | `ROM-HackZ/generals-zero-hour-ayn-thor`; adapted locally for this app and six RA2/Yuri archives |
| Lua | 5.1.5, lua.org release |
| FreeType | 2.13.2, GNU Savannah release |
| OpenAL Soft | 1.23.1, kcat/openal-soft release |

`scripts/fetch-native-sources.py` records and checks native source archive SHA-256 values. The desktop dependencies are locked in `installer/package-lock.json`. Android platform code is vendored in `android`; engine modifications are carried as `patches/thor-platform.patch` against the pinned engine.

The engine patch adds Android platform registration, Android assembly loading and EGL symbol lookup, a native radar state/target API, and selection-drag cancellation without issuing an order. Controller commands execute on the engine thread through the engine's selection, order generator, production, support-power and control-group APIs. Build icons come from the native sprite atlas. Menu navigation uses native widget bounds, including the separate in-game menu container. Android rendering uses an EGL/GLES surface; the second screen uses an Android Presentation.

The code retains upstream copyright notices and GPL licensing. Lua, FreeType, OpenAL Soft and desktop dependencies retain their own licenses in their source distributions or installed packages. RV artwork and credits remain with the upstream mod. EA retail archives are supplied by the player and are not included in the APK, installer or repository.
