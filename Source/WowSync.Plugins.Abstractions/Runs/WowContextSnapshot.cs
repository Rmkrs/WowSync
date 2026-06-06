// ReSharper disable NotAccessedPositionalProperty.Global
namespace WowSync.Plugins.Abstractions.Runs;

public sealed record WowContextSnapshot(
    string WowRoot,
    IReadOnlyList<AccountSnapshot> Accounts,
    string? MainAccountName,
    IReadOnlyList<string> IncludedAccountNames,
    CharacterSnapshot? MainCharacter);