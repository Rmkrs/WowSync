namespace WowSync.Plugins.Abstractions.Operations;

public interface IOperation
{
    string Description { get; }

    IReadOnlyList<string> TouchedPaths { get; }

    Task ExecuteAsync(IOperationContext context, CancellationToken ct);
}
