namespace WowSync.Core.Profiles;

using System;
using System.Collections.Generic;
using WowSync.Core.Lua;

public static class ProfilePatchEngine
{

    public static IReadOnlyList<PatchOp> Plan(
        LuaDocument sourceDoc,
        LuaDocument targetDoc,
        IReadOnlyList<string> includePaths,
        IPathExpander? expander = null)
    {
        expander ??= NoopPathExpander.Instance;

        var ops = new List<PatchOp>();

        foreach (var includePath in includePaths)
        {
            if (!LuaNavigator.TryGetValueAtPath(sourceDoc, includePath, out var sourceValue) ||
                sourceValue is LuaValue.LuaNil)
            {
                // Source missing -> skip
                continue;
            }

            var targetPaths = expander.Expand(targetDoc, includePath)
                .Select(NormalizeLegacyDotBracketPath)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (var targetPath in targetPaths)
            {
                ops.Add(new PatchOp(includePath, targetPath, sourceValue));
            }
        }

        return ops;
    }

    public static LuaDocument Apply(LuaDocument targetDoc, IReadOnlyList<PatchOp> ops)
    {
        var doc = targetDoc;

        foreach (var op in ops)
        {
            doc = LuaNavigator.SetValueAtPath(doc, op.TargetPath, op.Value);
        }

        return doc;
    }

    private static string NormalizeLegacyDotBracketPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        // Convert legacy: .["x"] -> ["x"]
        // Also: .[123] -> [123]
        // Keep other dots intact.
        return path
            .Replace(".[\"", "[\"", StringComparison.Ordinal)
            .Replace(".[", "[", StringComparison.Ordinal);
    }
}