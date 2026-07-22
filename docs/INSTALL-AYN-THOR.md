# Installing PMDO Android Player on AYN Thor

The Android port uses one landscape game view and does not require the Thor's
lower display. These steps assume that no files are currently on the device.

## What to copy to the Thor

Prepare these items on a PC:

1. The latest `PMDO-Android-Player-vX.Y.Z.apk` from the
   [Android Releases page](https://github.com/solbounder/pmdo-android-player/releases/latest).
2. A legally obtained PMDO 0.8.12 runtime folder. Its root must directly
   contain `Base`, `CONFIG`, `Content`, `Data`, `Script`, and the other normal
   PMDO folders.
3. The `Echoes_of_the_Abyss` mod folder from the
   [Windows/PMDO repository](https://github.com/solbounder/pmd-echoes-of-the-abyss).
   `Mod.xml` must be directly inside that folder.

Keep at least 2 GB free on the Thor while copying and importing.

## Copy the files over USB

1. Connect the Thor to the PC with USB-C.
2. On the Thor, open the USB notification and choose **File transfer**.
3. On the PC, open the Thor's **Internal storage** and then `Download`.
4. Copy the APK, the complete PMDO runtime folder, and
   `Echoes_of_the_Abyss` into `Download`.
5. If either folder is still a ZIP, extract it completely on the Thor before
   continuing.

The expected layout is similar to:

```text
Download/
  PMDO-Android-Player-v0.1.7.apk
  DumpAsset/
    Base/
    CONFIG/
    Content/
    Data/
    Script/
  Echoes_of_the_Abyss/
    Mod.xml
    Data/
    Script/
```

The PMDO folder may have a different name than `DumpAsset`; the folders inside
it are what matter.

## Install the app

1. Open the Thor's file manager and go to `Download`.
2. Tap `PMDO-Android-Player-v0.1.7.apk`.
3. If installation is blocked, enable **Allow from this source** for that file
   manager.
4. Confirm **Install** and open **PMDO Android Player**.

## Import PMDO

1. Tap **Import PMDO 0.8.12 folder**.
2. Open `Download`, then open the copied PMDO folder.
3. Confirm that `Base`, `CONFIG`, `Content`, `Data`, and `Script` are visible.
4. Tap **Use this folder** and approve the permission prompt.
5. Wait until the progress display finishes. Five minutes can be normal; do
   not close the app or turn off the Thor during the import.

## Import Echoes of the Abyss

1. Tap **Import mod folder**.
2. Open `Download/Echoes_of_the_Abyss`.
3. Confirm that `Mod.xml` is visible directly in this folder.
4. Tap **Use this folder**.
5. Wait for the import confirmation. Use **Manage mods** to select the Quest
   and manage any additional mods individually.

PMDO requirements through 0.8.12 work without changing `Mod.xml`. A mod that
requires a newer PMDO version or includes native desktop files is retained but
not enabled automatically. The external desktop on-screen-keyboard mod is not
supported; use the built-in Android keyboard button instead.

## Import a regular PMDO save

1. Tap **Import save data**.
2. Select **PMDO save (`SAVE.rssv`)** rather than **Android backup (`.zip`)**.
3. Select the base game or an installed Quest, then select the `SAVE.rssv`
   file.
4. The app safely keeps the prior save as `SAVE.rssv.bak` before activating
   the imported save.

## Start and configure the game

1. Tap **Start game**. The first start may take longer than later starts.
2. The Thor's physical controls work directly.
3. A controller input hides the touchscreen controls. Tap the upper
   touchscreen once to reactivate them; the next controller input hides them
   again.
4. The keyboard button is centered at the top edge for text entry.
5. **Max 4:3** is the default. In the game's options you can instead select
   stretched full screen, 320×240, 640×480, 960×720, or 1280×960.

After a successful import, the copied runtime and mod folders in `Download`
may be deleted because the app keeps its own private copies. Keep the APK if
you want an offline installer.

## Install future updates

1. Download the new APK.
2. Tap it and choose **Update** or **Install** over the existing version.
3. Do not uninstall the current app and do not import PMDO or the mod again.

Uninstalling the app removes the private runtime, mods, and saves. Use
**Export Android backup** before an intentional uninstall or device change.

