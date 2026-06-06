namespace WowSync.Plugins.Abstractions.Runs;

public sealed record AccountSnapshot(
    string AccountName,
    string AccountPath,
    IReadOnlyList<RealmSnapshot> Realms);