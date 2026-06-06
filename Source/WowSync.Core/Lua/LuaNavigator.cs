namespace WowSync.Core.Lua;

using System;
using System.Collections.Generic;
using System.Linq;

public static class LuaNavigator
{
    public static LuaAssignment? FindAssignment(LuaDocument doc, string name)
    {
        return doc.Assignments.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));
    }

    public static bool TryGetValueAtPath(LuaDocument doc, string path, out LuaValue value)
    {
        var p = LuaPath.Parse(path);

        var a = FindAssignment(doc, p.GlobalName);
        if (a is null)
        {
            value = new LuaValue.LuaNil();
            return false;
        }

        var current = a.Value;

        foreach (var seg in p.Segments)
        {
            if (current is not LuaValue.LuaTable t)
            {
                value = new LuaValue.LuaNil();
                return false;
            }

            var key = seg.ToLuaKey();
            current = GetValueOrNil(t, key);
        }

        value = current;
        return true;
    }

    public static LuaDocument SetValueAtPath(LuaDocument doc, string path, LuaValue newValue)
    {
        var p = LuaPath.Parse(path);

        var updatedAssignments = doc.Assignments.ToList();
        var idx = updatedAssignments.FindIndex(a => string.Equals(a.Name, p.GlobalName, StringComparison.Ordinal));

        if (idx < 0)
        {
            // Create a fresh assignment with nested tables to the leaf
            var built = BuildTables(p.Segments, 0, newValue);
            updatedAssignments.Add(new LuaAssignment(p.GlobalName, built));
            return new LuaDocument(updatedAssignments);
        }

        var existing = updatedAssignments[idx];
        var updatedRoot = SetValueInValue(existing.Value, p.Segments, 0, newValue);

        updatedAssignments[idx] = existing with { Value = updatedRoot };
        return new LuaDocument(updatedAssignments);
    }

    public static IReadOnlyList<string> GetProfileNamesFromProfileKeys(LuaDocument doc, string globalName)
    {
        var a = FindAssignment(doc, globalName);
        if (a?.Value is not LuaValue.LuaTable root)
        {
            return [];
        }

        // Expect profileKeys under root["profileKeys"]
        var profileKeysKey = new LuaKey.StringKey("profileKeys");
        if (!TryGetValue(root, profileKeysKey, out var pkValue) || pkValue is not LuaValue.LuaTable profileKeys)
        {
            // Some addons use identifier instead of string key
            var idKey = new LuaKey.IdentifierKey("profileKeys");
            if (!TryGetValue(root, idKey, out pkValue) || pkValue is not LuaValue.LuaTable pk2)
            {
                return [];
            }

            profileKeys = pk2;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var e in profileKeys.Entries)
        {
            if (e.Value is LuaValue.LuaString s && !string.IsNullOrWhiteSpace(s.Value))
            {
                names.Add(s.Value);
            }
        }

        return [.. names];
    }

    private static LuaValue BuildTables(IReadOnlyList<LuaPath.Segment> segments, int index, LuaValue leaf)
    {
        if (index >= segments.Count)
        {
            return leaf;
        }

        var key = segments[index].ToLuaKey();
        var child = BuildTables(segments, index + 1, leaf);

        var entries = new List<LuaTableEntry>
        {
            new(key, child),
        };

        return new LuaValue.LuaTable(entries);
    }

    private static LuaValue SetValueInValue(LuaValue current, IReadOnlyList<LuaPath.Segment> segments, int index, LuaValue leaf)
    {
        if (index >= segments.Count)
        {
            return leaf;
        }

        var key = segments[index].ToLuaKey();

        // We need a table at this level to continue.
        var table = current as LuaValue.LuaTable ?? new LuaValue.LuaTable((List<LuaTableEntry>)[]);

        // Rebuild entries (persistent update)
        var list = table.Entries.ToList();

        var existingIndex = -1;
        LuaValue? existingChild = null;

        for (var i = 0; i < list.Count; i++)
        {
            if (LuaTableKeyEquals(list[i].Key, key))
            {
                existingIndex = i;
                existingChild = list[i].Value;
                break;
            }
        }

        var updatedChild = SetValueInValue(existingChild ?? new LuaValue.LuaNil(), segments, index + 1, leaf);

        if (existingIndex >= 0)
        {
            list[existingIndex] = new LuaTableEntry(list[existingIndex].Key, updatedChild);
        }
        else
        {
            list.Add(new LuaTableEntry(key, updatedChild));
        }

        return new LuaValue.LuaTable(list);
    }

    private static LuaValue GetValueOrNil(LuaValue.LuaTable table, LuaKey key)
    {
        return TryGetValue(table, key, out var v) ? v : new LuaValue.LuaNil();
    }

    private static bool TryGetValue(LuaValue.LuaTable table, LuaKey key, out LuaValue value)
    {
        foreach (var e in table.Entries)
        {
            if (LuaTableKeyEquals(e.Key, key))
            {
                value = e.Value;
                return true;
            }
        }

        value = new LuaValue.LuaNil();
        return false;
    }

    private static bool LuaTableKeyEquals(LuaKey a, LuaKey b)
    {
        return (a, b) switch
        {
            (LuaKey.IdentifierKey ia, LuaKey.IdentifierKey ib) => string.Equals(ia.Value, ib.Value, StringComparison.Ordinal),
            (LuaKey.StringKey sa, LuaKey.StringKey sb) => string.Equals(sa.Value, sb.Value, StringComparison.Ordinal),
            (LuaKey.NumberKey na, LuaKey.NumberKey nb) => Math.Abs(na.Value - nb.Value) < 0.0000001,
            _ => false,
        };
    }
}
