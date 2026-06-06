namespace WowSync.Core.Engine;

using WowSync.Plugins.Abstractions.Operations;

public sealed class OperationContext(IFileSystem fileSystem, IRunLogger logger, bool isDryRun) : IOperationContext
{
    public IFileSystem FileSystem { get; } = fileSystem;

    public IRunLogger Logger { get; } = logger;

    public bool IsDryRun { get; } = isDryRun;
}