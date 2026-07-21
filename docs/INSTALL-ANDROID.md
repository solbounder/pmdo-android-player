# Installing PMDO Android Player

This guide applies to general ARM64 Android phones, tablets, and handhelds.
For device-specific AYN Thor instructions, see
[Installing on AYN Thor](INSTALL-AYN-THOR.md).

## Requirements

- An ARM64 device running Android 13 or newer.
- At least 1.1 GB of free storage during the initial import.
- A legally obtained, unmodified PMDO 0.8.12 runtime folder. Its root must
  directly contain folders such as `Base`, `CONFIG`, `Content`, `Data`, and
  `Script`.
- Optional Quest mods, such as
  [Echoes of the Abyss for Windows/PMDO](https://github.com/solbounder/pmd-echoes-of-the-abyss).

The APK does not contain PMDO, Pokémon assets, Quest data, or save files.
Runtime and mod files are imported into private Android app storage.

## Install the APK

1. Open the [latest Android release](https://github.com/solbounder/pmdo-android-player/releases/latest).
2. Download the file named `PMDO-Android-Player-vX.Y.Z.apk`.
3. Open the downloaded APK.
4. If Android blocks it, allow **Install unknown apps** for the browser or file
   manager you used.
5. Confirm the installation, then open **PMDO Android Player**.

## Import PMDO 0.8.12

1. Tap **Import PMDO 0.8.12 folder**.
2. In Android's folder picker, open the PMDO runtime folder.
3. Verify that `Base`, `CONFIG`, `Content`, `Data`, and `Script` are visible
   directly inside the selected folder.
4. Tap **Use this folder** and approve access.
5. Keep the app open while it verifies and copies approximately 11,485 files
   and 536 MB. Several minutes is normal.
6. Wait until the launcher reports that PMDO 0.8.12 was imported.

If the selected folder contains another folder before `Base` and `Content`, go
one level deeper and select the actual runtime root.

## Import Echoes of the Abyss or another mod

You can import either a folder or a ZIP:

1. Download or copy the mod to the Android device.
2. Choose **Import mod folder** or **Import mod ZIP**.
3. For a folder import, select the folder that directly contains `Mod.xml`.
4. For a ZIP import, `Mod.xml` may be at the ZIP root or inside one outer
   wrapper folder.
5. Wait for the confirmation that the mod was imported and activated.

Lua, JSON/JSONPatch, images, and Ogg audio are imported without rewriting.
Mods that depend on Windows DLLs, native desktop libraries, LuaSocket, or
Windows-specific paths may require porting.

## Start and control the game

1. Tap **Start game**.
2. Use a connected controller or the on-screen D-pad and A/B/X/Y controls.
3. Pressing a physical controller button hides the touch controls. Tap the
   touchscreen once to bring them back.
4. Use the keyboard icon at the top center when the game requests text input.
5. **Max 4:3** is the default and preserves the intended aspect ratio. Under
   the in-game window-size option, you can instead choose stretched full screen
   or a fixed 4:3 size. Changes apply immediately.

## Updating without losing data

Install a newer APK directly over the existing app. Do **not** uninstall the
old version first. Uninstalling removes the private PMDO runtime, mods, and save
data.

Before uninstalling or moving to another device, use **Export save data** in
the launcher. Restore it later with **Import save data**.

## Troubleshooting

- A black screen immediately after launch usually means the wrong PMDO folder
  was imported or a required runtime file is missing.
- If Android reports a signature conflict during an update, do not uninstall
  the app. Record the message and verify that the APK came from this
  repository's Releases page.
- If a **PMDO error on this device** dialog appears, use **Copy text** and
  include that text when reporting the problem.

