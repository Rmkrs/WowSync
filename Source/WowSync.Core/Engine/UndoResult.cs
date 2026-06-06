namespace WowSync.Core.Engine;

public sealed record UndoResult(
    string BackupPath,
    int RestoredFiles,
    int DeletedFiles,
    int SkippedFiles,
    IReadOnlyList<string> LogLines);
