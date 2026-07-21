using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace PMDO.Portable;

public sealed class PortableImportException(string message) : Exception(message);

public static class ImportLimits
{
    public const int MaxEntries = 50_000;
    public const int MaxPathLength = 512;
    public const long MaxArchiveBytes = 2L * 1024 * 1024 * 1024;
    public const long MaxEntryBytes = 1L * 1024 * 1024 * 1024;
    public const long MaxExpandedBytes = 4L * 1024 * 1024 * 1024;
    public const long MaxManifestBytes = 16L * 1024 * 1024;
}

public static class BoundedStreams
{
    public static async Task<long> CopyAsync(Stream input, Stream output, long maximumBytes, CancellationToken token = default)
    {
        if (maximumBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            int request = (int)Math.Min(buffer.Length, maximumBytes - total + 1);
            int read = await input.ReadAsync(buffer.AsMemory(0, request), token).ConfigureAwait(false);
            if (read == 0) return total;
            total += read;
            if (total > maximumBytes) throw new PortableImportException($"Import exceeds the {maximumBytes}-byte limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }
    }
}

public static class SafePaths
{
    public static string Relative(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new PortableImportException("Empty path.");
        path = path.Replace('\\', '/');
        if (path.Length > ImportLimits.MaxPathLength) throw new PortableImportException("Path is too long.");
        if (path.StartsWith('/') || Path.IsPathRooted(path) || path.Contains(':')) throw new PortableImportException("Rooted path.");
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(p => p is "." or "..")) throw new PortableImportException("Unsafe path.");
        return string.Join('/', parts);
    }

    public static string Under(string root, string relative)
    {
        relative = Relative(relative);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.Ordinal)) throw new PortableImportException("Path escapes root.");
        return full;
    }

    public static void EnsureNoCaseCollisions(IEnumerable<string> paths)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.Select(Relative))
        {
            if (seen.TryGetValue(path, out var prior) && !StringComparer.Ordinal.Equals(prior, path))
                throw new PortableImportException($"Case collision: {prior} / {path}");
            seen[path] = path;
        }
    }
}

internal static class AtomicDirectory
{
    public static void Replace(string staging, string target)
    {
        var backup = target + ".rollback";
        if (Directory.Exists(backup))
        {
            if (Directory.Exists(target)) Directory.Delete(backup, true);
            else Directory.Move(backup, target);
        }

        var movedExisting = false;
        try
        {
            if (Directory.Exists(target))
            {
                Directory.Move(target, backup);
                movedExisting = true;
            }
            Directory.Move(staging, target);
        }
        catch
        {
            if (!Directory.Exists(target) && Directory.Exists(backup))
                Directory.Move(backup, target);
            throw;
        }

        if (movedExisting && Directory.Exists(backup))
        {
            try { Directory.Delete(backup, true); }
            catch { /* The committed target is valid; a later import recovers cleanup. */ }
        }
    }
}

public interface IFileTree : IDisposable
{
    IReadOnlyList<string> Files { get; }
    Stream OpenRead(string relativePath);
}

public sealed class LocalFileTree : IFileTree
{
    private readonly string root;
    public IReadOnlyList<string> Files { get; }
    public LocalFileTree(string root)
    {
        this.root = Path.GetFullPath(root);
        if (!Directory.Exists(this.root)) throw new DirectoryNotFoundException(root);
        var files = new List<string>();
        Scan(this.root, "", files);
        SafePaths.EnsureNoCaseCollisions(files);
        Files = files.Order(StringComparer.Ordinal).ToArray();
    }
    private static void Scan(string absolute, string relative, List<string> output)
    {
        foreach (var item in Directory.EnumerateFileSystemEntries(absolute))
        {
            var info = new FileInfo(item);
            if (info.LinkTarget is not null) throw new PortableImportException("Symlinks are not allowed.");
            var name = Path.GetFileName(item); var child = relative.Length == 0 ? name : relative + "/" + name;
            if (Directory.Exists(item)) Scan(item, child, output); else output.Add(SafePaths.Relative(child));
        }
    }
    public Stream OpenRead(string relativePath) => new FileStream(SafePaths.Under(root, relativePath), FileMode.Open, FileAccess.Read, FileShare.Read);
    public void Dispose() { }
}

public sealed class ZipFileTree : IFileTree
{
    private readonly ZipArchive archive;
    private readonly Dictionary<string, ZipArchiveEntry> entries;
    public IReadOnlyList<string> Files { get; }
    public ZipFileTree(string zipPath)
    {
        if (new FileInfo(zipPath).Length > ImportLimits.MaxArchiveBytes) throw new PortableImportException("ZIP archive is too large.");
        archive = ZipFile.OpenRead(zipPath); entries = new(StringComparer.Ordinal);
        try
        {
            var archiveEntries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToArray();
            if (archiveEntries.Length > ImportLimits.MaxEntries) throw new PortableImportException("ZIP contains too many files.");
            long expanded = 0;
            foreach (var entry in archiveEntries)
            {
                if (entry.Length > ImportLimits.MaxEntryBytes) throw new PortableImportException("ZIP entry is too large.");
                if (entry.Length > ImportLimits.MaxExpandedBytes - expanded) throw new PortableImportException("ZIP expands beyond the size limit.");
                expanded += entry.Length;
            }
            var raw = archiveEntries.Select(e => (Entry: e, Path: SafePaths.Relative(e.FullName))).ToArray();
            SafePaths.EnsureNoCaseCollisions(raw.Select(x => x.Path));
            foreach (var item in raw)
                if (!entries.TryAdd(item.Path, item.Entry)) throw new PortableImportException("Duplicate ZIP entry.");
        }
        catch { archive.Dispose(); throw; }
        Files = entries.Keys.Order(StringComparer.Ordinal).ToArray();
    }
    public Stream OpenRead(string relativePath) => entries.TryGetValue(SafePaths.Relative(relativePath), out var entry) ? entry.Open() : throw new FileNotFoundException(relativePath);
    public void Dispose() => archive.Dispose();
}

public static class FileTreeExtensions
{
    public static string WrapperRoot(this IFileTree tree, string requiredFile)
    {
        requiredFile = SafePaths.Relative(requiredFile);
        if (tree.Files.Contains(requiredFile, StringComparer.Ordinal)) return "";
        var matches = tree.Files.Where(p => p.EndsWith("/" + requiredFile, StringComparison.Ordinal)).Select(p => p[..^(requiredFile.Length + 1)].Split('/')[0]).Distinct(StringComparer.Ordinal).ToArray();
        return matches.Length == 1 && tree.Files.All(p => p.StartsWith(matches[0] + "/", StringComparison.Ordinal)) ? matches[0] : throw new PortableImportException($"Missing {requiredFile} at root or a single wrapper root.");
    }
    public static Stream OpenRelative(this IFileTree tree, string wrapper, string path) => tree.OpenRead(wrapper.Length == 0 ? path : wrapper + "/" + path);
}

public sealed record RuntimeFile(string Path, long Size, string Sha256);
public sealed record RuntimeManifest(string Version, IReadOnlyList<RuntimeFile> Files);
public sealed record ImportResult(string ActivePath, string Version);
public sealed record RuntimeImportProgress(int CompletedFiles, int TotalFiles, long CompletedBytes, long TotalBytes);

public sealed class RuntimeImporter(string appRoot)
{
    private readonly string root = Path.GetFullPath(appRoot);
    public async Task<ImportResult> ImportAsync(IFileTree source, RuntimeManifest manifest, CancellationToken cancellationToken = default, IProgress<RuntimeImportProgress>? progress = null)
    {
        if (manifest.Version != "0.8.12") throw new PortableImportException("Unsupported PMDO version.");
        SafePaths.EnsureNoCaseCollisions(manifest.Files.Select(f => f.Path));
        long totalBytes = manifest.Files.Sum(file => file.Size);
        int completedFiles = 0;
        long completedBytes = 0;
        progress?.Report(new RuntimeImportProgress(0, manifest.Files.Count, 0, totalBytes));
        var staging = Path.Combine(root, "runtime", ".staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            foreach (var file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = SafePaths.Relative(file.Path);
                if (!source.Files.Contains(path, StringComparer.Ordinal)) throw new PortableImportException($"Missing runtime file: {path}");
                var target = SafePaths.Under(staging, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (file.Size < 0) throw new PortableImportException($"Invalid runtime size: {path}");
                await using (var input = source.OpenRead(path)) await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { await BoundedStreams.CopyAsync(input, output, file.Size, cancellationToken).ConfigureAwait(false); await output.FlushAsync(cancellationToken).ConfigureAwait(false); }
                var info = new FileInfo(target);
                if (info.Length != file.Size || !StringComparer.OrdinalIgnoreCase.Equals(Hash(target), file.Sha256)) throw new PortableImportException($"Runtime integrity failure: {path}");
                completedFiles++;
                completedBytes += file.Size;
                if (completedFiles == manifest.Files.Count || completedFiles % 25 == 0)
                    progress?.Report(new RuntimeImportProgress(completedFiles, manifest.Files.Count, completedBytes, totalBytes));
            }
            var final = Path.Combine(root, "runtime", "versions", manifest.Version + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.GetDirectoryName(final)!); Directory.Move(staging, final);
            var pointer = Path.Combine(root, "runtime", "active.txt"); Directory.CreateDirectory(Path.GetDirectoryName(pointer)!);
            var temporary = pointer + ".tmp"; await File.WriteAllTextAsync(temporary, final, cancellationToken); File.Move(temporary, pointer, true);
            return new ImportResult(final, manifest.Version);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
    public string? ActivePath() { var p = Path.Combine(root, "runtime", "active.txt"); return File.Exists(p) ? File.ReadAllText(p) : null; }
    private static string Hash(string file) { using var input = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(input)); }
}

public sealed record ModMetadata(string Name, string? GameVersion, IReadOnlyList<string> Warnings, string? ModType = null, string? DirectoryName = null);
public static class ModXml
{
    public static ModMetadata Parse(Stream stream)
    {
        var doc = XDocument.Load(stream); var root = doc.Root ?? throw new PortableImportException("Invalid Mod.xml.");
        if (!root.Name.LocalName.Equals("Mod", StringComparison.OrdinalIgnoreCase) && !root.Name.LocalName.Equals("Header", StringComparison.OrdinalIgnoreCase))
            throw new PortableImportException("Mod.xml root must be Header or Mod.");
        var name = root.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("Name", StringComparison.OrdinalIgnoreCase))?.Value.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new PortableImportException("Mod.xml has no Name.");
        var version = root.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("GameVersion", StringComparison.OrdinalIgnoreCase))?.Value.Trim();
        var modType = root.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("ModType", StringComparison.OrdinalIgnoreCase))?.Value.Trim();
        var supportedVersion = version is null || version is "0.8.12" or "0.8.12.0";
        return new(name, version, supportedVersion ? [] : ["Mod targets PMDO " + version + ", not 0.8.12."] , modType);
    }
}

public sealed class ModImporter(string appRoot)
{
    private readonly string mods = Path.Combine(Path.GetFullPath(appRoot), "MODS");
    public async Task<ModMetadata> ImportAsync(IFileTree tree, CancellationToken token = default)
    {
        if (tree.Files.Count > ImportLimits.MaxEntries) throw new PortableImportException("Mod contains too many files.");
        var wrapper = tree.WrapperRoot("Mod.xml"); using var xml = tree.OpenRelative(wrapper, "Mod.xml"); var meta = ModXml.Parse(xml);
        var id = string.Concat(meta.Name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_')).Trim('_'); if (id.Length == 0) throw new PortableImportException("Invalid mod name.");
        var warnings = meta.Warnings.ToList(); var staging = Path.Combine(mods, ".staging-" + Guid.NewGuid().ToString("N"));
        long expanded = 0;
        try
        {
            foreach (var original in tree.Files)
            {
                token.ThrowIfCancellationRequested(); var path = wrapper.Length == 0 ? original : original.StartsWith(wrapper + "/", StringComparison.Ordinal) ? original[(wrapper.Length + 1)..] : null;
                if (path is null) continue; if (IsNative(path)) warnings.Add("Native/executable file is unsupported on Android: " + path); var target = SafePaths.Under(staging, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); await using (var i = tree.OpenRead(original)) await using (var o = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) expanded += await BoundedStreams.CopyAsync(i, o, Math.Min(ImportLimits.MaxEntryBytes, ImportLimits.MaxExpandedBytes - expanded), token).ConfigureAwait(false);
            }
            var targetDir = Path.Combine(mods, id); Directory.CreateDirectory(mods);
            AtomicDirectory.Replace(staging, targetDir);
            return meta with { Warnings = warnings, DirectoryName = id };
        }
        catch { if (Directory.Exists(staging)) Directory.Delete(staging, true); throw; }
    }
    private static bool IsNative(string path) => new[] { ".dll", ".exe", ".so", ".dylib", ".bat", ".cmd", ".ps1", ".sh" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}

public static class ModConfiguration
{
    public static void Enable(string appRoot, ModMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.DirectoryName)) throw new PortableImportException("Imported mod has no directory name.");
        string configDir = Path.Combine(Path.GetFullPath(appRoot), "CONFIG");
        Directory.CreateDirectory(configDir);
        string configPath = Path.Combine(configDir, "ModConfig.xml");
        XDocument config = File.Exists(configPath) ? XDocument.Load(configPath) : new XDocument(new XElement("Config", new XElement("Quest", ""), new XElement("Mods")));
        XElement root = config.Root ?? throw new PortableImportException("Invalid ModConfig.xml.");
        XElement quest = root.Element("Quest") ?? new XElement("Quest", "");
        XElement mods = root.Element("Mods") ?? new XElement("Mods");
        if (quest.Parent == null) root.Add(quest);
        if (mods.Parent == null) root.Add(mods);
        string relative = "MODS/" + metadata.DirectoryName;
        if (string.Equals(metadata.ModType, "Quest", StringComparison.OrdinalIgnoreCase))
            quest.Value = relative;
        else if (!mods.Elements("Mod").Any(element => element.Value == relative))
            mods.Add(new XElement("Mod", relative));

        string temporary = configPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                config.Save(output);
                output.Flush(true);
            }
            File.Move(temporary, configPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed record SaveBackupManifest(int SchemaVersion, string PmdoVersion, DateTimeOffset CreatedUtc, IReadOnlyList<RuntimeFile> Files);
public sealed class SaveBackup(string appRoot)
{
    private readonly string save = Path.Combine(Path.GetFullPath(appRoot), "SAVE");
    public async Task ExportAsync(Stream destination, CancellationToken token = default)
    {
        Directory.CreateDirectory(save); using var zip = new ZipArchive(destination, ZipArchiveMode.Create, true); var files = new List<RuntimeFile>();
        foreach (var absolute in Directory.EnumerateFiles(save, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(save, absolute).Replace('\\', '/'); var entry = zip.CreateEntry("SAVE/" + relative, CompressionLevel.Optimal); await using var output = entry.Open(); await using var input = File.OpenRead(absolute); await input.CopyToAsync(output, token); files.Add(new(relative, new FileInfo(absolute).Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(absolute)))));
        }
        var manifest = zip.CreateEntry("manifest.json"); await using var writer = new StreamWriter(manifest.Open()); await writer.WriteAsync(JsonSerializer.Serialize(new SaveBackupManifest(1, "0.8.12", DateTimeOffset.UtcNow, files)));
    }
    public async Task ImportAsync(Stream source, CancellationToken token = default)
    {
        var staging = save + ".staging-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(staging);
            using var zip = new ZipArchive(source, ZipArchiveMode.Read, true);
            if (zip.Entries.Count > ImportLimits.MaxEntries + 1) throw new PortableImportException("Save backup contains too many entries.");
            var m = zip.GetEntry("manifest.json") ?? throw new PortableImportException("Missing save manifest.");
            if (m.Length > ImportLimits.MaxManifestBytes) throw new PortableImportException("Save manifest is too large.");
            SaveBackupManifest manifest; using (var reader = new StreamReader(m.Open())) manifest = JsonSerializer.Deserialize<SaveBackupManifest>(await reader.ReadToEndAsync(token).ConfigureAwait(false)) ?? throw new PortableImportException("Invalid save manifest.");
            if (manifest.SchemaVersion != 1 || manifest.PmdoVersion != "0.8.12" || manifest.Files is null) throw new PortableImportException("Unsupported save backup.");
            if (manifest.Files.Count > ImportLimits.MaxEntries) throw new PortableImportException("Save backup contains too many files.");
            SafePaths.EnsureNoCaseCollisions(manifest.Files.Select(f => f.Path));
            long expanded = 0;
            foreach (var file in manifest.Files)
            {
                token.ThrowIfCancellationRequested();
                if (file.Size < 0 || file.Size > ImportLimits.MaxEntryBytes || file.Size > ImportLimits.MaxExpandedBytes - expanded) throw new PortableImportException("Save backup exceeds the size limit.");
                var entry = zip.GetEntry("SAVE/" + SafePaths.Relative(file.Path)) ?? throw new PortableImportException("Missing save file.");
                if (entry.Length != file.Size) throw new PortableImportException("Save entry size does not match its manifest.");
                var target = SafePaths.Under(staging, file.Path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using (var i = entry.Open()) await using (var o = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) await BoundedStreams.CopyAsync(i, o, file.Size, token).ConfigureAwait(false);
                expanded += file.Size;
                using var hashInput = File.OpenRead(target);
                if (!StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(SHA256.HashData(hashInput)), file.Sha256)) throw new PortableImportException("Save integrity failure.");
            }
            AtomicDirectory.Replace(staging, save);
        }
        catch { if (Directory.Exists(staging)) Directory.Delete(staging, true); throw; }
    }
}
