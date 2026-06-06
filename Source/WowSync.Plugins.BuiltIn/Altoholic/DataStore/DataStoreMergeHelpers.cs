// ReSharper disable IdentifierTypo
// ReSharper disable GrammarMistakeInComment
namespace WowSync.Plugins.BuiltIn.Altoholic.DataStore;

using System.Globalization;
using WowSync.Core.Lua;
using WowSync.Plugins.Abstractions.Operations;

public static class DataStoreMergeHelpers
{
    public sealed record CharacterRef(string CharacterGuid, string Name, int ZeroBasedIndex, string FullId);

    public static IReadOnlyList<CharacterRef> BuildCharacterIndex(LuaDocument dataStoreLua)
    {
        var guids = ReadStringArrayFromGlobal(dataStoreLua, "DataStore_CharacterGUIDs");
        var ids = ReadStringArrayFromField(dataStoreLua, "DataStore_CharacterIDs", "List");

        if (guids.Count != ids.Count)
        {
            throw new InvalidOperationException(
                $"DataStore.lua mismatch: DataStore_CharacterGUIDs={guids.Count} vs DataStore_CharacterIDs['List']={ids.Count}");
        }

        var list = new List<CharacterRef>(guids.Count);
        for (var i = 0; i < guids.Count; i++)
        {
            var guid = guids[i];
            var fullId = ids[i];
            var name = LastTokenAfterDot(fullId);
            list.Add(new CharacterRef(guid, name, i, fullId));
        }

        return list;
    }

    public static bool ValidateNoMismatchesOrAbort(
        IReadOnlyList<CharacterRef> alt,
        IReadOnlyList<CharacterRef> main,
        IRunLogger log,
        string fileTag)
    {
        var mainByGuid = main.ToDictionary(x => x.CharacterGuid, StringComparer.Ordinal);
        var mainByName = main.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var a in alt)
        {
            if (mainByGuid.TryGetValue(a.CharacterGuid, out var mByGuid) &&
                !string.Equals(mByGuid.Name, a.Name, StringComparison.Ordinal))
            {
                log.Err($"{fileTag}: MISMATCH: GUID '{a.CharacterGuid}' exists in main as '{mByGuid.Name}' but alt says '{a.Name}'. Aborting merge for this file.");
                return false;
            }

            if (mainByName.TryGetValue(a.Name, out var mByName) &&
                !string.Equals(mByName.CharacterGuid, a.CharacterGuid, StringComparison.Ordinal))
            {
                log.Err($"{fileTag}: MISMATCH: Name '{a.Name}' exists in main with GUID '{mByName.CharacterGuid}' but alt says '{a.CharacterGuid}'. Aborting merge for this file.");
                return false;
            }
        }

        return true;
    }

    public static int GetArrayLength(LuaValue.LuaTable table)
    {
        // DataStore arrays are numeric keys (1..N). We treat "max numeric key" as length.
        var max = 0;
        foreach (var e in table.Entries)
        {
            if (e.Key is LuaKey.NumberKey nk)
            {
                var v = (int)nk.Value;
                if (v > max)
                {
                    max = v;
                }
            }
        }

        return max;
    }

    public static LuaDocument AddCharacterToMainIdentity(
        LuaDocument mainDsDoc,
        LuaDocument altDsDoc,
        CharacterRef altChar,
        IRunLogger log,
        string fileTag)
    {
        // We only ever touch DataStore_CharacterIDs using ["..."] to avoid creating a second "List" field.
        const string IdsRoot = "DataStore_CharacterIDs";
        const string IdsList = "DataStore_CharacterIDs[\"List\"]";
        const string IdsSet = "DataStore_CharacterIDs[\"Set\"]";
        const string IdsCount = "DataStore_CharacterIDs[\"Count\"]";

        // 1) Determine the new index based on GUID array length.
        var mainGuidCount = GetGlobalArrayLength(mainDsDoc, "DataStore_CharacterGUIDs");
        var newIndex = mainGuidCount + 1;

        // 2) Sanity: main IDs["List"] must exist and match GUID count.
        var mainIdCount = GetFieldArrayLength(mainDsDoc, IdsRoot, "List");
        if (mainIdCount != mainGuidCount)
        {
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"{fileTag}: Main identity mismatch before add. GUIDs={mainGuidCount} vs IDs['List']={mainIdCount}"));
        }

        // 3) Ensure required tables exist (List + Set) in main.
        mainDsDoc = EnsureFieldTableExists(mainDsDoc, IdsRoot, "List");
        mainDsDoc = EnsureFieldTableExists(mainDsDoc, IdsRoot, "Set");

        // 4) Append GUID + FullId + Count + Set
        mainDsDoc = LuaNavigator.SetValueAtPath(mainDsDoc, string.Create(CultureInfo.InvariantCulture, $"DataStore_CharacterGUIDs[{newIndex}]"), new LuaValue.LuaString(altChar.CharacterGuid));
        mainDsDoc = LuaNavigator.SetValueAtPath(mainDsDoc, string.Create(CultureInfo.InvariantCulture, $"{IdsList}[{newIndex}]"), new LuaValue.LuaString(altChar.FullId));
        mainDsDoc = LuaNavigator.SetValueAtPath(mainDsDoc, IdsCount, new LuaValue.LuaNumber(newIndex));
        mainDsDoc = LuaNavigator.SetValueAtPath(mainDsDoc, $"{IdsSet}[\"{EscapeLuaStringKey(altChar.FullId)}\"]", new LuaValue.LuaNumber(newIndex));

        // 5) Append CharacterGuilds[ newIndex ] from alt's corresponding index
        // If main doesn't have guilds table, that's an error
        var altLuaIndex = altChar.ZeroBasedIndex + 1;
        var guildVal = GetAltGuildValueOrDefault(altDsDoc, altLuaIndex);

        // Ensure main guilds table exists too (should, but safe)
        mainDsDoc = EnsureGlobalTableExists(mainDsDoc, "DataStore_CharacterGuilds");
        mainDsDoc = LuaNavigator.SetValueAtPath(mainDsDoc, string.Create(CultureInfo.InvariantCulture, $"DataStore_CharacterGuilds[{newIndex}]"), guildVal);

        log.Info(string.Create(CultureInfo.InvariantCulture, $"{fileTag}: Planned identity index {newIndex}: guid='{altChar.CharacterGuid}', id='{altChar.FullId}', guild={LuaValueToDebug(guildVal)}"));

        // 6) Final sanity: GUIDs and IDs["List"] must match after add.
        var newGuidCount = GetGlobalArrayLength(mainDsDoc, "DataStore_CharacterGUIDs");
        var newIdCount = GetFieldArrayLength(mainDsDoc, IdsRoot, "List");
        if (newGuidCount != newIdCount)
        {
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"{fileTag}: Post-add mismatch: DataStore_CharacterGUIDs={newGuidCount} vs DataStore_CharacterIDs['List']={newIdCount}"));
        }

        return mainDsDoc;
    }

    private static IReadOnlyList<string> ReadStringArrayFromGlobal(LuaDocument doc, string globalName)
    {
        var a = LuaNavigator.FindAssignment(doc, globalName)
            ?? throw new InvalidOperationException($"Missing global assignment: {globalName}");

        if (a.Value is not LuaValue.LuaTable t)
        {
            throw new InvalidOperationException($"{globalName} is not a table");
        }

        var ordered = t.Entries
            .Where(e => e.Key is LuaKey.NumberKey)
            .OrderBy(e => ((LuaKey.NumberKey)e.Key).Value)
            .Select(e => e.Value)
            .ToList();

        var result = new List<string>(ordered.Count);
        foreach (var v in ordered)
        {
            if (v is not LuaValue.LuaString s)
            {
                throw new InvalidOperationException($"{globalName} contains non-string array item");
            }

            result.Add(s.Value);
        }

        return result;
    }

    private static IReadOnlyList<string> ReadStringArrayFromField(LuaDocument doc, string globalName, string fieldName)
    {
        var a = LuaNavigator.FindAssignment(doc, globalName)
            ?? throw new InvalidOperationException($"Missing global assignment: {globalName}");

        if (a.Value is not LuaValue.LuaTable root)
        {
            throw new InvalidOperationException($"{globalName} is not a table");
        }

        LuaValue? field = null;
        foreach (var e in root.Entries)
        {
            if (e.Key is LuaKey.StringKey sk && string.Equals(sk.Value, fieldName, StringComparison.Ordinal))
            {
                field = e.Value;
                break;
            }

            if (e.Key is LuaKey.IdentifierKey ik && string.Equals(ik.Value, fieldName, StringComparison.Ordinal))
            {
                field = e.Value;
                break;
            }
        }

        if (field is not LuaValue.LuaTable fieldTable)
        {
            throw new InvalidOperationException($"{globalName}.{fieldName} is not a table");
        }

        var ordered = fieldTable.Entries
            .Where(e => e.Key is LuaKey.NumberKey)
            .OrderBy(e => ((LuaKey.NumberKey)e.Key).Value)
            .Select(e => e.Value)
            .ToList();

        var result = new List<string>(ordered.Count);
        foreach (var v in ordered)
        {
            if (v is not LuaValue.LuaString s)
            {
                throw new InvalidOperationException($"{globalName}.{fieldName} contains non-string array item");
            }

            result.Add(s.Value);
        }

        return result;
    }

    private static string LastTokenAfterDot(string fullId)
    {
        var idx = fullId.LastIndexOf('.');
        return idx >= 0 ? fullId[(idx + 1)..] : fullId;
    }

    private static LuaDocument EnsureGlobalTableExists(LuaDocument doc, string globalName)
    {
        if (LuaNavigator.FindAssignment(doc, globalName) is not null)
        {
            return doc;
        }

        // If missing entirely, create it as an empty table.
        return LuaNavigator.SetValueAtPath(doc, globalName, new LuaValue.LuaTable([]));
    }

    private static LuaDocument EnsureFieldTableExists(LuaDocument doc, string globalName, string fieldName)
    {
        // Prefer existing ["fieldName"] or identifier key. If missing, create using ["fieldName"] to avoid "List =" bugs.
        if (LuaNavigator.TryGetValueAtPath(doc, $"{globalName}[\"{fieldName}\"]", out var v1) && v1 is LuaValue.LuaTable)
        {
            return doc;
        }

        if (LuaNavigator.TryGetValueAtPath(doc, $"{globalName}.{fieldName}", out var v2) && v2 is LuaValue.LuaTable)
        {
            return doc;
        }

        return LuaNavigator.SetValueAtPath(doc, $"{globalName}[\"{fieldName}\"]", new LuaValue.LuaTable([]));
    }

    private static int GetGlobalArrayLength(LuaDocument doc, string globalName)
    {
        var a = LuaNavigator.FindAssignment(doc, globalName)
            ?? throw new InvalidOperationException($"Missing global assignment: {globalName}");

        if (a.Value is not LuaValue.LuaTable t)
        {
            throw new InvalidOperationException($"{globalName} is not a table");
        }

        return GetArrayLength(t);
    }

    private static int GetFieldArrayLength(LuaDocument doc, string globalName, string fieldName)
    {
        // Important: if it exists as ["List"] that’s what we want.
        if (LuaNavigator.TryGetValueAtPath(doc, $"{globalName}[\"{fieldName}\"]", out var v1) && v1 is LuaValue.LuaTable t1)
        {
            return GetArrayLength(t1);
        }

        // Fallback: it might be identifier style (List = { ... })
        if (LuaNavigator.TryGetValueAtPath(doc, $"{globalName}.{fieldName}", out var v2) && v2 is LuaValue.LuaTable t2)
        {
            return GetArrayLength(t2);
        }

        throw new InvalidOperationException($"{globalName} missing field table '{fieldName}'");
    }

    private static LuaValue GetAltGuildValueOrDefault(LuaDocument altDsDoc, int altLuaIndex)
    {
        // If missing, treat as 0.
        if (!LuaNavigator.TryGetValueAtPath(altDsDoc, string.Create(CultureInfo.InvariantCulture, $"DataStore_CharacterGuilds[{altLuaIndex}]"), out var v))
        {
            return new LuaValue.LuaNumber(0);
        }

        // DataStore uses numeric array values
        return v;
    }

    private static string EscapeLuaStringKey(string s)
    {
        // We only need to escape backslash and double-quote for ["..."] keys.
        return s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string LuaValueToDebug(LuaValue v)
    {
        return v switch
        {
            LuaValue.LuaNumber n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            LuaValue.LuaString s => $"\"{s.Value}\"",
            LuaValue.LuaBoolean b => b.Value ? "true" : "false",
            LuaValue.LuaNil => "nil",
            _ => v.GetType().Name,
        };
    }

}
