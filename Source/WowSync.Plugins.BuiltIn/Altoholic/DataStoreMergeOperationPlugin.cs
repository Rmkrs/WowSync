// ReSharper disable IdentifierTypo
// ReSharper disable StringLiteralTypo
namespace WowSync.Plugins.BuiltIn.Altoholic;

using WowSync.Plugins.Abstractions.Contracts;
using WowSync.Plugins.Abstractions.Operations;
using WowSync.Plugins.Abstractions.Runs;
using WowSync.Plugins.BuiltIn.Altoholic.DataStore;

public sealed class DataStoreMergeOperationPlugin : IOperationPlugin
{
    public string Id => "builtin.altoholic.dataStoreMerge";

    public string DisplayName => "Altoholic (DataStore merge)";

    public string Description => "Merge DataStore character branches from alt accounts into main (strict GUID+Name abort-on-mismatch).";

    public IReadOnlyList<IOperation> BuildOperations(WowContextSnapshot ctx, SyncScope scope)
    {
        var main = ctx.MainAccountName;
        if (string.IsNullOrWhiteSpace(main))
        {
            return [];
        }

        var mainAccount = ctx.Accounts.FirstOrDefault(a =>
                                                          a.AccountName.Equals(main, StringComparison.OrdinalIgnoreCase));

        if (mainAccount is null)
        {
            return [];
        }

        var mainSvPath = Path.Combine(mainAccount.AccountPath, "SavedVariables");

        var altSvPaths = ctx
            .Accounts
            .Where(a => !a.AccountName.Equals(main, StringComparison.OrdinalIgnoreCase) &&
                        ctx.IncludedAccountNames.Contains(a.AccountName, StringComparer.OrdinalIgnoreCase))
            .Select(a => Path.Combine(a.AccountPath, "SavedVariables"))
            .ToList();

        return DataStoreMergePlanBuilder.BuildOperations(
            mainAccountSavedVariablesPath: mainSvPath,
            altAccountSavedVariablesPaths: altSvPaths);
    }
}
