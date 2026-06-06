namespace WowSync.Plugins.Abstractions.Operations;

public sealed record OperationPlan(
    string Title,
    IReadOnlyList<IOperation> Operations);
