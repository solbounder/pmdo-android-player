# PMDO Android Player v0.1.7

Experimental player-only preview for PMDO 0.8.12 on ARM64 Android 13+.

## Highlights

- **Import save data** now distinguishes a complete Android backup (`.zip`)
  from a regular PMDO `SAVE.rssv`. For a raw save, select the base game or an
  installed Quest first. The prior save is safely kept as `SAVE.rssv.bak`.
- **Manage mods** lets you choose the base game or one Quest and toggle each
  additional mod independently.
- Mod requirements through PMDO 0.8.12 are accepted without modifying the
  mod's `Mod.xml`. Mods requiring a newer version or containing native desktop
  files are imported but not enabled automatically.
- The external desktop on-screen-keyboard mod is not supported. Use the native
  Android keyboard and clipboard integration instead.

## Assets

- `PMDO-Android-Player-v0.1.7.apk`
- `PMDO-Android-Player-Thor-Update-v0.1.7.zip`
- `SHA256SUMS.txt`

Install the APK over the existing app; do not uninstall first. The Thor update
ZIP contains only the player APK and update instructions. Neither release
contains PMDO runtime files, Pokémon assets, Echoes of the Abyss, other mods,
or save data.

This is an unofficial, experimental community preview. See the Android and
AYN Thor installation guides for requirements and import instructions.
