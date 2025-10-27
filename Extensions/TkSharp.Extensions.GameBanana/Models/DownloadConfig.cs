using System.Text.Json;

namespace TkSharp.Extensions.GameBanana.Models;

public class DownloadConfig(bool useThreadedDownloads, int timeoutSeconds, int maxRetries)
{
    private static readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "tk-sharp.gb", "Config.json");

    private static readonly DownloadConfig _default = new(
        useThreadedDownloads: false,
        timeoutSeconds: 2 * 60,
        maxRetries: 5
    );

    public bool UseThreadedDownloads { get; set; } = useThreadedDownloads;

    public int TimeoutSeconds { get; set; } = timeoutSeconds;

    public int MaxRetries { get; set; } = maxRetries;

    public static DownloadConfig Load()
    {
        if (!File.Exists(_path)) {
            return _default;
        }

        using var fs = File.OpenRead(_path);
        return JsonSerializer.Deserialize<DownloadConfig>(fs) ?? _default;
    }

    public void Save()
    {
        if (Path.GetDirectoryName(_path) is { } folder) {
            Directory.CreateDirectory(folder);
        }

        using var fs = File.Create(_path);
        JsonSerializer.Serialize(fs, this);
    }
}