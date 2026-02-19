using System.Text.Json;

namespace FlowForge.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON in a platform-appropriate location.
/// Uses atomic writes (write to .tmp, then rename) to avoid corrupt files on crash.
/// </summary>
public sealed class AppSettingsManager
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _settingsFilePath;

    /// <summary>
    /// Creates a manager that reads/writes settings at the default platform path:
    /// <c>{ApplicationData}/FlowForge/settings.json</c>.
    /// </summary>
    public AppSettingsManager()
        : this(BuildDefaultPath())
    {
    }

    /// <summary>
    /// Creates a manager that reads/writes settings at the specified path.
    /// Useful for testing.
    /// </summary>
    public AppSettingsManager(string settingsFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        _settingsFilePath = settingsFilePath;
    }

    /// <summary>Full path where settings are persisted.</summary>
    public string SettingsFilePath => _settingsFilePath;

    /// <summary>
    /// Loads settings from disk. Returns a fresh <see cref="AppSettings"/> with defaults
    /// when the file does not exist or contains invalid JSON.
    /// </summary>
    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_settingsFilePath))
        {
            return new AppSettings();
        }

        string json = await File.ReadAllTextAsync(_settingsFilePath, ct);

        try
        {
            AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
            return settings ?? new AppSettings();
        }
        catch (JsonException)
        {
            // Corrupt file — return defaults so the app can still start.
            return new AppSettings();
        }
    }

    /// <summary>
    /// Persists settings to disk using an atomic write (write to .tmp, then rename).
    /// Creates the parent directory if it does not exist.
    /// </summary>
    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string? directory = Path.GetDirectoryName(_settingsFilePath);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(settings, SerializerOptions);

        // Atomic write: write to temp file, then rename.
        string tmpPath = _settingsFilePath + ".tmp";
        await File.WriteAllTextAsync(tmpPath, json, ct);
        File.Move(tmpPath, _settingsFilePath, overwrite: true);
    }

    private static string BuildDefaultPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "FlowForge", "settings.json");
    }
}
