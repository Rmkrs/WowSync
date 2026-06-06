namespace WowSync.Plugins.BuiltIn.AccountWide;

using WowSync.Core.Operations;
using WowSync.Plugins.Abstractions.Operations;
using WowSync.Plugins.Abstractions.Runs;

public static class AccountSavedVariablesSyncPlugin
{
    public static OperationPlan BuildPlan(WowContextSnapshot context, IReadOnlyList<string> files)
    {
        var main = context.Accounts.First(a => a.AccountName.Equals(context.MainAccountName, StringComparison.OrdinalIgnoreCase));

        var included = new HashSet<string>(context.IncludedAccountNames, StringComparer.OrdinalIgnoreCase);

        var subAccounts = context
            .Accounts
            .Where(a => included.Contains(a.AccountName) && !a.AccountName.Equals(context.MainAccountName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var ops = new List<IOperation>();

        foreach (var file in files.Where(f => !string.IsNullOrWhiteSpace(f)))
        {
            var trimmed = file.Trim();

            // normalize extension: allow "Addon" or "Addon.lua"
            var lua = trimmed.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + ".lua";

            var srcLua = Path.Combine(main.AccountPath, "SavedVariables", lua);

            foreach (var sub in subAccounts)
            {
                var dstLua = Path.Combine(sub.AccountPath, "SavedVariables", lua);
                ops.Add(new CopyFileOperation(srcLua, dstLua, overwrite: true));
            }
        }

        return new OperationPlan("Account SavedVariables Sync (Main -> Subs)", ops);
    }
}
