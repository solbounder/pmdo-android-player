# PMDO Android Player

An experimental, unofficial ARM64 Android port of the PMDO 0.8.12 player,
primarily tested on the upper display of the AYN Thor.

This is an independent community project. It is not an official PMDO release
and is not affiliated with or endorsed by the PMDO developers, Nintendo,
Creatures Inc., GAME FREAK inc., or The Pokemon Company.

## Download and installation

Preview APKs are published on the
[GitHub Releases page](https://github.com/solbounder/pmdo-android-player/releases/latest).

- [General Android installation](docs/INSTALL-ANDROID.md)
- [AYN Thor installation](docs/INSTALL-AYN-THOR.md)
- [Echoes of the Abyss Quest repository](https://github.com/solbounder/pmd-echoes-of-the-abyss)

The APK contains the Android player only. It does not contain or redistribute
the PMDO runtime, Pokemon assets, Quest data, or save files. The user must
import a legally obtained PMDO 0.8.12 runtime through Android's system folder
picker. Imported files are checked against a SHA-256 manifest before they are
activated.

Quest mods are optional. Standard Lua, data, JSON Patch, image, and Ogg based
Quest mods can be imported as folders or ZIP files. Mods containing desktop
DLLs, native desktop libraries, LuaSocket, network dependencies, or
Windows-specific path assumptions may require additional porting.

## Current preview scope

- ARM64 devices running Android 13 or newer
- Native Android APK without Winlator or Windows emulation
- Physical controllers and a persistent, freely configurable multitouch
  gamepad overlay, including L/R/ZL/ZR
- Shoulder-button item selection and left/right shoulder chords for selecting
  every sellable inventory item
- Android keyboard and clipboard integration
- Maximum 4:3 as the default, with full screen and fixed 4:3 modes available
- Optional Quest mod import
- Atomic runtime import and save import or export
- No `INTERNET` permission
- No Android backup or device transfer of private runtime, mod, or save data

The app has been tested successfully on an AYN Thor. Wider device compatibility
is not yet guaranteed, so releases remain experimental previews. Please report
problems through GitHub Issues and include the copyable engine error report when
one is available.

Imported Lua mods are executable code. Only import mods from sources you trust.
Archive imports are bounded and reject path traversal, colliding paths, and
oversized inputs.

## Development transparency

The initial implementation was produced with extensive use of OpenAI Codex.
The maintainer performs the hands-on device testing and release acceptance.
The complete player source, pinned upstream revisions, patch series, dependency
locks, build scripts, tests, and release checks are public so that the PMDO
community can independently inspect and review the work.

The repository contains no private signing key. Preview APKs are signed with a
stable local test certificate so they can be updated in place. Treat them as
sideloaded test builds, not as store releases.

## Reproducible local build

The supported build host is Windows with:

- .NET SDK 10.0.302 and Android workload 36.1.69
- Android SDK and Build Tools 36, platform 36, and NDK 29.0.14206865
- JDK 21 from Android Studio

Pinned package versions, NuGet lock files, upstream commits, and the complete
RogueEssence patch series are committed to this repository.

```powershell
dotnet workload install android
.\tools\Get-Upstream.ps1
.\tools\Build-Android.ps1 -Configuration Release
```

The build verifies all upstream revisions, applies the numbered patches, builds
Lua 5.4 with 16 KiB page alignment, restores NuGet packages in locked mode, and
runs the APK audit. A local signing certificate is required and intentionally
excluded from the repository.

Run the portable test suite with:

```powershell
.\.dotnet\dotnet.exe restore PMDO.Android.slnx --locked-mode
.\.dotnet\dotnet.exe test tests\PMDO.Portable.Tests\PMDO.Portable.Tests.csproj -c Release --no-restore
```

See [the architecture document](docs/architecture.md) for trust boundaries and
platform design, [the acceptance checklist](docs/acceptance.md) for hardware
testing, [SECURITY.md](SECURITY.md) for private vulnerability reports, and
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for dependency attribution.

## License

Original project code and original artwork in this repository are available
under the MIT License. Upstream and third party components retain their
respective licenses and notices. See [LICENSE](LICENSE) and
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
