# Changelog

## 0.1.9 - 2026-07-23

- Deferred Android playback resources until a song actually starts. Browsing
  the PMDO music menu no longer reserves one OpenAL source per listed track,
  removing the source-exhaustion cause identified in the reported
  `InstancePlayLimitException` and audio lockup.
- Converted every app-owned launcher, status, dialog, error-report, keyboard,
  and touch-editor surface to English.
- Added actionable English guidance for save files copied manually with
  incompatible Android ownership or permissions.

## 0.1.8 - 2026-07-22

- Load MonoGame's XNA-compatible assembly into the Android Lua runtime before
  PMDO's stock `include.lua` resolves shared types. This restores globals such
  as `Color` and `GameTime` for unmodified mods including Enable Mission Board.

## 0.1.7 - 2026-07-22

- Separated complete Android save backups (`.zip`) from direct PMDO
  `SAVE.rssv` imports. A raw save can now be assigned to the base game or an
  installed Quest; the prior save is retained as `SAVE.rssv.bak`.
- Added launcher controls to select one Quest and enable or disable additional
  mods individually.
- Treat mod requirements through PMDO 0.8.12 as compatible without modifying
  the original `Mod.xml`. Mods requiring a newer version or carrying native
  desktop files are retained but not auto-enabled.
- Documented that the native Android keyboard replaces unsupported external
  desktop on-screen-keyboard mods.

## 0.1.6 - 2026-07-22

- Fixed runtime imports from regular PMDO 0.8.12 installations. The locked
  manifest now hashes the canonical Git archive bytes used by the official
  installer instead of CRLF-converted files from a Windows worktree.
- Kept strict size and SHA-256 verification and added a release-build check
  that rejects stale runtime manifests.

## 0.1.5 - 2026-07-21

- Refreshed the Thor update bundle with Echoes of the Abyss 0.8.2. The accepted
  Abyss artwork is now stored as engine-native tiles and actually rendered by
  the dungeon-selection map instead of merely being tracked as source art.

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
  reviewed Echoes of the Abyss Quest tree.

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
