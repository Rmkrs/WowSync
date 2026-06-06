// ReSharper disable IdentifierTypo
namespace WowSync.Plugins.BuiltIn.Altoholic.DataStore;

using WowSync.Plugins.Abstractions.Operations;

public static class DataStoreMergePlanBuilder
{
    public static IReadOnlyList<IOperation> BuildOperations(
        string mainAccountSavedVariablesPath,
        IReadOnlyList<string> altAccountSavedVariablesPaths)
    {
        return
        [
            new MergeDataStoreAltAccountOperation(
                mainAccountSavedVariablesPath: mainAccountSavedVariablesPath,
                altAccountSavedVariablesPaths: altAccountSavedVariablesPaths),
        ];
    }
}
