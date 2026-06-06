// ReSharper disable IdentifierTypo
namespace WowSync.Core.Lua;

using System;
using System.Collections.Generic;

public static class LuaDeepComparer
{
    public static bool AreEquivalent(LuaDocument a, LuaDocument b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        // Strict document shape: same number of assignments, same order, same names.
        // (For SavedVariables, assignment order is normally stable.)
        if (a.Assignments.Count != b.Assignments.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Assignments.Count; i++)
        {
            var aa = a.Assignments[i];
            var bb = b.Assignments[i];

            if (!string.Equals(aa.Name, bb.Name, StringComparison.Ordinal))
            {
                return false;
            }

            if (!AreEquivalent(aa.Value, bb.Value))
            {
                return false;
            }
        }

        return true;
    }

    public static bool AreEquivalent(LuaValue a, LuaValue b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        // Match by runtime type
        if (a.GetType() != b.GetType())
        {
            return false;
        }

        return a switch
        {
            LuaValue.LuaNil => true,

            LuaValue.LuaBoolean ab when b is LuaValue.LuaBoolean bb =>
                ab.Value == bb.Value,

            LuaValue.LuaNumber an when b is LuaValue.LuaNumber bn =>
                an.Value.Equals(bn.Value),

            LuaValue.LuaString @as when b is LuaValue.LuaString bs =>
                string.Equals(@as.Value, bs.Value, StringComparison.Ordinal),

            LuaValue.LuaTable at when b is LuaValue.LuaTable bt =>
                AreEquivalent(at, bt),

            _ => false,
        };
    }

    private static bool AreEquivalent(LuaValue.LuaTable a, LuaValue.LuaTable b)
    {
        if (a.Entries.Count != b.Entries.Count)
        {
            return false;
        }

        // Treat table entries as an unordered multimap keyed by LuaKey (or "array position" entries where Key is null).
        var amap = BuildEntryMultimap(a.Entries);
        var bmap = BuildEntryMultimap(b.Entries);

        if (amap.Count != bmap.Count)
        {
            return false;
        }

        foreach (var (key, alist) in amap)
        {
            if (!bmap.TryGetValue(key, out var blist))
            {
                return false;
            }

            if (alist.Count != blist.Count)
            {
                return false;
            }

            // We compare as an unordered multiset of values for the same key.
            // For SavedVariables, duplicate keys are rare, but this keeps us correct.
            if (!UnorderedValuesEqual(alist, blist))
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<TableKey, List<LuaValue>> BuildEntryMultimap(IReadOnlyList<LuaTableEntry> entries)
    {
        var map = new Dictionary<TableKey, List<LuaValue>>();

        foreach (var e in entries)
        {
            var key = new TableKey(e.Key); // null allowed
            if (!map.TryGetValue(key, out var list))
            {
                list = [];
                map[key] = list;
            }

            list.Add(e.Value);
        }

        return map;
    }

    private static bool UnorderedValuesEqual(List<LuaValue> a, List<LuaValue> b)
    {
        // O(n^2) matching is fine for the small-per-key case we expect.
        // (Array keys are unique; identifier keys are unique; duplicates are rare.)
        var used = new bool[b.Count];

        foreach (var av in a)
        {
            var matched = false;

            for (var i = 0; i < b.Count; i++)
            {
                if (used[i])
                {
                    continue;
                }

                if (AreEquivalent(av, b[i]))
                {
                    used[i] = true;
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                return false;
            }
        }

        return true;
    }

    private readonly record struct TableKey(LuaKey? Key);
}
