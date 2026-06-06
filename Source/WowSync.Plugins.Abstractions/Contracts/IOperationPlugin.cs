namespace WowSync.Plugins.Abstractions.Contracts;

using WowSync.Plugins.Abstractions.Operations;
using WowSync.Plugins.Abstractions.Runs;

public interface IOperationPlugin
{
    string Id { get; }

    string DisplayName { get; }

    string Description { get; }

    IReadOnlyList<IOperation> BuildOperations(
        WowContextSnapshot ctx,
        SyncScope scope);
}