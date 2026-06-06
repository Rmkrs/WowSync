// ReSharper disable CommentTypo
namespace WowSync.Plugins.BuiltIn.Plans;

using WowSync.Core.Profiles;
using WowSync.Plugins.Abstractions.Contracts;
using WowSync.Plugins.Abstractions.Operations;
using WowSync.Plugins.Abstractions.Runs;
using WowSync.Plugins.BuiltIn.Operations;

public static class ProfilePatchPlanFactory
{
    public static OperationPlan CreateApplyLuaProfilePatchPlan(
        WowContextSnapshot ctx,
        string savedVariablesFileName,
        IReadOnlyList<string> includePaths,
        SyncScope scope,
        PatchMode patchMode)
    {
        if (string.IsNullOrWhiteSpace(ctx.MainAccountName))
        {
            throw new InvalidOperationException("Main account is not set in context.");
        }

        var mainAccountName = ctx.MainAccountName;
        var mainAccount = ctx.Accounts.FirstOrDefault(a => a.AccountName.Equals(mainAccountName, StringComparison.OrdinalIgnoreCase));
        if (mainAccount is null)
        {
            throw new InvalidOperationException($"Main account '{mainAccountName}' not found in discovered accounts.");
        }

        var sourceFile = GetSavedVariablesFile(mainAccount, savedVariablesFileName);

        var ops = new List<IOperation>();

        // 1) Apply to main account file (covers 'main account other toons' via profile expansion)
        if (scope is SyncScope.MainToMainAccountOtherToons or SyncScope.MainToAllAccountsAndToons)
        {
            ops.Add(new ApplyLuaProfilePatchOperation(
                sourceFile: sourceFile,
                targetFile: sourceFile,
                includePaths: includePaths,
                patchMode: patchMode));
        }

        // 2) Apply to included sub accounts
        if (scope is SyncScope.MainToSubAccounts or SyncScope.MainToAllAccountsAndToons)
        {
            foreach (var account in ctx.Accounts)
            {
                if (account.AccountName.Equals(mainAccountName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Respect include list from config (or ctx.IncludedAccountNames, whichever is the source of truth)
                if (!ctx.IncludedAccountNames.Contains(account.AccountName, StringComparer.Ordinal))
                {
                    continue;
                }

                var targetFile = GetSavedVariablesFile(account, savedVariablesFileName);

                ops.Add(new ApplyLuaProfilePatchOperation(
                    sourceFile: sourceFile,
                    targetFile: targetFile,
                    includePaths: includePaths,
                    patchMode: patchMode));
            }
        }

        var title = $"Apply Lua Profile Patch ({savedVariablesFileName}) [{scope}]";

        return new OperationPlan(title, ops);
    }

    private static string GetSavedVariablesFile(AccountSnapshot account, string fileName)
    {
        // The discovery’s AccountPath is the account folder: ...\WTF\Account\<AccountName>
        // SavedVariables are directly under it.
        var dir = Path.Combine(account.AccountPath, "SavedVariables");
        return Path.Combine(dir, fileName);
    }
}
