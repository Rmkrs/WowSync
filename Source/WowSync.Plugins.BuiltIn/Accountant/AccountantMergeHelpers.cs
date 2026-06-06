// ReSharper disable IdentifierTypo
// ReSharper disable CommentTypo
// ReSharper disable StringLiteralTypo
namespace WowSync.Plugins.BuiltIn.Accountant;

using System.Globalization;
using WowSync.Core.Lua;
using WowSync.Plugins.Abstractions.Operations;

public static class AccountantMergeHelpers
{
    private const string SaveDataVar = "Accountant_SaveData";

    public static bool TryGetSaveDataTable(LuaDocument doc, out LuaValue.LuaTable table)
    {
        table = null!;

        if (!LuaNavigator.TryGetValueAtPath(doc, SaveDataVar, out var v) || v is not LuaValue.LuaTable t)
        {
            return false;
        }

        table = t;
        return true;
    }

    /// <summary>
    /// Merge all character branches from alt into main's Accountant_SaveData.
    /// Treat each character branch as atomic value (deep compare).
    /// Deterministic: process keys sorted Ordinal.
    /// </summary>
    public static (LuaDocument UpdatedMainDoc, int Adds, int Replaces) MergeAltIntoMain(
    LuaDocument mainDoc,
    LuaValue.LuaTable altSaveData,
    IRunLogger log,
    bool isDryRun)
    {
        var adds = 0;
        var replaces = 0;

        if (!TryGetSaveDataTable(mainDoc, out var mainSaveData))
        {
            throw new InvalidOperationException($"Main '{SaveDataVar}' missing during merge.");
        }

        // Determine "exists in main" using actual keys (no path lookup ambiguity).
        var mainKeys = new HashSet<string>(
            ReadStringKeys(mainSaveData),
            StringComparer.Ordinal);

        var altKeys = ReadStringKeys(altSaveData);

        foreach (var charKey in altKeys)
        {
            if (!TryGetStringKeyValue(altSaveData, charKey, out var altBranch))
            {
                continue;
            }

            var escaped = EscapeLuaStringKey(charKey);
            var path = $"{SaveDataVar}[\"{escaped}\"]";

            var existsInMain = mainKeys.Contains(charKey);

            // If it exists, fetch existing branch for equivalence check
            LuaValue? existingMainBranch = null;
            if (existsInMain && TryGetStringKeyValue(mainSaveData, charKey, out var existing))
            {
                existingMainBranch = existing;
            }

            if (existsInMain && existingMainBranch is not null && LuaDeepComparer.AreEquivalent(existingMainBranch, altBranch))
            {
                // identical, keep quiet
                continue;
            }

            if (existsInMain)
            {
                replaces++;
            }
            else
            {
                adds++;
            }

            log.Info(
                isDryRun
                    ? $"Accountant: Would {(existsInMain ? "REPLACE" : "ADD")} '{charKey}' into main ({(existsInMain ? "key exists" : "key missing")})."
                    : $"Accountant: {(existsInMain ? "REPLACE" : "ADD")} '{charKey}' into main ({(existsInMain ? "key exists" : "key missing")}).");

            mainDoc = LuaNavigator.SetValueAtPath(mainDoc, path, altBranch);

            // If we just added a new key, update keyset so subsequent alts behave correctly
            if (!existsInMain)
            {
                mainKeys.Add(charKey);
            }
        }

        return (mainDoc, adds, replaces);
    }


    /// <summary>
    /// Build a version of target doc where Accountant_SaveData contains only the toon keys that already exist on that target,
    /// but values are pulled from the merged main doc. If a target toon key doesn't exist in main, skip with a log line.
    /// Deterministic ordering by key Ordinal.
    /// </summary>
    public static (LuaDocument UpdatedTargetDoc, int SkippedMissingInMain, int ToonCount) BuildFilteredTargetDocument(
        LuaDocument mergedMainDoc,
        LuaDocument targetDoc,
        LuaValue.LuaTable targetExistingSaveData,
        IRunLogger log,
        bool isDryRun,
        string targetTag)
    {
        if (!TryGetSaveDataTable(mergedMainDoc, out var mergedMainSaveData))
        {
            throw new InvalidOperationException($"Merged main '{SaveDataVar}' missing during distribution.");
        }

        var mainMap = mergedMainSaveData.Entries
            .Where(e => e.Key is LuaKey.StringKey)
            .ToDictionary(
                e => ((LuaKey.StringKey)e.Key).Value,
                e => e.Value,
                StringComparer.Ordinal);

        var targetKeys = ReadStringKeys(targetExistingSaveData);

        var skipped = 0;

        var newEntries = new List<(LuaKey Key, LuaValue Value)>(targetKeys.Count);

        foreach (var charKey in targetKeys)
        {
            if (!mainMap.TryGetValue(charKey, out var mainBranch))
            {
                skipped++;
                log.Info($"Accountant: Target '{targetTag}': Toon '{charKey}' exists on target but not in main. Skipping.");
                continue;
            }

            newEntries.Add((new LuaKey.StringKey(charKey), mainBranch));
        }

        // Create a new SaveData table with entries sorted by charKey Ordinal already
        var newSaveData = new LuaValue.LuaTable(
            [.. newEntries.Select(e => new LuaTableEntry(e.Key, e.Value))]);

        // Avoid touching the doc if identical: compare old vs new table
        // (still safe to set, but this reduces churn)
        if (LuaDeepComparer.AreEquivalent(targetExistingSaveData, newSaveData))
        {
            return (targetDoc, skipped, targetKeys.Count);
        }

        log.Info(
            isDryRun
                ? string.Create(CultureInfo.InvariantCulture, $"Accountant: Would rebuild target '{targetTag}' SaveData table (toons={targetKeys.Count}, skippedMissingInMain={skipped}).")
                : string.Create(CultureInfo.InvariantCulture, $"Accountant: Rebuilding target '{targetTag}' SaveData table (toons={targetKeys.Count}, skippedMissingInMain={skipped})."));

        targetDoc = LuaNavigator.SetValueAtPath(targetDoc, SaveDataVar, newSaveData);

        return (targetDoc, skipped, targetKeys.Count);
    }

    private static List<string> ReadStringKeys(LuaValue.LuaTable table)
    {
        var keys = new List<string>();

        foreach (var e in table.Entries)
        {
            if (e.Key is LuaKey.StringKey sk)
            {
                keys.Add(sk.Value);
            }
        }

        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    private static bool TryGetStringKeyValue(LuaValue.LuaTable table, string key, out LuaValue value)
    {
        foreach (var e in table.Entries)
        {
            if (e.Key is LuaKey.StringKey sk && string.Equals(sk.Value, key, StringComparison.Ordinal))
            {
                value = e.Value;
                return true;
            }
        }

        value = null!;
        return false;
    }

    private static string EscapeLuaStringKey(string s)
    {
        return s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
