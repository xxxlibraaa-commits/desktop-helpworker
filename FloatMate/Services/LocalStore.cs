using System.Text.Json;
using System.IO;

namespace FloatMate.Services;

public sealed class LocalStore
{
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatMate");
    private string FilePath => Path.Combine(_directory, "data.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string DataLocation => FilePath;

    public AppData Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return NewDay();
            var json = File.ReadAllText(FilePath);
            var isLegacy = !json.Contains("\"SchemaVersion\"", StringComparison.Ordinal);
            var data = JsonSerializer.Deserialize<AppData>(json, JsonOptions) ?? NewDay();
            var previousActiveDate = data.ActiveDate;
            foreach (var goal in data.Goals)
            {
                if (goal.Date == default) goal.Date = previousActiveDate;
            }
            if (isLegacy)
            {
                data.SchemaVersion = 4;
                data.IsExpanded = false;
                data.Opacity = 0.78;
                data.AutoCollapse = true;
                data.DesktopWidgetMode = true;
            }
            else if (data.SchemaVersion < 4)
            {
                data.SchemaVersion = 4;
                if (data.Opacity >= 0.85) data.Opacity = 0.78;
                data.DesktopWidgetMode = true;
            }
            if (data.SchemaVersion < 5)
            {
                data.SchemaVersion = 5;
                if (data.Opacity <= 0.551) data.Opacity = 0.78;
            }
            if (data.SchemaVersion < 6)
            {
                data.SchemaVersion = 6;
                // The light surface needs more opacity than the former dark glass to preserve text contrast.
                data.Opacity = Math.Max(data.Opacity, 0.94);
            }
            if (data.SchemaVersion < 7)
            {
                data.SchemaVersion = 7;
                data.DockedRightMode = true;
                data.DesktopWidgetMode = false;
            }
            if (data.SchemaVersion < 8)
            {
                data.SchemaVersion = 8;
                foreach (var goal in data.Goals) goal.Details ??= string.Empty;
            }
            if (data.SchemaVersion < 9)
            {
                data.SchemaVersion = 9;
                data.AppUsage ??= [];
            }
            if (data.SchemaVersion < 10)
            {
                BackupBeforeMigration(FilePath, data.SchemaVersion);
                data.SchemaVersion = 10;
                data.Plans ??= [];
            }
            foreach (var plan in data.Plans ?? []) plan.Tasks ??= [];
            data.ActiveDate = DateOnly.FromDateTime(DateTime.Now);
            return data;
        }
        catch
        {
            return NewDay();
        }
    }

    private static void BackupBeforeMigration(string filePath, int schemaVersion)
    {
        try
        {
            var backupDirectory = Path.Combine(Path.GetDirectoryName(filePath)!, "backups");
            Directory.CreateDirectory(backupDirectory);
            var backupPath = Path.Combine(backupDirectory, $"data-schema{schemaVersion}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(filePath, backupPath, false);
        }
        catch
        {
            // A backup failure must not prevent loading the existing local data.
        }
    }

    public void Save(AppData data)
    {
        Directory.CreateDirectory(_directory);
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(data, JsonOptions));
        File.Move(tempPath, FilePath, true);
    }

    private static AppData NewDay() => new();
}
