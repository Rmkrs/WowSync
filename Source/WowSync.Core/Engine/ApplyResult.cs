namespace WowSync.Core.Engine;

using WowSync.Plugins.Abstractions.Runs;

public sealed record ApplyResult(
    RunResult RunResult,
    IReadOnlyList<string> LogLines);
