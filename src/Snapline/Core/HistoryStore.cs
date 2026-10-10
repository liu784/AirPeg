using System.IO;
using System.Text.Json;

namespace Snapline.Core;

public sealed record HistoryEntry(Guid Id, string Name, string Extension, DateTime CreatedUtc, long Bytes);

/// <summary>Opt-in immutable copies. Only GUID-owned entries inside this folder may be removed.</summary>
public sealed class HistoryStore
{
    public const int DefaultCount = 100;
    public const long DefaultBytes = 256L * 1024 * 1024, MaxCaptureBytes = 64L * 1024 * 1024;
    private readonly string _folder;
    private readonly object _gate = new();
    private readonly int _maxCount;
    private readonly long _maxBytes;
    private readonly TimeSpan _retention;
    private volatile bool _enabled;
    private bool _unreadableEntries;
    public bool Enabled { get => _enabled; set => _enabled = value; }

    public HistoryStore(string folder, int maxCount = DefaultCount, long maxBytes = DefaultBytes, TimeSpan? retention = null)
    {
        _folder = Path.GetFullPath(folder);
        _maxCount = Math.Max(1, maxCount); _maxBytes = Math.Max(1, maxBytes);
        _retention = retention ?? TimeSpan.FromDays(7);
    }

    // History is never allowed to follow a redirected data directory.
    private void CheckFolder()
    {
        for (var dir = new DirectoryInfo(_folder); dir is not null; dir = dir.Parent)
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("History folder cannot be a reparse point.");
    }
    private string EntryFolder(Guid id) => Path.Combine(_folder, id.ToString("N"));
    private static void CheckEntry(string folder)
    {
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new IOException("Redirected history entry.");
        foreach (var file in Directory.EnumerateFileSystemEntries(folder))
            if ((File.GetAttributes(file) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new IOException("Unexpected history entry contents.");
    }
    private HistoryEntry Read(Guid id)
    {
        var folder = EntryFolder(id); CheckEntry(folder);
        var meta = Path.Combine(folder, "entry.json");
        if (new FileInfo(meta).Length > 8192) throw new IOException("Invalid history metadata.");
        var entry = JsonSerializer.Deserialize<HistoryEntry>(File.ReadAllText(meta)) ?? throw new IOException("Invalid history entry.");
        if (entry.Id != id || !Thumbnails.ImageExtensions.Contains(entry.Extension) ||
            entry.Extension != Path.GetExtension(entry.Extension) || string.IsNullOrEmpty(entry.Name))
            throw new IOException("Invalid history metadata.");
        var bytes = new FileInfo(Path.Combine(folder, "capture" + entry.Extension)).Length;
        return entry with { Name = Path.GetFileNameWithoutExtension(entry.Name) + entry.Extension, Bytes = bytes };
    }
    private List<HistoryEntry> Entries()
    {
        var entries = new List<HistoryEntry>();
        _unreadableEntries = false;
        if (!Directory.Exists(_folder)) return entries;
        foreach (var folder in Directory.EnumerateDirectories(_folder))
        {
            if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out var id)) continue;
            try { entries.Add(Read(id)); }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
            { _unreadableEntries = true; Log.Error($"History entry unavailable: {id:N}: {ex.Message}"); }
        }
        return entries.OrderByDescending(e => e.CreatedUtc).ThenBy(e => e.Id).ToList();
    }
    private void DeleteCore(Guid id)
    {
        DeleteFolder(EntryFolder(id));
    }
    private static void DeleteFolder(string folder)
    {
        if (!Directory.Exists(folder)) return;
        CheckEntry(folder);
        // Delete only files owned by this format; never recurse or follow links.
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(file);
            if (name != "entry.json" && !(name.StartsWith("capture.", StringComparison.Ordinal) && Thumbnails.IsImage(name)))
                throw new IOException("Unexpected file in history entry.");
        }
        foreach (var file in Directory.EnumerateFiles(folder)) File.Delete(file);
        Directory.Delete(folder, false);
    }
    private void PruneCore(DateTime now)
    {
        // Incomplete copies left by process interruption were never published.
        if (Directory.Exists(_folder))
            foreach (var folder in Directory.EnumerateDirectories(_folder, ".pending-*"))
                if (Guid.TryParseExact(Path.GetFileName(folder)[9..], "N", out _)) DeleteFolder(folder);
        long bytes = 0; int count = 0;
        foreach (var entry in Entries())
        {
            if (now - entry.CreatedUtc >= _retention || count >= _maxCount || bytes + entry.Bytes > _maxBytes)
                DeleteCore(entry.Id);
            else { count++; bytes += entry.Bytes; }
        }
    }
    public IReadOnlyList<HistoryEntry> List()
    {
        lock (_gate)
        {
            if (!Enabled) return Array.Empty<HistoryEntry>();
            CheckFolder(); PruneCore(DateTime.UtcNow); return Entries();
        }
    }
    public HistoryEntry? Add(string source)
    {
        lock (_gate)
        {
            if (!Enabled) return null;
            CheckFolder();
            string ext = Path.GetExtension(source).ToLowerInvariant();
            if (!Thumbnails.ImageExtensions.Contains(ext)) throw new IOException("Unsupported capture format.");
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length <= 0 || input.Length > Math.Min(MaxCaptureBytes, _maxBytes)) throw new IOException("Capture exceeds history limit.");
            var entry = new HistoryEntry(Guid.NewGuid(), Path.GetFileName(source), ext, DateTime.UtcNow, input.Length);
            Directory.CreateDirectory(_folder);
            PruneCore(DateTime.UtcNow);
            if (_unreadableEntries) throw new IOException("Unreadable history entries retained; pause recording until storage is repaired.");
            string folder = EntryFolder(entry.Id), pending = Path.Combine(_folder, ".pending-" + entry.Id.ToString("N"));
            Directory.CreateDirectory(pending);
            try
            {
                using (var output = new FileStream(Path.Combine(pending, "capture" + ext), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { input.CopyTo(output); output.Flush(true); }
                File.WriteAllText(Path.Combine(pending, "entry.json"), JsonSerializer.Serialize(entry));
                if (!Enabled) return null;
                Directory.Move(pending, folder); // publish the complete copy, never a partially-written file
                PruneCore(DateTime.UtcNow);
                return entry;
            }
            finally
            {
                if (Directory.Exists(pending))
                {
                    CheckEntry(pending);
                    foreach (var file in Directory.EnumerateFiles(pending)) File.Delete(file);
                    Directory.Delete(pending, false);
                }
            }
        }
    }
    public Thumbnail? Thumbnail(Guid id)
    {
        lock (_gate)
        {
            if (!Enabled) return null;
            CheckFolder(); var entry = Read(id);
            return Thumbnails.Load(Path.Combine(EntryFolder(id), "capture" + entry.Extension), 180, 1);
        }
    }
    public void Restore(Guid id, string destination)
    {
        lock (_gate)
        {
            if (!Enabled) throw new InvalidOperationException("History is disabled.");
            CheckFolder(); var entry = Read(id);
            var target = Path.GetFullPath(destination);
            if (target.StartsWith(_folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Restore must be outside history.");
            // A fresh output is required; original images and previous restores are never overwritten.
            string pending = target + "." + Guid.NewGuid().ToString("N") + ".history-restore";
            bool created = false;
            try
            {
                using (var input = File.OpenRead(Path.Combine(EntryFolder(id), "capture" + entry.Extension)))
                using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { created = true; input.CopyTo(output); output.Flush(true); }
                if (!Enabled) throw new InvalidOperationException("History is disabled.");
                File.Move(pending, target, false);
            }
            finally { if (created && File.Exists(pending)) File.Delete(pending); }
        }
    }
    public void Delete(Guid id)
    {
        lock (_gate) { if (!Enabled) return; CheckFolder(); DeleteCore(id); }
    }
    public void Clear()
    {
        lock (_gate)
        {
            if (!Enabled) return; CheckFolder();
            foreach (var entry in Entries()) DeleteCore(entry.Id);
        }
    }
}
