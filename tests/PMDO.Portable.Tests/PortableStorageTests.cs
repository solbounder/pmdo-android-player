using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PMDO.Portable;
using Xunit;

namespace PMDO.Portable.Tests;

public sealed class PortableStorageTests : IDisposable
{
    [Fact]
    public void Duplicate_touch_bindings_release_only_after_last_source()
    {
        var state = new TouchHoldState<string, string>();
        Assert.True(state.Press("left-L", "L"));
        Assert.False(state.Press("right-L", "L"));
        Assert.False(state.Release("left-L", out string first));
        Assert.Equal("L", first);
        Assert.True(state.Release("right-L", out string last));
        Assert.Equal("L", last);

        Assert.True(state.Press("one", "ZR"));
        Assert.True(state.Press("two", "A"));
        Assert.Equal(new[] { "ZR", "A" }, state.Reset());
        Assert.False(state.Release("one", out _));
    }

    [Fact]
    public void Touch_layout_round_trips_and_allows_duplicate_bindings()
    {
        var layout = TouchLayoutV1.Default with { Buttons = TouchLayoutV1.Default.Buttons.Select(x => x with { Binding = "A" }).ToArray() };
        TouchLayoutV1 loaded = TouchLayoutStorage.DeserializeOrDefault(TouchLayoutStorage.Serialize(layout));
        Assert.All(loaded.Buttons, button => Assert.Equal("A", button.Binding));
    }

    [Fact]
    public void Touch_layout_invalid_or_out_of_range_values_are_safe()
    {
        TouchLayoutV1 invalidVersion = TouchLayoutStorage.DeserializeOrDefault("{\"Version\":99}");
        Assert.Equal(TouchLayoutV1.Default, invalidVersion);
        var bad = TouchLayoutV1.Default with { DPad = TouchLayoutV1.Default.DPad with { X = float.NaN, Scale = 99f }, Buttons = [] };
        TouchLayoutV1 normalized = TouchLayoutStorage.DeserializeOrDefault(TouchLayoutStorage.Serialize(bad));
        Assert.Equal(0f, normalized.DPad.X);
        Assert.Equal(2f, normalized.DPad.Scale);
        Assert.Equal(TouchLayoutV1.MaximumButtons, normalized.Buttons.Count);
    }
    private readonly string root = Path.Combine(Path.GetTempPath(), "pmdo-portable-" + Guid.NewGuid().ToString("N"));
    public PortableStorageTests() => Directory.CreateDirectory(root);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private string Dir(string name) { var path = Path.Combine(root, name); Directory.CreateDirectory(path); return path; }
    private static RuntimeFile FileManifest(string path, string text) => new(path, System.Text.Encoding.UTF8.GetByteCount(text), Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))));

    [Theory]
    [InlineData("../bad")]
    [InlineData("/bad")]
    [InlineData("C:/bad")]
    public void Relative_rejects_unsafe_paths(string path) => Assert.Throws<PortableImportException>(() => SafePaths.Relative(path));

    [Fact]
    public void Case_collisions_are_rejected() => Assert.Throws<PortableImportException>(() => SafePaths.EnsureNoCaseCollisions(["Data/A.txt", "data/a.txt"]));

    [Fact]
    public void Paths_over_the_import_limit_are_rejected() =>
        Assert.Throws<PortableImportException>(() => SafePaths.Relative(new string('a', ImportLimits.MaxPathLength + 1)));

    [Theory]
    [InlineData(1920, 1080, 320, 60, 1280, 960, 4)]
    [InlineData(2400, 1080, 560, 60, 1280, 960, 4)]
    [InlineData(1280, 720, 160, 0, 960, 720, 3)]
    public void Viewport_is_integer_scaled_and_centered(int availableWidth, int availableHeight, int left, int top, int width, int height, int scale)
    {
        ViewportPlacement placement = ViewportLayout.Calculate(availableWidth, availableHeight, 320, 240);
        Assert.Equal(new ViewportPlacement(left, top, width, height, scale), placement);
    }

    [Theory]
    [InlineData(1920, 1080, 0, 0, 0, 1280, 960, 4, 1.5f, 1.125f)]
    [InlineData(1920, 1080, 4, 320, 60, 1280, 960, 4, 1f, 1f)]
    [InlineData(1920, 1080, 5, 240, 0, 1280, 960, 4, 1.125f, 1.125f)]
    [InlineData(2400, 1080, 0, 0, 0, 1280, 960, 4, 1.875f, 1.125f)]
    public void Android_viewport_supports_stretched_and_aspect_modes(
        int availableWidth, int availableHeight, int mode,
        int left, int top, int width, int height, int renderScale,
        float scaleX, float scaleY)
    {
        AndroidViewportPlacement placement = ViewportLayout.CalculateAndroid(
            availableWidth, availableHeight, 320, 240, mode);

        Assert.Equal(left, placement.Left);
        Assert.Equal(top, placement.Top);
        Assert.Equal(width, placement.Width);
        Assert.Equal(height, placement.Height);
        Assert.Equal(renderScale, placement.RenderScale);
        Assert.Equal(scaleX, placement.ScaleX, 5);
        Assert.Equal(scaleY, placement.ScaleY, 5);
    }

    [Fact]
    public async Task Bounded_copy_stops_before_writing_past_limit()
    {
        await using var input = new MemoryStream(new byte[9]);
        await using var output = new MemoryStream();
        await Assert.ThrowsAsync<PortableImportException>(() => BoundedStreams.CopyAsync(input, output, 8));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task Runtime_import_is_verified_and_atomically_activates()
    {
        var source = Dir("source"); Directory.CreateDirectory(Path.Combine(source, "Data")); await File.WriteAllTextAsync(Path.Combine(source, "Data", "x.txt"), "hello");
        using var tree = new LocalFileTree(source); var importer = new RuntimeImporter(Dir("app"));
        var result = await importer.ImportAsync(tree, new RuntimeManifest("0.8.12", [FileManifest("Data/x.txt", "hello")]));
        Assert.Equal(result.ActivePath, importer.ActivePath()); Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(result.ActivePath, "Data", "x.txt")));
    }

    [Fact]
    public async Task Runtime_import_reports_initial_and_completed_progress()
    {
        var source = Dir("progress-source");
        await File.WriteAllTextAsync(Path.Combine(source, "one.txt"), "one");
        await File.WriteAllTextAsync(Path.Combine(source, "two.txt"), "two");
        using var tree = new LocalFileTree(source);
        var reports = new List<RuntimeImportProgress>();
        var progress = new SynchronousProgress<RuntimeImportProgress>(reports.Add);

        await new RuntimeImporter(Dir("progress-app")).ImportAsync(tree,
            new RuntimeManifest("0.8.12", [FileManifest("one.txt", "one"), FileManifest("two.txt", "two")]), default, progress);

        Assert.Equal(0, reports.First().CompletedFiles);
        Assert.Equal(2, reports.Last().CompletedFiles);
        Assert.Equal(6, reports.Last().CompletedBytes);
    }

    [Fact]
    public async Task Runtime_bad_hash_keeps_previous_active()
    {
        var source = Dir("source"); await File.WriteAllTextAsync(Path.Combine(source, "x"), "good"); using var tree = new LocalFileTree(source); var importer = new RuntimeImporter(Dir("app"));
        var valid = new RuntimeManifest("0.8.12", [FileManifest("x", "good")]); await importer.ImportAsync(tree, valid); var previous = importer.ActivePath();
        await Assert.ThrowsAsync<PortableImportException>(() => importer.ImportAsync(tree, new RuntimeManifest("0.8.12", [FileManifest("x", "bad")])));
        Assert.Equal(previous, importer.ActivePath());
    }

    [Fact]
    public void Zip_tree_rejects_traversal_and_finds_wrapper()
    {
        var zip = Path.Combine(root, "mod.zip"); using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) { using var writer = new StreamWriter(archive.CreateEntry("wrapped/Mod.xml").Open()); writer.Write("<Mod><Name>Test</Name></Mod>"); }
        using var tree = new ZipFileTree(zip); Assert.Equal("wrapped", tree.WrapperRoot("Mod.xml"));
        var unsafeZip = Path.Combine(root, "unsafe.zip"); using (var archive = ZipFile.Open(unsafeZip, ZipArchiveMode.Create)) archive.CreateEntry("../bad");
        Assert.Throws<PortableImportException>(() => new ZipFileTree(unsafeZip));
    }

    [Fact]
    public async Task Mod_import_preserves_content_accepts_older_version_and_warns_for_native_files()
    {
        var source = Dir("mod"); await File.WriteAllTextAsync(Path.Combine(source, "Mod.xml"), "<Mod><Name>My Mod</Name><GameVersion>0.8.11</GameVersion></Mod>"); await File.WriteAllTextAsync(Path.Combine(source, "script.lua"), "return 1"); await File.WriteAllTextAsync(Path.Combine(source, "native.dll"), "native");
        using var tree = new LocalFileTree(source); var meta = await new ModImporter(Dir("app")).ImportAsync(tree);
        Assert.DoesNotContain(meta.Warnings, x => x.Contains("0.8.11")); Assert.Contains(meta.Warnings, x => x.Contains("native.dll")); Assert.True(meta.IsGameVersionCompatible); Assert.True(meta.HasUnsupportedFiles); Assert.Equal("return 1", await File.ReadAllTextAsync(Path.Combine(root, "app", "MODS", "My_Mod", "script.lua"))); Assert.Equal("native", await File.ReadAllTextAsync(Path.Combine(root, "app", "MODS", "My_Mod", "native.dll")));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("0.0", true)]
    [InlineData("0.8.11", true)]
    [InlineData("0.8.12.0", true)]
    [InlineData("0.8.13", false)]
    [InlineData("not-a-version", false)]
    public void Mod_version_compatibility_matches_upstream_minimum_version_semantics(string? gameVersion, bool compatible)
    {
        string versionElement = gameVersion is null ? String.Empty : "<GameVersion>" + gameVersion + "</GameVersion>";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<Header><Name>Test</Name>" + versionElement + "<ModType>Mod</ModType></Header>"));
        ModMetadata metadata = ModXml.Parse(stream);
        Assert.Equal(compatible, metadata.IsGameVersionCompatible);
        Assert.Equal(compatible, metadata.Warnings.Count == 0);
    }

    [Fact]
    public async Task Quest_header_used_by_real_mods_imports_without_rewriting()
    {
        var source = Dir("quest");
        await File.WriteAllTextAsync(Path.Combine(source, "Mod.xml"), "<Header><Name>Echoes of the Abyss</Name><GameVersion>0.8.12.0</GameVersion><ModType>Quest</ModType></Header>");
        await File.WriteAllTextAsync(Path.Combine(source, "script.lua"), "return 'unchanged'");

        using var tree = new LocalFileTree(source);
        var meta = await new ModImporter(Dir("app")).ImportAsync(tree);

        Assert.Equal("Echoes of the Abyss", meta.Name);
        Assert.Equal("0.8.12.0", meta.GameVersion);
        Assert.Equal("Quest", meta.ModType);
        Assert.Equal("Echoes_of_the_Abyss", meta.DirectoryName);
        Assert.Empty(meta.Warnings);
        Assert.Equal("return 'unchanged'", await File.ReadAllTextAsync(Path.Combine(root, "app", "MODS", meta.DirectoryName!, "script.lua")));
    }

    [Fact]
    public void Mod_configuration_is_replaced_as_complete_xml()
    {
        var app = Dir("configured-app");
        Directory.CreateDirectory(Path.Combine(app, "MODS", "Quest_A"));
        File.WriteAllText(Path.Combine(app, "MODS", "Quest_A", "Mod.xml"), "<Header><Name>Quest</Name><ModType>Quest</ModType></Header>");
        Directory.CreateDirectory(Path.Combine(app, "MODS", "Patch_B"));
        File.WriteAllText(Path.Combine(app, "MODS", "Patch_B", "Mod.xml"), "<Header><Name>Patch</Name><ModType>Mod</ModType></Header>");
        ModConfiguration.Enable(app, new ModMetadata("Quest", "0.8.12", [], "Quest", "Quest_A"));
        ModConfiguration.Enable(app, new ModMetadata("Patch", "0.8.12", [], "Mod", "Patch_B"));

        var document = System.Xml.Linq.XDocument.Load(Path.Combine(app, "CONFIG", "ModConfig.xml"));
        Assert.Equal("MODS/Quest_A", document.Root!.Element("Quest")!.Value);
        Assert.Equal("MODS/Patch_B", document.Root.Element("Mods")!.Element("Mod")!.Value);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(app, "CONFIG"), "*.tmp-*"));
    }

    [Fact]
    public async Task Mod_catalog_and_configuration_manage_quest_and_mods_individually()
    {
        var app = Dir("managed-mods");
        async Task Install(string directory, string name, string type, string? extraFile = null)
        {
            string path = Path.Combine(app, "MODS", directory); Directory.CreateDirectory(path);
            await File.WriteAllTextAsync(Path.Combine(path, "Mod.xml"), $"<Header><Name>{name}</Name><GameVersion>0.8.12</GameVersion><ModType>{type}</ModType></Header>");
            if (extraFile is not null) await File.WriteAllTextAsync(Path.Combine(path, extraFile), "x");
        }
        await Install("Quest_A", "Quest A", "Quest");
        await Install("Mod_B", "Mod B", "Mod");
        await Install("Native_C", "Native C", "Mod", "plugin.dll");

        ModConfiguration.Save(app, "Quest_A", ["Mod_B"]);
        ModConfigurationState state = ModConfiguration.Load(app);
        Assert.Equal("Quest_A", state.QuestDirectoryName);
        Assert.Equal(["Mod_B"], state.EnabledModDirectoryNames);

        IReadOnlyList<InstalledMod> installed = new ModCatalog(app).Installed();
        Assert.True(installed.Single(mod => mod.DirectoryName == "Quest_A").Enabled);
        Assert.True(installed.Single(mod => mod.DirectoryName == "Mod_B").Enabled);
        Assert.False(installed.Single(mod => mod.DirectoryName == "Native_C").Enabled);
        Assert.True(installed.Single(mod => mod.DirectoryName == "Native_C").Metadata.HasUnsupportedFiles);

        ModConfiguration.Save(app, null, ["Native_C"]);
        state = ModConfiguration.Load(app);
        Assert.Null(state.QuestDirectoryName);
        Assert.Equal(["Native_C"], state.EnabledModDirectoryNames);
    }

    [Fact]
    public async Task Mod_configuration_rejects_wrong_type_and_unsafe_directory()
    {
        var app = Dir("invalid-managed-mods");
        string quest = Path.Combine(app, "MODS", "Quest"); Directory.CreateDirectory(quest);
        await File.WriteAllTextAsync(Path.Combine(quest, "Mod.xml"), "<Header><Name>Quest</Name><ModType>Quest</ModType></Header>");
        Assert.Throws<PortableImportException>(() => ModConfiguration.Save(app, null, ["Quest"]));
        Assert.Throws<PortableImportException>(() => ModConfiguration.Save(app, "../Quest", []));
    }

    [Fact]
    public async Task Disabling_a_reimported_incompatible_mod_removes_its_existing_selection()
    {
        var app = Dir("disable-reimport");
        string questDirectory = Path.Combine(app, "MODS", "Quest_A"); Directory.CreateDirectory(questDirectory);
        await File.WriteAllTextAsync(Path.Combine(questDirectory, "Mod.xml"), "<Header><Name>Quest A</Name><ModType>Quest</ModType></Header>");
        string modDirectory = Path.Combine(app, "MODS", "Mod_B"); Directory.CreateDirectory(modDirectory);
        await File.WriteAllTextAsync(Path.Combine(modDirectory, "Mod.xml"), "<Header><Name>Mod B</Name><ModType>Mod</ModType></Header>");
        ModConfiguration.Save(app, "Quest_A", ["Mod_B"]);

        ModConfiguration.Disable(app, new ModMetadata("Mod B", "0.8.13", ["incompatible"], "Quest", "Mod_B", false));
        ModConfigurationState state = ModConfiguration.Load(app);
        Assert.Equal("Quest_A", state.QuestDirectoryName);
        Assert.Empty(state.EnabledModDirectoryNames);

        ModConfiguration.Disable(app, new ModMetadata("Quest A", "0.8.13", ["incompatible"], "Mod", "Quest_A", false));
        state = ModConfiguration.Load(app);
        Assert.Null(state.QuestDirectoryName);
    }

    [Fact]
    public void Mod_selection_preserves_existing_override_order_and_appends_new_choices()
    {
        IReadOnlyList<string> ordered = ModConfiguration.PreserveEnabledOrder(
            ["Z_Last", "A_First", "Removed"],
            ["A_First", "New_Mod", "Z_Last"]);

        Assert.Equal(["Z_Last", "A_First", "New_Mod"], ordered);
    }

    [Fact]
    public async Task Save_import_rejects_declared_oversize_before_writing()
    {
        var app = Dir("oversize-save-app");
        await using var stream = new MemoryStream();
        await using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open());
            await writer.WriteAsync(JsonSerializer.Serialize(new SaveBackupManifest(1, "0.8.12", DateTimeOffset.UtcNow,
                [new RuntimeFile("large.dat", ImportLimits.MaxEntryBytes + 1, "00")])));
        }
        stream.Position = 0;

        await Assert.ThrowsAsync<PortableImportException>(() => new SaveBackup(app).ImportAsync(stream));
        Assert.False(Directory.Exists(Path.Combine(app, "SAVE")));
    }

    [Fact]
    public async Task Save_roundtrip_and_tamper_rolls_back()
    {
        var app = Dir("app"); var save = Path.Combine(app, "SAVE"); Directory.CreateDirectory(save); await File.WriteAllTextAsync(Path.Combine(save, "state.dat"), "original"); var backups = new SaveBackup(app);
        await using var stream = new MemoryStream(); await backups.ExportAsync(stream); stream.Position = 0; await File.WriteAllTextAsync(Path.Combine(save, "state.dat"), "changed"); await backups.ImportAsync(stream); Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(save, "state.dat")));
        var tampered = new MemoryStream(); await using (var zip = new ZipArchive(tampered, ZipArchiveMode.Create, true)) { using (var w = new StreamWriter(zip.CreateEntry("manifest.json").Open())) await w.WriteAsync("{\"schemaVersion\":1,\"pmdoVersion\":\"0.8.12\",\"createdUtc\":\"2026-01-01T00:00:00+00:00\",\"files\":[{\"path\":\"state.dat\",\"size\":3,\"sha256\":\"00\"}]} "); using var w2 = new StreamWriter(zip.CreateEntry("SAVE/state.dat").Open()); await w2.WriteAsync("bad"); } tampered.Position = 0;
        await Assert.ThrowsAsync<PortableImportException>(() => backups.ImportAsync(tampered)); Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(save, "state.dat")));
    }

    [Fact]
    public async Task Raw_save_import_into_empty_base_namespace_is_atomic_and_has_no_fake_backup()
    {
        var app = Dir("raw-empty");
        byte[] save = SaveBytes("new");
        await new RawSaveImporter(app).ImportAsync(new MemoryStream(save), SaveTarget.BaseGame);
        Assert.Equal(save, await File.ReadAllBytesAsync(Path.Combine(app, "SAVE", "SAVE.rssv")));
        Assert.False(File.Exists(Path.Combine(app, "SAVE", "SAVE.rssv.bak")));
        Assert.False(Directory.Exists(Path.Combine(app, "SAVE", ".rssv-import-transaction")));
    }

    [Fact]
    public async Task Raw_save_import_preserves_old_main_and_quarantines_conflicting_state()
    {
        var app = Dir("raw-existing"); string saveDir = Path.Combine(app, "SAVE"); Directory.CreateDirectory(saveDir);
        byte[] oldMain = SaveBytes("old"); byte[] newMain = SaveBytes("new");
        await File.WriteAllBytesAsync(Path.Combine(saveDir, "SAVE.rssv"), oldMain);
        await File.WriteAllTextAsync(Path.Combine(saveDir, "SAVE.rssv.bak"), "older");
        await File.WriteAllTextAsync(Path.Combine(saveDir, "QUICKSAVE.rsqs"), "quick");
        await File.WriteAllTextAsync(Path.Combine(saveDir, "SAVE.rssv.pending"), "pending");
        await File.WriteAllTextAsync(Path.Combine(saveDir, "SAVE.rssv.write"), "write");

        await new RawSaveImporter(app).ImportAsync(new MemoryStream(newMain), SaveTarget.BaseGame);

        Assert.Equal(newMain, await File.ReadAllBytesAsync(Path.Combine(saveDir, "SAVE.rssv")));
        Assert.Equal(oldMain, await File.ReadAllBytesAsync(Path.Combine(saveDir, "SAVE.rssv.bak")));
        Assert.False(File.Exists(Path.Combine(saveDir, "QUICKSAVE.rsqs")));
        Assert.False(File.Exists(Path.Combine(saveDir, "SAVE.rssv.pending")));
        Assert.False(File.Exists(Path.Combine(saveDir, "SAVE.rssv.write")));
        string previous = Path.Combine(saveDir, ".rssv-import-previous", "original");
        Assert.Equal("older", await File.ReadAllTextAsync(Path.Combine(previous, "SAVE.rssv.bak")));
        Assert.Equal("quick", await File.ReadAllTextAsync(Path.Combine(previous, "QUICKSAVE.rsqs")));
    }

    [Fact]
    public async Task Raw_save_import_rejects_wrong_version_without_touching_existing_state()
    {
        var app = Dir("raw-invalid"); string saveDir = Path.Combine(app, "SAVE"); Directory.CreateDirectory(saveDir);
        byte[] oldMain = SaveBytes("old"); await File.WriteAllBytesAsync(Path.Combine(saveDir, "SAVE.rssv"), oldMain);
        byte[] invalid = SaveBytes("bad", revision: 1);
        await Assert.ThrowsAsync<PortableImportException>(() => new RawSaveImporter(app).ImportAsync(new MemoryStream(invalid), SaveTarget.BaseGame));
        Assert.Equal(oldMain, await File.ReadAllBytesAsync(Path.Combine(saveDir, "SAVE.rssv")));
        Assert.False(Directory.Exists(Path.Combine(saveDir, ".rssv-import-transaction")));
    }

    [Fact]
    public async Task Raw_save_import_uses_only_an_installed_quest_target()
    {
        var app = Dir("raw-quest"); string quest = Path.Combine(app, "MODS", "Quest_A"); Directory.CreateDirectory(quest);
        await File.WriteAllTextAsync(Path.Combine(quest, "Mod.xml"), "<Header><Name>Quest A</Name><ModType>Quest</ModType></Header>");
        byte[] save = SaveBytes("quest");
        RawSaveImportResult result = await new RawSaveImporter(app).ImportAsync(new MemoryStream(save), SaveTarget.ForQuest("Quest_A", "forged label"));
        Assert.Equal("Quest A", result.Target.DisplayName);
        Assert.Equal(save, await File.ReadAllBytesAsync(Path.Combine(app, "SAVE", "MODS", "Quest_A", "SAVE.rssv")));
        await Assert.ThrowsAsync<PortableImportException>(() => new RawSaveImporter(app).ImportAsync(new MemoryStream(save), SaveTarget.ForQuest("../Quest_A", "unsafe")));
        await Assert.ThrowsAsync<PortableImportException>(() => new RawSaveImporter(app).ImportAsync(new MemoryStream(save), SaveTarget.ForQuest("Missing", "missing")));
    }

    [Fact]
    public async Task Raw_save_import_rejects_a_linked_save_ancestor_before_creating_the_target()
    {
        var app = Dir("raw-linked-save");
        string quest = Path.Combine(app, "MODS", "Quest_A"); Directory.CreateDirectory(quest);
        await File.WriteAllTextAsync(Path.Combine(quest, "Mod.xml"), "<Header><Name>Quest A</Name><ModType>Quest</ModType></Header>");
        string external = Dir("raw-linked-external");
        string save = Path.Combine(app, "SAVE"); Directory.CreateDirectory(save);
        Directory.CreateSymbolicLink(Path.Combine(save, "MODS"), external);

        await Assert.ThrowsAsync<PortableImportException>(() => new RawSaveImporter(app)
            .ImportAsync(new MemoryStream(SaveBytes("quest")), SaveTarget.ForQuest("Quest_A", "Quest A")));

        Assert.False(Directory.Exists(Path.Combine(external, "Quest_A")));
    }

    [Fact]
    public async Task Raw_save_recovery_finishes_a_switched_transaction_and_rolls_back_a_pre_switch_transaction()
    {
        var committedApp = Dir("raw-recover-commit"); string committedSave = Path.Combine(committedApp, "SAVE"); Directory.CreateDirectory(committedSave);
        byte[] newMain = SaveBytes("new"); await File.WriteAllBytesAsync(Path.Combine(committedSave, "SAVE.rssv"), newMain);
        string committedTransaction = Path.Combine(committedSave, ".rssv-import-transaction"); string committedOriginals = Path.Combine(committedTransaction, "original"); Directory.CreateDirectory(committedOriginals);
        byte[] oldMain = SaveBytes("old"); await File.WriteAllBytesAsync(Path.Combine(committedOriginals, "SAVE.rssv"), oldMain);
        string hash = Convert.ToHexString(SHA256.HashData(newMain));
        await File.WriteAllTextAsync(Path.Combine(committedTransaction, "journal.json"), JsonSerializer.Serialize(new { Phase = "Switching", Sha256 = hash, Length = newMain.LongLength, MainExisted = true }));
        RawSaveImporter.RecoverPending(committedApp);
        Assert.Equal(newMain, await File.ReadAllBytesAsync(Path.Combine(committedSave, "SAVE.rssv")));
        Assert.Equal(oldMain, await File.ReadAllBytesAsync(Path.Combine(committedSave, "SAVE.rssv.bak")));

        var rollbackApp = Dir("raw-recover-rollback"); string rollbackSave = Path.Combine(rollbackApp, "SAVE"); Directory.CreateDirectory(rollbackSave);
        await File.WriteAllBytesAsync(Path.Combine(rollbackSave, "SAVE.rssv"), oldMain);
        string rollbackTransaction = Path.Combine(rollbackSave, ".rssv-import-transaction"); string rollbackOriginals = Path.Combine(rollbackTransaction, "original"); Directory.CreateDirectory(rollbackOriginals);
        await File.WriteAllBytesAsync(Path.Combine(rollbackOriginals, "SAVE.rssv"), oldMain);
        await File.WriteAllTextAsync(Path.Combine(rollbackOriginals, "QUICKSAVE.rsqs"), "quick");
        await File.WriteAllTextAsync(Path.Combine(rollbackTransaction, "journal.json"), JsonSerializer.Serialize(new { Phase = "Quarantined", Sha256 = hash, Length = newMain.LongLength, MainExisted = true }));
        RawSaveImporter.RecoverPending(rollbackApp);
        Assert.Equal(oldMain, await File.ReadAllBytesAsync(Path.Combine(rollbackSave, "SAVE.rssv")));
        Assert.Equal("quick", await File.ReadAllTextAsync(Path.Combine(rollbackSave, "QUICKSAVE.rsqs")));
        Assert.False(Directory.Exists(rollbackTransaction));
    }

    [Fact]
    public async Task Raw_save_recovery_preserves_auxiliary_files_if_prepared_transaction_was_interrupted_mid_quarantine()
    {
        var app = Dir("raw-recover-prepared"); string save = Path.Combine(app, "SAVE"); Directory.CreateDirectory(save);
        byte[] oldMain = SaveBytes("old"); byte[] newMain = SaveBytes("new");
        await File.WriteAllBytesAsync(Path.Combine(save, "SAVE.rssv"), oldMain);
        await File.WriteAllTextAsync(Path.Combine(save, "SAVE.rssv.bak"), "backup");
        string transaction = Path.Combine(save, ".rssv-import-transaction"); string originals = Path.Combine(transaction, "original"); Directory.CreateDirectory(originals);
        await File.WriteAllBytesAsync(Path.Combine(originals, "SAVE.rssv"), oldMain);
        await File.WriteAllTextAsync(Path.Combine(originals, "QUICKSAVE.rsqs"), "quick");
        await File.WriteAllBytesAsync(Path.Combine(transaction, "new.rssv"), newMain);
        string hash = Convert.ToHexString(SHA256.HashData(newMain));
        await File.WriteAllTextAsync(Path.Combine(transaction, "journal.json"), JsonSerializer.Serialize(new { Phase = "Prepared", Sha256 = hash, Length = newMain.LongLength, MainExisted = true }));

        RawSaveImporter.RecoverPending(app);

        Assert.Equal(oldMain, await File.ReadAllBytesAsync(Path.Combine(save, "SAVE.rssv")));
        Assert.Equal("backup", await File.ReadAllTextAsync(Path.Combine(save, "SAVE.rssv.bak")));
        Assert.Equal("quick", await File.ReadAllTextAsync(Path.Combine(save, "QUICKSAVE.rsqs")));
        Assert.False(Directory.Exists(transaction));
    }

    [Fact]
    public async Task Empty_save_roundtrip_replaces_existing_save()
    {
        var sourceApp = Dir("empty-source");
        var destinationApp = Dir("empty-destination");
        Directory.CreateDirectory(Path.Combine(sourceApp, "SAVE"));
        Directory.CreateDirectory(Path.Combine(destinationApp, "SAVE"));
        await File.WriteAllTextAsync(Path.Combine(destinationApp, "SAVE", "old.dat"), "old");

        await using var stream = new MemoryStream();
        await new SaveBackup(sourceApp).ExportAsync(stream);
        stream.Position = 0;
        await new SaveBackup(destinationApp).ImportAsync(stream);

        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(destinationApp, "SAVE")));
    }

    [Fact]
    public async Task Cancellation_does_not_activate_runtime()
    {
        var source = Dir("source"); await File.WriteAllTextAsync(Path.Combine(source, "x"), "x"); using var tree = new LocalFileTree(source); using var cts = new CancellationTokenSource(); cts.Cancel(); var importer = new RuntimeImporter(Dir("app"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => importer.ImportAsync(tree, new RuntimeManifest("0.8.12", [FileManifest("x", "x")]), cts.Token)); Assert.Null(importer.ActivePath());
    }

    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private static byte[] SaveBytes(string payload, int revision = 0)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(0); writer.Write(8); writer.Write(12); writer.Write(revision); writer.Write(System.Text.Encoding.UTF8.GetBytes(payload)); writer.Flush();
        return stream.ToArray();
    }
}
