# Changelog

## 0.1.5 - 2026-07-21

- Refreshed the Thor update bundle with Echoes of the Abyss 0.8.1, fixing the
  Abyss selection map black screen caused by an invalid generated GroundMap.

- Added a persistent, editable touch overlay: drag the D-pad and up to 16
  buttons, configure scale, visibility and A/B/X/Y/L/R/ZL/ZR/Start/Select/L3/R3
  bindings, then save, cancel or reset from the gear menu.

- Added L/R/ZL/ZR multi-select shortcuts and every left/right shoulder chord
  for selecting all sellable items across inventory pages.
- Fixed fanfare fade overshoot and clamped Android audio volume writes to the
  finite range accepted by MonoGame.
- Pinned the patch committer identity so the 18-patch engine head is
  reproducible independently of the caller's Git configuration.
- Added a deterministic Thor update bundle containing the 0.1.5 APK and the
  reviewed Echoes of the Abyss 0.8.1 Quest tree.

## 0.1.4

- Added an ARM64 Android 13+ player for PMDO 0.8.12.
- Added folder and ZIP import for unmodified Lua/data/asset Quest mods.
- Added physical controller input and a multi-touch gamepad overlay.
- Added Android keyboard and clipboard support for game text entry.
- Added maximum 4:3 as the default plus full-screen stretching and fixed 4:3
  display modes.
- Added save import/export and crash-safe runtime import.
- Improved BGM recovery and recycling of overlapping one-shot sound effects.
- Added an original, neutral launcher icon and the visible app name
  **PMDO Android Player**.
