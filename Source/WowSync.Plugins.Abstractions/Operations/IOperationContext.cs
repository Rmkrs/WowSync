namespace WowSync.Plugins.Abstractions.Operations;

public interface IOperationContext
{
    IFileSystem FileSystem { get; }

    IRunLogger Logger { get; }

    bool IsDryRun { get; }
}
