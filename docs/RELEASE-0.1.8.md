# PMDO Android Player v0.1.8

Experimental player-only preview for PMDO 0.8.12 on ARM64 Android 13+.

## Fix

- Load MonoGame's XNA-compatible assembly before PMDO's stock Lua include
  resolves shared framework types. This restores globals such as `Color` and
  `GameTime` for unmodified mods, including Enable Mission Board.

The fix was verified with Echoes of the Abyss and with a fresh PMDO base-game
save using All Starters, Any Starting Team, Enable Mission Board, Gender
Unlock, Mega Stones, Music Notice, and Visible Monster Houses. The ground menu
and Job List opened without the reported `Color` error. The original standalone
Friend Areas 1.1 package has not yet been tested in the base-game stack.

## Assets

- `PMDO-Android-Player-v0.1.8.apk`
- `PMDO-Android-Player-Thor-Update-v0.1.8.zip`
- `SHA256SUMS.txt`

Install the APK over the existing app; do not uninstall first. The Thor update
ZIP contains only the player APK and update instructions. Neither release
contains PMDO runtime files, Pokemon assets, Echoes of the Abyss, other mods,
or save data.

This is an unofficial, experimental community preview. See the Android and
AYN Thor installation guides for requirements and import instructions.
