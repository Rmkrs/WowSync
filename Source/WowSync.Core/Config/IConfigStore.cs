namespace WowSync.Core.Config;

public interface IConfigStore
{
    Task<AppConfig> LoadAsync(CancellationToken ct);

    Task SaveAsync(AppConfig config, CancellationToken ct);

    string GetConfigPath();
}
