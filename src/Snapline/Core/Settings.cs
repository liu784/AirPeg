using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snapline.Core;

/// <summary>Everything the app remembers, in one small JSON file.</summary>
public sealed class Settings
{
    /// <summary>A portable preview keeps its data alongside the executable, without migrating another installation.</summary>
    public static bool IsPortable { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag"));
    public static string Folder { get; } = IsPortable
        ? Path.Combine(AppContext.BaseDirectory, "Data")
        : Migrate(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Snapline"));

    /// <summary>The app used to be called Clothesline. Its folder, settings and caught captures come along.</summary>
    private static string Migrate(string folder)
    {
        try
        {
            var old = Path.Combine(Path.GetDirectoryName(folder)!, "Clothesline");
            var marker = Path.Combine(folder, ".migrated");
            var oldJson = Path.Combine(old, "settings.json");
            // Only a folder that is recognisably ours is touched: it must hold our settings file.
            if (!Directory.Exists(old) || File.Exists(marker) || !File.Exists(oldJson)) return folder;
            Directory.CreateDirectory(folder);
            // The old settings win, with their paths rewritten to the new folder.
            File.WriteAllText(Path.Combine(folder, "settings.json"), File.ReadAllText(oldJson).Replace("\\\\Clothesline\\\\", "\\\\Snapline\\\\"));
            File.Delete(oldJson);
            // Caught captures, originals and the rest come along, file by file,
            // subfolders included. Nothing is ever deleted: what cannot be moved stays.
            MoveTree(old, folder);
            File.WriteAllText(marker, DateTime.Now.ToString("o"));
            try { Directory.Delete(old, recursive: false); } catch { }
        }
        catch { }
        return folder;
    }

    /// <summary>Moves every file under one folder to the same place under another, leaving existing files alone, and removes folders that end up empty.</summary>
    private static void MoveTree(string from, string to)
    {
        foreach (var dir in Directory.GetDirectories(from))
        {
            var target = Path.Combine(to, Path.GetFileName(dir));
            Directory.CreateDirectory(target);
            MoveTree(dir, target);
            try { Directory.Delete(dir, recursive: false); } catch { }
        }
        foreach (var file in Directory.GetFiles(from))
        {
            var dest = Path.Combine(to, Path.GetFileName(file));
            if (!File.Exists(dest)) { try { File.Move(file, dest); } catch { } }
        }
    }

    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    // Declared before Current: static fields run in order, and Load needs the options.
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static Settings Current { get; } = Load();

    /// <summary>Files hanging on the line, oldest first.</summary>
    public List<string> Pegged { get; set; } = new();
    public List<string> PinnedPaths { get; set; } = new();
    public bool SoundOn { get; set; } = true;
    public bool CatchClipboard { get; set; } = true;
    /// <summary>Keep the line down while a capture has not been used yet.</summary>
    public bool StayDownWhileUnused { get; set; }
    /// <summary>A photo dragged into an app leaves the line, as if it had been dropped into a folder.</summary>
    public bool TakeDownAfterDrag { get; set; }
    /// <summary>Bring the line down when the pointer rests still at the top edge of the screen.</summary>
    public bool RevealAtTopEdge { get; set; } = true;
    /// <summary>Show the rope-end lock ornament; off also disables its lock behavior.</summary>
    public bool LockHangerEnabled { get; set; } = true;
    /// <summary>Current saved lock state, independent of ornament visibility.</summary>
    public bool LockExpanded { get; set; }
    public bool ShortcutConflictAlerts { get; set; } = true;
    public bool FullScreenHideEnabled { get; set; } = true;
    public bool HistoryEnabled { get; set; }
    public bool QuickPreviewEnabled { get; set; } = true;
    public string PreviewHotKey { get; set; } = "Ctrl+Alt+P";
    private double _revealDelay = 0.5, _hideDelay = 0.5;
    public double RevealDelay { get => _revealDelay; set => _revealDelay = NormalizeDelay(value); }
    public double HideDelay { get => _hideDelay; set => _hideDelay = NormalizeDelay(value); }
    private static double NormalizeDelay(double value) => double.IsFinite(value)
        ? Math.Round(Math.Clamp(value, 0, 3), 1, MidpointRounding.AwayFromZero) : 0.5;
    private double _expandSpeed = 1, _hideSpeed = 1;
    /// <summary>Only the panel reveal clock; 1 preserves the accepted P1 animation.</summary>
    public double ExpandSpeed { get => _expandSpeed; set => _expandSpeed = NormalizeSpeed(value); }
    /// <summary>Only the panel hide clock, independent of cards and capture flights.</summary>
    public double HideSpeed { get => _hideSpeed; set => _hideSpeed = NormalizeSpeed(value); }
    private static double NormalizeSpeed(double value) => double.IsFinite(value)
        ? Math.Round(Math.Clamp(value, 0.5, 2), 1, MidpointRounding.AwayFromZero) : 1;
    /// <summary>How far below the top of the work area the line hangs, in device independent pixels.</summary>
    public double LineOffset { get; set; }
    /// <summary>A preset name like "bronze", or a hex colour like "#8C5A2E".</summary>
    public string RopeColor { get; set; } = "bronze";
    /// <summary>wood, metal, mixed, red, blue, green or yellow.</summary>
    public string PegStyle { get; set; } = "wood";
    public bool Bows { get; set; } = true;
    /// <summary>Legacy setting retained for compatibility; rendering always uses the reference centered layout.</summary>
    public bool AutoArrange { get; set; } = true;
    /// <summary>Where each photo hangs, as a fraction of the width of the screen, by file path.</summary>
    public Dictionary<string, double> Spots { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Sticky notes hanging on the line, by the path of the picture each is drawn into.</summary>
    public Dictionary<string, NoteData> Notes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>auto, light or dark.</summary>
    public string Appearance { get; set; } = "auto";
    public bool LiquidGlassEnabled { get; set; } = true;
    private double _glassIntensity = 50;
    public double GlassIntensity { get => _glassIntensity; set => _glassIntensity = double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 50; }
    /// <summary>zh-CN by default; en is available in Settings.</summary>
    public string Language { get; set; } = "zh-CN";
    public bool Welcomed { get; set; }
    public string HotKey { get; set; } = "Ctrl+Alt+T";
    /// <summary>Extra folders to watch, for ShareX, Greenshot and friends.</summary>
    public List<string> WatchFolders { get; set; } = new();
    /// <summary>Where clipboard captures are written. Defaults to Snapline's own folder.</summary>
    public string? InboxFolder { get; set; }
    /// <summary>Monitor DPI scale aware maximum; null means as many as fit.</summary>
    public int? MaxItems { get; set; }

    private static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Options) ?? new Settings();
                // Paths are matched regardless of case; the deserializer forgets that.
                loaded.Spots = new Dictionary<string, double>(loaded.Spots, StringComparer.OrdinalIgnoreCase);
                loaded.Notes = new Dictionary<string, NoteData>(loaded.Notes, StringComparer.OrdinalIgnoreCase);
                return loaded;
            }
        }
        catch (Exception e)
        {
            Log.Error($"Could not read settings: {e.Message}");
        }
        return new Settings();
    }

    /// <summary>True while rendering art: the real settings file is left alone.</summary>
    public static bool ReadOnly { get; set; }

    public void Save()
    {
        if (ReadOnly) return;
        try
        {
            Directory.CreateDirectory(Folder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception e)
        {
            Log.Error($"Could not save settings: {e.Message}");
        }
    }
}

/// <summary>A tiny log in %LOCALAPPDATA%\Snapline\log.txt, for when something is off.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static readonly string FilePath = Path.Combine(Settings.Folder, "log.txt");
    public static string Location => FilePath;

    public static void Notice(string message) => Write("notice", message);
    public static void Error(string message) => Write("error", message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Settings.Folder);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 512 * 1024) File.Delete(FilePath);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch { }
        System.Diagnostics.Debug.WriteLine($"[{level}] {message}");
    }
}

/// <summary>What a sticky note says and what colour its paper is.</summary>
public sealed class NoteData
{
    public string Text { get; set; } = "";
    public string Color { get; set; } = "yellow";
}
