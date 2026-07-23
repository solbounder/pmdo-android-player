# PMDO Android Player v0.1.10

Experimental player-only preview for PMDO 0.8.12 on ARM64 Android 13+.

## Hotfix

- Fixes the immediate startup crash in the published v0.1.9 APK.
- The final ARM64 package was rebuilt from a fully clean output tree so its
  Java activity wrappers and native .NET registration data match.
- The exact signed APK was verified with a fresh install and an in-place update
  over v0.1.9 before release.

v0.1.10 also contains the v0.1.9 music-resource fix, fully English player UI,
and save-permission guidance.

## Assets

- `PMDO-Android-Player-v0.1.10.apk`
- `PMDO-Android-Player-Thor-Update-v0.1.10.zip`
- `SHA256SUMS.txt`

Install the APK over the existing app; do not uninstall first. This preserves
the imported PMDO runtime, optional mods, settings, and save data.

Neither release package contains PMDO runtime files, Pokemon assets, Echoes of
the Abyss, other mods, or save data.

This is an unofficial, experimental community preview. See the Android and
AYN Thor installation guides for requirements and import instructions.
