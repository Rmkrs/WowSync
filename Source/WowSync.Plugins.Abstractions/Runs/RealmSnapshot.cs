namespace WowSync.Plugins.Abstractions.Runs;

public sealed record RealmSnapshot(
    string RealmName,
    IReadOnlyList<CharacterSnapshot> Characters);