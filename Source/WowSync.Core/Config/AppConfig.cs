namespace WowSync.Core.Config;

using System.Text.Json.Serialization;
using Plugins.Abstractions.Contracts;

public sealed record AppConfig
{
    public string? WowRoot { get; init; }

    public string? BackupRoot { get; init; }

    public string? MainAccountName { get; init; }

    public HashSet<string> IncludedAccountNames { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string? MainRealmName { get; init; }

    public string? MainCharacterName { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SyncScope SelectedSyncScope { get; init; } = SyncScope.MainToAllAccountsAndToons;

    public List<string> AccountSavedVariablesFiles { get; init; } = [];

    public HashSet<string> IncludedAccountSavedVariablesFiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> IncludedProfileFiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> IncludedPluginIds { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public int LastSelectedTabIndex { get; init; }

    public WindowPlacement? MainWindowPlacement { get; init; }

    public WindowPlacement? InspectorWindowPlacement { get; init; }
}
