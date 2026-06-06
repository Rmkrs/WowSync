// ReSharper disable IdentifierTypo
// ReSharper disable StringLiteralTypo
namespace WowSync.Plugins.BuiltIn.Accountant;

using WowSync.Plugins.Abstractions.Contracts;
using WowSync.Plugins.Abstractions.Operations;
using WowSync.Plugins.Abstractions.Runs;

public sealed class AccountantMergeOperationPlugin : IOperationPlugin
{
    public string Id => "builtin.accountant.merge";
    public string DisplayName => "Accountant (merge + distribute)";
    public string Description => "Merge Accountant character branches from included accounts into main, then distribute main back to each included account (filtered to toons present on that target).";

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

        var altSvPaths = ctx.Accounts
            .Where(a => !a.AccountName.Equals(main, StringComparison.OrdinalIgnoreCase) && ctx.IncludedAccountNames.Contains(a.AccountName, StringComparer.OrdinalIgnoreCase))
            .Select(a => Path.Combine(a.AccountPath, "SavedVariables"))
            .Order(StringComparer.Ordinal) // deterministic
            .ToList();

        return AccountantMergePlanBuilder.BuildOperations(
            mainAccountSavedVariablesPath: mainSvPath,
            altAccountSavedVariablesPaths: altSvPaths);
    }
}
