// ReSharper disable NotAccessedPositionalProperty.Global
namespace WowSync.Plugins.Abstractions.Runs;

public sealed record RunResult(
    string RunId,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    bool Succeeded,
    string BackupPath,
    IReadOnlyList<string> TouchedPaths,
    IReadOnlyList<string> AppliedOperationDescriptions,
    WowContextSnapshot Context);
