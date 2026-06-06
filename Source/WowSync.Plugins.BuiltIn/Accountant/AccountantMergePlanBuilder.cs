namespace WowSync.Plugins.BuiltIn.Accountant;

using WowSync.Plugins.Abstractions.Operations;

public static class AccountantMergePlanBuilder
{
    public static IReadOnlyList<IOperation> BuildOperations(
        string mainAccountSavedVariablesPath,
        IReadOnlyList<string> altAccountSavedVariablesPaths)
    {
        return
        [
            new MergeAccountantOperation(
                mainAccountSavedVariablesPath: mainAccountSavedVariablesPath,
                altAccountSavedVariablesPaths: altAccountSavedVariablesPaths),
        ];
    }
}