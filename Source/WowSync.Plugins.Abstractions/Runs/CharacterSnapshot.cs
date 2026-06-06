// ReSharper disable NotAccessedPositionalProperty.Global
namespace WowSync.Plugins.Abstractions.Runs;

public sealed record CharacterSnapshot(
    string AccountName,
    string RealmName,
    string CharacterName,
    string CharacterPath);