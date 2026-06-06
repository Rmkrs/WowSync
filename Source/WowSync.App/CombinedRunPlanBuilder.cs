namespace WowSync.App;

using Core.Profiles;
using Plugins.Abstractions.Contracts;
using Plugins.Abstractions.Operations;
using Plugins.Abstractions.Runs;

public sealed class CombinedRunPlanBuilder(IEnumerable<IOperationPlugin> plugins)
{
    private readonly IReadOnlyList<IOperationPlugin> plugins = [.. plugins];

    public OperationPlan Build(
        WowContextSnapshot ctx,
        IReadOnlyList<string> accountSvFiles,
        IReadOnlyList<ProfileRow> selectedProfiles,
        IReadOnlySet<string> selectedPluginIds,
        SyncScope scope,
        ProfileStore profileStore)
    {
        var ops = new List<IOperation>();

        // 1) account SV files: NOT a plugin
        if (accountSvFiles.Count > 0)
        {
            var svPlan = Plugins.BuiltIn.AccountWide.AccountSavedVariablesSyncPlugin.BuildPlan(
                ctx, accountSvFiles);

            ops.AddRange(svPlan.Operations);
        }

        // 2) profiles: NOT a plugin
        foreach (var row in selectedProfiles)
        {
            LuaSyncProfile profile;
            try
            {
                profile = profileStore.Load(row.FilePath);
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(profile.FileName))
            {
                continue;
            }

            var plan = Plugins.BuiltIn.Plans.ProfilePatchPlanFactory.CreateApplyLuaProfilePatchPlan(
                ctx: ctx,
                savedVariablesFileName: profile.FileName,
                includePaths: profile.IncludePaths,
                scope: scope,
                patchMode: profile.PatchMode);

            ops.AddRange(plan.Operations);
        }

        // 3) plugins: REAL plugins
        foreach (var p in this.plugins.Where(p => selectedPluginIds.Contains(p.Id)))
        {
            ops.AddRange(p.BuildOperations(ctx, scope));
        }

        return new OperationPlan("WowSync: Combined run (files + profiles + plugins)", ops);
    }
}
