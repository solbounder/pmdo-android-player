# PMDO Android Player v0.1.9

Experimental player-only preview for PMDO 0.8.12 on ARM64 Android 13+.

## Fixes

- The PMDO music menu no longer reserves one Android audio source for every
  listed track. This removes the source-exhaustion cause identified in the
  reported `InstancePlayLimitException` and subsequent audio lockup.
- All app-owned launcher, status, dialog, error-report, keyboard, and touch
  editor text is now English.
- Save permission errors include recovery guidance for files copied manually
  with incompatible Android ownership. Importing `SAVE.rssv` through the
  launcher remains the supported path.

## Assets

- `PMDO-Android-Player-v0.1.9.apk`
- `PMDO-Android-Player-Thor-Update-v0.1.9.zip`
- `SHA256SUMS.txt`

Install the APK over the existing app; do not uninstall first. The Thor update
ZIP contains only the player APK and update instructions. Neither release
contains PMDO runtime files, Pokemon assets, Echoes of the Abyss, other mods,
or save data.

This is an unofficial, experimental community preview. See the Android and
AYN Thor installation guides for requirements and import instructions.
