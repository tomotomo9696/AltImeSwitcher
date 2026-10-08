using System.Text.Json;

namespace AltImeSwitcher;

internal sealed record AppSettings
{
    public const int DefaultHoldMilliseconds = 220;
    public static IReadOnlyList<int> HoldOptions { get; } = Array.AsReadOnly(new[] { 150, 180, 220, 300, 400, 500 });
    public static string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AltImeSwitcher", "settings.json");
    public int HoldMilliseconds { get; init; } = DefaultHoldMilliseconds;

    public static int ValidateHoldMilliseconds(int value) => HoldOptions.Contains(value)
        ? value : throw new ArgumentOutOfRangeException(nameof(value), "Unsupported Alt hold threshold.");

    public static AppSettings Load(string? path = null)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path ?? SettingsPath));
            if (settings is not null && HoldOptions.Contains(settings.HoldMilliseconds)) return settings;
            AppLog.Write("Invalid hold threshold in settings; using default.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or System.Security.SecurityException)
        {
            AppLog.Write($"Settings load failed; using default. {ex.Message}");
        }
        return new();
    }

    public bool TrySave(string? path = null)
    {
        ValidateHoldMilliseconds(HoldMilliseconds);
        try
        {
            var destination = Path.GetFullPath(path ?? SettingsPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, destination, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            AppLog.Write($"Settings save failed. {ex.Message}");
            return false;
        }
    }
}
