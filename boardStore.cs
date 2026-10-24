using System.IO;
using System.Text.Json;

namespace boardzz;

internal sealed record padRecord(int index, string file, string name, string? emoji, double start, double end, float volume);

internal sealed record tabRecord(string name, string? emoji, List<padRecord> pads);

internal sealed record boardRecord(int selected, List<tabRecord> tabs)
{
    public static readonly boardRecord empty = new(0, []);
}

internal sealed record appSettings(string? targetId, int recordSeconds, List<string> micIds, bool desktop)
{
    public static readonly appSettings defaults = new(null, 60, [], false);
}

// Saves the board to %AppData%\Boardzz\board.json and keeps a private copy of every sound
// in %AppData%\Boardzz\sounds, so pads survive restarts even if the original file moves.
internal static class boardStore
{
    public static readonly string rootFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Boardzz");
    public static readonly string soundsFolder = Path.Combine(rootFolder, "sounds");

    private static readonly string boardFile = Path.Combine(rootFolder, "board.json");
    private static readonly string settingsFile = Path.Combine(rootFolder, "settings.json");
    private static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };

    public static boardRecord load()
    {
        try
        {
            if (!File.Exists(boardFile)) return boardRecord.empty;
            string json = File.ReadAllText(boardFile);

            // Older versions saved one board as a plain list of pads: move it into a first tab.
            using (var document = JsonDocument.Parse(json))
            {
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                    return new boardRecord(0, [new tabRecord("Main", null, JsonSerializer.Deserialize<List<padRecord>>(json) ?? [])]);
            }
            return JsonSerializer.Deserialize<boardRecord>(json) ?? boardRecord.empty;
        }
        catch (JsonException)
        {
            File.Copy(boardFile, boardFile + ".bad", true);   // keep the broken file for inspection, start fresh
            return boardRecord.empty;
        }
    }

    public static void save(boardRecord board) => writeJson(boardFile, board);

    public static appSettings loadSettings()
    {
        try
        {
            if (!File.Exists(settingsFile)) return appSettings.defaults;
            return JsonSerializer.Deserialize<appSettings>(File.ReadAllText(settingsFile)) ?? appSettings.defaults;
        }
        catch (JsonException)
        {
            return appSettings.defaults;
        }
    }

    public static void saveSettings(appSettings settings) => writeJson(settingsFile, settings);

    private static void writeJson<T>(string path, T value)
    {
        Directory.CreateDirectory(rootFolder);
        var tempFile = path + ".tmp";
        File.WriteAllText(tempFile, JsonSerializer.Serialize(value, jsonOptions));
        File.Move(tempFile, path, true);   // atomic swap: a crash mid-save never corrupts the file
    }

    // Copies a sound into the app's own folder (unless it is already there) and returns the stored path.
    public static string import(string sourcePath)
    {
        if (isStored(sourcePath)) return sourcePath;

        Directory.CreateDirectory(soundsFolder);
        string baseName = Path.GetFileNameWithoutExtension(sourcePath);
        string target = Path.Combine(soundsFolder, $"{baseName}_{Guid.NewGuid().ToString("N")[..8]}{Path.GetExtension(sourcePath)}");
        File.Copy(sourcePath, target);
        return target;
    }

    public static bool isStored(string path) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(soundsFolder) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    public static void deleteIfUnused(string? path, IEnumerable<string?> pathsInUse)
    {
        if (path == null || !isStored(path)) return;
        if (pathsInUse.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase))) return;

        try { File.Delete(path); }
        catch (IOException) { }                  // still open somewhere: leave it, harmless
        catch (UnauthorizedAccessException) { }
    }
}
