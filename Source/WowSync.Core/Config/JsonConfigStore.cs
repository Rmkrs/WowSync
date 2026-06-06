namespace WowSync.Core.Config;

using System.IO;
using System.Text.Json;

public sealed class JsonConfigStore(string? configPath = null) : IConfigStore
{
    private static readonly JsonSerializerOptions options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly SemaphoreSlim saveGate = new(1, 1);

    private readonly string configPath = configPath ?? GetDefaultConfigPath();

    public string GetConfigPath()
    {
        return this.configPath;
    }

    public async Task<AppConfig> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(this.configPath))
        {
            return new AppConfig();
        }

        var json = await File.ReadAllTextAsync(this.configPath, ct);
        var cfg = JsonSerializer.Deserialize<AppConfig>(json, options) ?? new AppConfig();
        return cfg;
    }

    public async Task SaveAsync(AppConfig config, CancellationToken ct)
    {
        await saveGate.WaitAsync(ct);
        string? tmp = null;

        try
        {
            var dir = Path.GetDirectoryName(this.configPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // unique temp file per save to avoid collisions
            tmp = this.configPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            await using (var stream = new FileStream(
                             tmp,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, config, options, ct);
                await stream.FlushAsync(ct);
            }

            // Prefer File.Replace when target exists (more atomic on Windows)
            if (File.Exists(this.configPath))
            {
                File.Replace(tmp, this.configPath, destinationBackupFileName: null);
                tmp = null; // File.Replace deletes the source tmp
            }
            else
            {
                File.Move(tmp, this.configPath);
                tmp = null;
            }
        }
        finally
        {
            // Best-effort cleanup if anything failed
            if (tmp is not null && File.Exists(tmp))
            {
                try
                {
                    File.Delete(tmp);
                }
                catch
                {
                    /* ignore */
                }
            }

            saveGate.Release();
        }
    }

    private static string GetDefaultConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "WowSync", "config.json");
    }
}
