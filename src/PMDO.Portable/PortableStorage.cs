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

public sealed record ModMetadata(
    string Name,
    string? GameVersion,
    IReadOnlyList<string> Warnings,
    string? ModType = null,
    string? DirectoryName = null,
    bool IsGameVersionCompatible = true,
    bool HasUnsupportedFiles = false);
public static class ModXml
{
    private static readonly Version PlayerVersion = new(0, 8, 12, 0);

    public static ModMetadata Parse(Stream stream)
    {
        var doc = XDocument.Load(stream); var root = doc.Root ?? throw new PortableImportException("Invalid Mod.xml.");
        if (!root.Name.LocalName.Equals("Mod", StringComparison.OrdinalIgnoreCase) && !root.Name.LocalName.Equals("Header", StringComparison.OrdinalIgnoreCase))
            throw new PortableImportException("Mod.xml root must be Header or Mod.");
        var name = root.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("Name", StringComparison.OrdinalIgnoreCase))?.Value.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new PortableImportException("Mod.xml has no Name.");
        var version = root.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("GameVersion", StringComparison.OrdinalIgnoreCase))?.Value.Trim();
        var modType = root.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("ModType", StringComparison.OrdinalIgnoreCase))?.Value.Trim();
        bool compatible = String.IsNullOrWhiteSpace(version) || Version.TryParse(version, out Version? required) && required <= PlayerVersion;
        IReadOnlyList<string> warnings = compatible
            ? []
            : Version.TryParse(version, out _)
                ? ["Mod requires PMDO " + version + "; this player provides 0.8.12."]
                : ["Mod has an invalid GameVersion: " + version + "."];
        return new(name, version, warnings, modType, IsGameVersionCompatible: compatible);
    }
}

public static class ModCompatibility
{
    private static readonly HashSet<string> UnsupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".so", ".dylib", ".bat", ".cmd", ".ps1", ".sh"
    };

    public static bool IsUnsupportedFile(string path) => UnsupportedExtensions.Contains(Path.GetExtension(path));
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
        bool hasUnsupportedFiles = false;
        long expanded = 0;
        try
        {
            foreach (var original in tree.Files)
            {
                token.ThrowIfCancellationRequested(); var path = wrapper.Length == 0 ? original : original.StartsWith(wrapper + "/", StringComparison.Ordinal) ? original[(wrapper.Length + 1)..] : null;
                if (path is null) continue;
                if (ModCompatibility.IsUnsupportedFile(path))
                {
                    hasUnsupportedFiles = true;
                    warnings.Add("Native/executable file is unsupported on Android: " + path);
                }
                var target = SafePaths.Under(staging, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); await using (var i = tree.OpenRead(original)) await using (var o = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) expanded += await BoundedStreams.CopyAsync(i, o, Math.Min(ImportLimits.MaxEntryBytes, ImportLimits.MaxExpandedBytes - expanded), token).ConfigureAwait(false);
            }
            var targetDir = Path.Combine(mods, id); Directory.CreateDirectory(mods);
            AtomicDirectory.Replace(staging, targetDir);
            return meta with { Warnings = warnings, DirectoryName = id, HasUnsupportedFiles = hasUnsupportedFiles };
        }
        catch { if (Directory.Exists(staging)) Directory.Delete(staging, true); throw; }
    }
}

public sealed record ModConfigurationState(string? QuestDirectoryName, IReadOnlyList<string> EnabledModDirectoryNames);
public sealed record InstalledMod(string DirectoryName, ModMetadata Metadata, bool Enabled);

public sealed class ModCatalog(string appRoot)
{
    private readonly string root = Path.GetFullPath(appRoot);

    public IReadOnlyList<InstalledMod> Installed()
    {
        string modsRoot = Path.Combine(root, "MODS");
        if (!Directory.Exists(modsRoot)) return [];
        ModConfigurationState state = ModConfiguration.Load(root);
        var enabledMods = state.EnabledModDirectoryNames.ToHashSet(StringComparer.Ordinal);
        var result = new List<InstalledMod>();
        foreach (string directory in Directory.EnumerateDirectories(modsRoot).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            string directoryName = Path.GetFileName(directory);
            if (directoryName.StartsWith(".staging-", StringComparison.Ordinal)) continue;
            string manifest = Path.Combine(directory, "Mod.xml");
            if (!File.Exists(manifest)) continue;
            try
            {
                using Stream input = File.OpenRead(manifest);
                ModMetadata metadata = ModXml.Parse(input);
                bool hasUnsupportedFiles = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                    .Any(ModCompatibility.IsUnsupportedFile);
                if (hasUnsupportedFiles && !metadata.Warnings.Any(warning => warning.Contains("unsupported on Android", StringComparison.OrdinalIgnoreCase)))
                    metadata = metadata with { Warnings = metadata.Warnings.Concat(["Mod contains native/executable files that are unsupported on Android."]).ToArray() };
                metadata = metadata with { DirectoryName = directoryName, HasUnsupportedFiles = hasUnsupportedFiles };
                bool quest = String.Equals(metadata.ModType, "Quest", StringComparison.OrdinalIgnoreCase);
                bool enabled = quest
                    ? String.Equals(state.QuestDirectoryName, directoryName, StringComparison.Ordinal)
                    : enabledMods.Contains(directoryName);
                result.Add(new InstalledMod(directoryName, metadata, enabled));
            }
            catch (Exception)
            {
                // A malformed folder is not safe to expose as a selectable mod or save target.
            }
        }
        return result;
    }
}

public static class ModConfiguration
{
    public static ModConfigurationState Load(string appRoot)
    {
        string configPath = Path.Combine(Path.GetFullPath(appRoot), "CONFIG", "ModConfig.xml");
        if (!File.Exists(configPath)) return new(null, []);
        XDocument config = XDocument.Load(configPath);
        XElement root = config.Root ?? throw new PortableImportException("Invalid ModConfig.xml.");
        string? quest = DirectoryNameFromConfigPath(root.Element("Quest")?.Value);
        string[] mods = (root.Element("Mods")?.Elements("Mod") ?? [])
            .Select(element => DirectoryNameFromConfigPath(element.Value))
            .Where(name => name is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new(quest, mods);
    }

    public static void Save(string appRoot, string? questDirectoryName, IEnumerable<string> enabledModDirectoryNames)
    {
        string root = Path.GetFullPath(appRoot);
        if (questDirectoryName is not null) ValidateInstalled(root, questDirectoryName, expectQuest: true);
        string[] mods = enabledModDirectoryNames.Distinct(StringComparer.Ordinal).ToArray();
        foreach (string mod in mods) ValidateInstalled(root, mod, expectQuest: false);

        string configDir = Path.Combine(root, "CONFIG");
        Directory.CreateDirectory(configDir);
        string configPath = Path.Combine(configDir, "ModConfig.xml");
        var config = new XDocument(new XElement("Config",
            new XElement("Quest", questDirectoryName is null ? String.Empty : "MODS/" + questDirectoryName),
            new XElement("Mods", mods.Select(mod => new XElement("Mod", "MODS/" + mod)))));
        WriteAtomic(configPath, config);
    }

    public static void Enable(string appRoot, ModMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.DirectoryName)) throw new PortableImportException("Imported mod has no directory name.");
        ModConfigurationState state = Load(appRoot);
        if (String.Equals(metadata.ModType, "Quest", StringComparison.OrdinalIgnoreCase))
            Save(appRoot, metadata.DirectoryName, state.EnabledModDirectoryNames);
        else
            Save(appRoot, state.QuestDirectoryName, state.EnabledModDirectoryNames.Append(metadata.DirectoryName));
    }

    public static void Disable(string appRoot, ModMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.DirectoryName)) throw new PortableImportException("Imported mod has no directory name.");
        ModConfigurationState state = Load(appRoot);
        string? selectedQuest = String.Equals(state.QuestDirectoryName, metadata.DirectoryName, StringComparison.Ordinal)
            ? null
            : state.QuestDirectoryName;
        IEnumerable<string> enabledMods = state.EnabledModDirectoryNames
            .Where(name => !String.Equals(name, metadata.DirectoryName, StringComparison.Ordinal));
        Save(appRoot, selectedQuest, enabledMods);
    }

    public static IReadOnlyList<string> PreserveEnabledOrder(
        IEnumerable<string> previousOrder,
        IEnumerable<string> selectedDirectoryNames)
    {
        string[] selected = selectedDirectoryNames.Distinct(StringComparer.Ordinal).ToArray();
        var remaining = selected.ToHashSet(StringComparer.Ordinal);
        var ordered = new List<string>();
        foreach (string name in previousOrder)
            if (remaining.Remove(name)) ordered.Add(name);
        foreach (string name in selected)
            if (remaining.Remove(name)) ordered.Add(name);
        return ordered;
    }

    private static void ValidateInstalled(string appRoot, string directoryName, bool expectQuest)
    {
        string safeName = SafeDirectoryName(directoryName);
        string directory = SafePaths.Under(Path.Combine(appRoot, "MODS"), safeName);
        string manifest = Path.Combine(directory, "Mod.xml");
        if (!File.Exists(manifest)) throw new PortableImportException("Installed mod not found: " + directoryName);
        using Stream input = File.OpenRead(manifest);
        ModMetadata metadata = ModXml.Parse(input);
        bool isQuest = String.Equals(metadata.ModType, "Quest", StringComparison.OrdinalIgnoreCase);
        if (isQuest != expectQuest) throw new PortableImportException(expectQuest
            ? directoryName + " is not a Quest."
            : directoryName + " is a Quest and cannot be enabled as an additional mod.");
    }

    internal static string SafeDirectoryName(string directoryName)
    {
        string safe = SafePaths.Relative(directoryName);
        if (safe.Contains('/')) throw new PortableImportException("Mod directory must be a single path segment.");
        return safe;
    }

    private static string? DirectoryNameFromConfigPath(string? path)
    {
        if (String.IsNullOrWhiteSpace(path)) return null;
        try
        {
            string safe = SafePaths.Relative(path);
            string[] parts = safe.Split('/');
            return parts.Length == 2 && String.Equals(parts[0], "MODS", StringComparison.OrdinalIgnoreCase)
                ? SafeDirectoryName(parts[1])
                : null;
        }
        catch (PortableImportException) { return null; }
    }

    private static void WriteAtomic(string configPath, XDocument config)
    {
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

public sealed record SaveTarget(string DisplayName, string? QuestDirectoryName)
{
    public static SaveTarget BaseGame { get; } = new("PMDO base game", null);
    public static SaveTarget ForQuest(string directoryName, string displayName) => new(displayName, directoryName);
}

public sealed record RawSaveImportResult(SaveTarget Target, long Bytes, string Sha256, bool PreviousSaveBackedUp);

public sealed class RawSaveImporter(string appRoot)
{
    private const string TransactionDirectoryName = ".rssv-import-transaction";
    private const string PreviousDirectoryName = ".rssv-import-previous";
    private const string MainFile = "SAVE.rssv";
    private const string BackupFile = "SAVE.rssv.bak";
    private static readonly string[] AuxiliaryFiles = [BackupFile, "QUICKSAVE.rsqs", "SAVE.rssv.pending", "SAVE.rssv.write"];
    private readonly string root = Path.GetFullPath(appRoot);

    private sealed record Journal(string Phase, string Sha256, long Length, bool MainExisted);

    public async Task<RawSaveImportResult> ImportAsync(Stream source, SaveTarget requestedTarget, CancellationToken token = default)
    {
        (string targetDirectory, SaveTarget target) = ResolveTarget(requestedTarget);
        EnsureNotReparsePoint(targetDirectory, root);
        Directory.CreateDirectory(targetDirectory);
        EnsureNotReparsePoint(targetDirectory, root);
        await using FileStream importLock = AcquireLock(root, targetDirectory);
        RecoverTarget(targetDirectory);

        string transaction = Path.Combine(targetDirectory, TransactionDirectoryName);
        string originals = Path.Combine(transaction, "original");
        Directory.CreateDirectory(originals);
        string staged = Path.Combine(transaction, "new.rssv");
        Journal? journal = null;
        try
        {
            await using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await BoundedStreams.CopyAsync(source, output, ImportLimits.MaxEntryBytes, token).ConfigureAwait(false);
                output.Flush(true);
            }
            ValidateVersion(staged);
            long length = new FileInfo(staged).Length;
            string sha256 = HashFile(staged);
            string main = Path.Combine(targetDirectory, MainFile);
            bool mainExisted = File.Exists(main);
            if (mainExisted) CopyFileDurable(main, Path.Combine(originals, MainFile));
            journal = new Journal("Prepared", sha256, length, mainExisted);
            WriteJournal(transaction, journal);

            foreach (string fileName in AuxiliaryFiles)
            {
                string active = Path.Combine(targetDirectory, fileName);
                if (File.Exists(active)) File.Move(active, Path.Combine(originals, fileName), true);
            }
            journal = journal with { Phase = "Quarantined" };
            WriteJournal(transaction, journal);
            journal = journal with { Phase = "Switching" };
            WriteJournal(transaction, journal);

            if (mainExisted)
                File.Replace(staged, main, Path.Combine(targetDirectory, BackupFile), true);
            else
                File.Move(staged, main);

            FinalizeCommitted(targetDirectory, transaction, journal);
            return new RawSaveImportResult(target, length, sha256, mainExisted);
        }
        catch
        {
            if (journal is not null) RecoverTarget(targetDirectory);
            else if (Directory.Exists(transaction)) Directory.Delete(transaction, true);
            throw;
        }
    }

    public static void RecoverPending(string appRoot)
    {
        string root = Path.GetFullPath(appRoot);
        string saveRoot = Path.Combine(root, "SAVE");
        if (!Directory.Exists(saveRoot)) return;
        string[] pending = Directory.EnumerateDirectories(saveRoot, TransactionDirectoryName, SearchOption.AllDirectories).ToArray();
        foreach (string transaction in pending)
        {
            string targetDirectory = Directory.GetParent(transaction)?.FullName ?? throw new PortableImportException("Invalid save import transaction path.");
            string verified = Path.GetFullPath(targetDirectory);
            string savePrefix = Path.GetFullPath(saveRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!verified.StartsWith(savePrefix, StringComparison.Ordinal) && !String.Equals(verified, Path.GetFullPath(saveRoot), StringComparison.Ordinal))
                throw new PortableImportException("Save import transaction escaped the save root.");
            EnsureNotReparsePoint(targetDirectory, root);
            using FileStream importLock = AcquireLock(root, targetDirectory);
            RecoverTarget(targetDirectory);
        }
    }

    private (string Directory, SaveTarget Target) ResolveTarget(SaveTarget requested)
    {
        string saveRoot = Path.Combine(root, "SAVE");
        if (requested.QuestDirectoryName is null) return (saveRoot, SaveTarget.BaseGame);
        string safeName = ModConfiguration.SafeDirectoryName(requested.QuestDirectoryName);
        InstalledMod quest = new ModCatalog(root).Installed().SingleOrDefault(mod =>
            String.Equals(mod.DirectoryName, safeName, StringComparison.Ordinal) &&
            String.Equals(mod.Metadata.ModType, "Quest", StringComparison.OrdinalIgnoreCase))
            ?? throw new PortableImportException("Installed Quest not found: " + requested.QuestDirectoryName);
        string directory = SafePaths.Under(saveRoot, "MODS/" + quest.DirectoryName);
        return (directory, SaveTarget.ForQuest(quest.DirectoryName, quest.Metadata.Name));
    }

    private static FileStream AcquireLock(string appRoot, string targetDirectory)
    {
        string locks = Path.Combine(appRoot, ".save-import-locks");
        Directory.CreateDirectory(locks);
        string id = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(targetDirectory))));
        return new FileStream(Path.Combine(locks, id + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static void ValidateVersion(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length < 16) throw new PortableImportException("SAVE.rssv is truncated.");
        using var reader = new BinaryReader(input);
        int major = reader.ReadInt32(); int minor = reader.ReadInt32(); int build = reader.ReadInt32(); int revision = reader.ReadInt32();
        if (major != 0 || minor != 8 || build != 12 || revision != 0)
            throw new PortableImportException($"SAVE.rssv targets PMDO {major}.{minor}.{build}.{revision}, not 0.8.12.0.");
    }

    private static void RecoverTarget(string targetDirectory)
    {
        string transaction = Path.Combine(targetDirectory, TransactionDirectoryName);
        if (!Directory.Exists(transaction)) return;
        if (!File.Exists(Path.Combine(transaction, "journal.json")))
        {
            Directory.Delete(transaction, true);
            return;
        }
        Journal journal = ReadJournal(transaction);
        string main = Path.Combine(targetDirectory, MainFile);
        bool installed = journal.Phase is "Switching" or "Committed" &&
            File.Exists(main) && new FileInfo(main).Length == journal.Length &&
            StringComparer.OrdinalIgnoreCase.Equals(HashFile(main), journal.Sha256);
        if (installed) FinalizeCommitted(targetDirectory, transaction, journal);
        else Rollback(targetDirectory, transaction, journal);
    }

    private static void FinalizeCommitted(string targetDirectory, string transaction, Journal journal)
    {
        string main = Path.Combine(targetDirectory, MainFile);
        if (!File.Exists(main) || new FileInfo(main).Length != journal.Length ||
            !StringComparer.OrdinalIgnoreCase.Equals(HashFile(main), journal.Sha256))
            throw new PortableImportException("Imported SAVE.rssv failed its post-commit integrity check.");
        string originals = Path.Combine(transaction, "original");
        string backup = Path.Combine(targetDirectory, BackupFile);
        if (journal.MainExisted && !File.Exists(backup))
            CopyFileDurable(Path.Combine(originals, MainFile), backup);
        if (!journal.MainExisted && File.Exists(backup)) File.Delete(backup);
        foreach (string fileName in AuxiliaryFiles.Skip(1))
        {
            string active = Path.Combine(targetDirectory, fileName);
            if (File.Exists(active)) File.Delete(active);
        }
        WriteJournal(transaction, journal with { Phase = "Committed" });
        string previous = Path.Combine(targetDirectory, PreviousDirectoryName);
        if (Directory.Exists(previous)) Directory.Delete(previous, true);
        Directory.Move(transaction, previous);
    }

    private static void Rollback(string targetDirectory, string transaction, Journal journal)
    {
        string originals = Path.Combine(transaction, "original");
        string main = Path.Combine(targetDirectory, MainFile);
        string originalMain = Path.Combine(originals, MainFile);
        if (journal.MainExisted)
        {
            if (!File.Exists(originalMain)) throw new PortableImportException("Save import rollback is missing the original SAVE.rssv.");
            CopyFileDurable(originalMain, main);
        }
        else if (File.Exists(main)) File.Delete(main);

        foreach (string fileName in AuxiliaryFiles)
        {
            string active = Path.Combine(targetDirectory, fileName);
            string original = Path.Combine(originals, fileName);
            if (File.Exists(original)) CopyFileDurable(original, active);
            else if (!String.Equals(journal.Phase, "Prepared", StringComparison.Ordinal) && File.Exists(active))
                File.Delete(active);
        }
        Directory.Delete(transaction, true);
    }

    private static void WriteJournal(string transaction, Journal journal)
    {
        string path = Path.Combine(transaction, "journal.json");
        string temporary = path + ".tmp";
        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(output, journal);
            output.Flush(true);
        }
        File.Move(temporary, path, true);
    }

    private static Journal ReadJournal(string transaction)
    {
        string path = Path.Combine(transaction, "journal.json");
        if (!File.Exists(path)) throw new PortableImportException("Incomplete save import transaction has no journal.");
        using Stream input = File.OpenRead(path);
        return JsonSerializer.Deserialize<Journal>(input) ?? throw new PortableImportException("Invalid save import transaction journal.");
    }

    private static void CopyFileDurable(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var input = File.OpenRead(source))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output);
                output.Flush(true);
            }
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string HashFile(string path)
    {
        using Stream input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    private static void EnsureNotReparsePoint(string path, string allowedRoot)
    {
        string rootPath = Path.GetFullPath(allowedRoot).TrimEnd(Path.DirectorySeparatorChar);
        string targetPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        string rootPrefix = rootPath + Path.DirectorySeparatorChar;
        if (!String.Equals(targetPath, rootPath, StringComparison.Ordinal) &&
            !targetPath.StartsWith(rootPrefix, StringComparison.Ordinal))
            throw new PortableImportException("Save target escaped the app root.");

        for (DirectoryInfo? directory = new(targetPath); directory is not null; directory = directory.Parent)
        {
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new PortableImportException("Save target contains a reparse point.");
            if (String.Equals(directory.FullName.TrimEnd(Path.DirectorySeparatorChar), rootPath, StringComparison.Ordinal))
                return;
        }

        throw new PortableImportException("Save target escaped the app root.");
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
