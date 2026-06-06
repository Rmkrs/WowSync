namespace WowSync.Plugins.BuiltIn.Operations;

using System.Globalization;
using WowSync.Core.Lua;
using WowSync.Core.Profiles;
using WowSync.Plugins.Abstractions.Operations;

public sealed class ApplyLuaProfilePatchOperation(string sourceFile, string targetFile, IReadOnlyList<string> includePaths, PatchMode patchMode) : IOperation
{
    public string Description
        => $"Apply profile patch: {Path.GetFileName(targetFile)}";

    public IReadOnlyList<string> TouchedPaths
        =>
            [targetFile];

    public async Task ExecuteAsync(IOperationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!context.FileSystem.FileExists(sourceFile))
        {
            throw new FileNotFoundException($"Source file not found: {sourceFile}");
        }

        if (!context.FileSystem.FileExists(targetFile))
        {
            context.Logger.Info($"Target file missing, skipping: {targetFile}");
            return;
        }

        var sourceText = await context.FileSystem.ReadAllTextAsync(sourceFile, ct);
        var targetText = await context.FileSystem.ReadAllTextAsync(targetFile, ct);

        ct.ThrowIfCancellationRequested();

        var sourceDoc = new LuaParser(sourceText).ParseDocument();
        var targetDoc = new LuaParser(targetText).ParseDocument();

        var expander = new FirstMatchPathExpander(
            new AceDbProfileExpander(),
            new HeuristicPerCharacterKeyExpander(),
            NoopPathExpander.Instance);

        var planned = ProfilePatchEngine.Plan(
            sourceDoc: sourceDoc,
            targetDoc: targetDoc,
            includePaths: includePaths,
            expander: expander);

        context.Logger.Info($"Patch target: {targetFile}");
        context.Logger.Info($"Patch mode: {patchMode}");
        context.Logger.Info($"Include paths: {includePaths.Count}");
        context.Logger.Info($"Expanded ops: {planned.Count}");

        var eligible = new List<PatchOp>(planned.Count);

        var add = 0;
        var update = 0;
        var unchanged = 0;
        var skippedBranchMissing = 0;
        var skippedUpdateOnly = 0;

        foreach (var op in planned)
        {
            if (!TryGetBranchAndLeafStatus(targetDoc, op.TargetPath, out var leafExists, out var oldValue, out var reason))
            {
                context.Logger.Info($"! {op.TargetPath}");
                context.Logger.Info($"    ! skip: {reason}");
                skippedBranchMissing++;
                continue;
            }

            if (!leafExists)
            {
                if (patchMode == PatchMode.UpdateOnly)
                {
                    context.Logger.Info($"! {op.TargetPath}");
                    context.Logger.Info("    ! skip: update-only (leaf missing)");
                    skippedUpdateOnly++;
                    continue;
                }

                context.Logger.Info($"+ {op.TargetPath}");
                context.Logger.Info($"    + new: {Describe(op.Value)}");
                eligible.Add(op);
                add++;
                continue;
            }

            // Leaf exists
            if (LuaValueEquals(oldValue, op.Value))
            {
                unchanged++;
                continue;
            }

            context.Logger.Info($"~ {op.TargetPath}");
            context.Logger.Info($"    - old: {Describe(oldValue)}");
            context.Logger.Info($"    + new: {Describe(op.Value)}");
            eligible.Add(op);
            update++;
        }

        if (add > 0 || update > 0 || skippedBranchMissing > 0 || skippedUpdateOnly > 0)
        {
            context.Logger.Info(string.Create(CultureInfo.InvariantCulture, $"Summary: {add} add, {update} update, {unchanged} unchanged, {skippedBranchMissing} skipped(branch-missing), {skippedUpdateOnly} skipped(update-only)"));
        }

        if (eligible.Count == 0)
        {
            context.Logger.Info("No changes required (nothing eligible to apply).");
            return;
        }

        var updated = ProfilePatchEngine.Apply(targetDoc, eligible);
        var updatedText = LuaWriter.WriteDocument(updated);

        // Guard rail: never write something we can’t parse
        LuaRoundTripValidator.ValidateOrThrow(targetFile, updatedText);

        await context.FileSystem.WriteAllTextAtomicAsync(targetFile, updatedText, ct);
    }

    private static bool TryGetBranchAndLeafStatus(
        LuaDocument doc,
        string fullPath,
        out bool leafExists,
        out LuaValue oldValue,
        out string reason)
    {
        var p = LuaPath.Parse(fullPath);

        var a = LuaNavigator.FindAssignment(doc, p.GlobalName);
        if (a is null)
        {
            leafExists = false;
            oldValue = new LuaValue.LuaNil();
            reason = "missing global";
            return false;
        }

        // Need parent table of leaf
        var current = a.Value;

        // Walk all segments except last
        for (var idx = 0; idx < p.Segments.Count - 1; idx++)
        {
            if (current is not LuaValue.LuaTable t)
            {
                leafExists = false;
                oldValue = new LuaValue.LuaNil();
                reason = "missing branch (non-table)";
                return false;
            }

            var key = p.Segments[idx].ToLuaKey();

            if (!TryGetEntry(t, key, out var next))
            {
                leafExists = false;
                oldValue = new LuaValue.LuaNil();
                reason = "missing branch (won't create new section)";
                return false;
            }

            current = next;
        }

        // If there are no segments, it's the global itself
        if (p.Segments.Count == 0)
        {
            leafExists = true;
            oldValue = a.Value;
            reason = "";
            return true;
        }

        // Now current must be the parent table
        if (current is not LuaValue.LuaTable parent)
        {
            leafExists = false;
            oldValue = new LuaValue.LuaNil();
            reason = "missing branch (parent non-table)";
            return false;
        }

        var leafKey = p.Segments[^1].ToLuaKey();

        if (TryGetEntry(parent, leafKey, out var existingLeaf))
        {
            leafExists = true;
            oldValue = existingLeaf;
            reason = "";
            return true;
        }

        leafExists = false;
        oldValue = new LuaValue.LuaNil();
        reason = "";
        return true;
    }

    private static bool TryGetEntry(LuaValue.LuaTable table, LuaKey key, out LuaValue value)
    {
        foreach (var e in table.Entries)
        {
            if (LuaKeyEquals(e.Key, key))
            {
                value = e.Value;
                return true;
            }
        }

        value = new LuaValue.LuaNil();
        return false;
    }

    private static bool LuaKeyEquals(LuaKey a, LuaKey b)
    {
        return (a, b) switch
        {
            (LuaKey.IdentifierKey ia, LuaKey.IdentifierKey ib) => string.Equals(ia.Value, ib.Value, StringComparison.Ordinal),
            (LuaKey.StringKey sa, LuaKey.StringKey sb) => string.Equals(sa.Value, sb.Value, StringComparison.Ordinal),
            (LuaKey.NumberKey na, LuaKey.NumberKey nb) => Math.Abs(na.Value - nb.Value) < 0.0000001,
            _ => false,
        };
    }

    private static bool LuaValueEquals(LuaValue a, LuaValue b)
    {
        if (a is LuaValue.LuaTable || b is LuaValue.LuaTable)
        {
            return LuaDeepComparer.AreEquivalent(a, b);
        }

        return (a, b) switch
        {
            (LuaValue.LuaNil, LuaValue.LuaNil) => true,
            (LuaValue.LuaBoolean ba, LuaValue.LuaBoolean bb) => ba.Value == bb.Value,
            (LuaValue.LuaNumber na, LuaValue.LuaNumber nb) => Math.Abs(na.Value - nb.Value) < 0.0000001,
            (LuaValue.LuaString sa, LuaValue.LuaString sb) => string.Equals(sa.Value, sb.Value, StringComparison.Ordinal),
            _ => false,
        };
    }

    private static string Describe(LuaValue v)
    {
        return v switch
        {
            LuaValue.LuaNil => "nil",
            LuaValue.LuaBoolean b => b.Value ? "true" : "false",
            LuaValue.LuaNumber n => n.Value.ToString("G", CultureInfo.InvariantCulture),
            LuaValue.LuaString s => $"\"{s.Value}\"",
            LuaValue.LuaTable t => $"{{...}} ({t.Entries.Count} entries)",
            _ => v.ToString() ?? "(?)",
        };
    }
}
