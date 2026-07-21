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
    public async Task Mod_import_preserves_content_warns_and_replaces_atomically()
    {
        var source = Dir("mod"); await File.WriteAllTextAsync(Path.Combine(source, "Mod.xml"), "<Mod><Name>My Mod</Name><GameVersion>0.8.11</GameVersion></Mod>"); await File.WriteAllTextAsync(Path.Combine(source, "script.lua"), "return 1"); await File.WriteAllTextAsync(Path.Combine(source, "native.dll"), "native");
        using var tree = new LocalFileTree(source); var meta = await new ModImporter(Dir("app")).ImportAsync(tree);
        Assert.Contains(meta.Warnings, x => x.Contains("0.8.11")); Assert.Contains(meta.Warnings, x => x.Contains("native.dll")); Assert.Equal("return 1", await File.ReadAllTextAsync(Path.Combine(root, "app", "MODS", "My_Mod", "script.lua"))); Assert.Equal("native", await File.ReadAllTextAsync(Path.Combine(root, "app", "MODS", "My_Mod", "native.dll")));
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
        ModConfiguration.Enable(app, new ModMetadata("Quest", "0.8.12", [], "Quest", "Quest_A"));
        ModConfiguration.Enable(app, new ModMetadata("Patch", "0.8.12", [], "Mod", "Patch_B"));

        var document = System.Xml.Linq.XDocument.Load(Path.Combine(app, "CONFIG", "ModConfig.xml"));
        Assert.Equal("MODS/Quest_A", document.Root!.Element("Quest")!.Value);
        Assert.Equal("MODS/Patch_B", document.Root.Element("Mods")!.Element("Mod")!.Value);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(app, "CONFIG"), "*.tmp-*"));
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
}
