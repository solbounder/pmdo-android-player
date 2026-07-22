# PMDO Android Player v0.1.6

This preview fixes runtime imports from regular PMDO 0.8.12 installations.
Versions 0.1.4 and 0.1.5 generated their integrity manifest from a Windows
worktree, where line endings in text files differed from the canonical files
downloaded by the official PMDO installer. Valid runtimes could therefore be
rejected at `Base/GFXParams.xml`.

Version 0.1.6 builds the manifest from the canonical Git archive bytes while
keeping strict size and SHA-256 verification. The release build now also fails
if the committed manifest is stale.

The Thor update ZIP contains only the fixed player APK and its update guide.
Quest mods are not bundled and remain optional. This is an in-place update
(`versionCode` 7); do not uninstall the app. Existing runtime data, optional
mods and saves remain in place.
