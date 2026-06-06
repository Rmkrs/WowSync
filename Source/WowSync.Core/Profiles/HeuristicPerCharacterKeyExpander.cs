// ReSharper disable CommentTypo
namespace WowSync.Core.Profiles;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using WowSync.Core.Lua;

/// <summary>
/// Generic expander for SavedVariables that store per-character data in a table keyed by strings like:
/// "Name - Realm", "Realm-Name", etc.
///
/// If an includePath contains a string key segment that looks like a character key, and the parent table
/// contains multiple similar string keys, we expand that segment to all sibling keys.
/// </summary>
public sealed class HeuristicPerCharacterKeyExpander : IPathExpander
{
    public IReadOnlyList<string> Expand(LuaDocument targetDoc, string includePath)
    {
        LuaPath path;
        try
        {
            path = LuaPath.Parse(includePath);
        }
        catch
        {
            // If we can't parse the path, do no expansion.
            return [includePath];
        }

        // Find a candidate segment: a string index that looks like a toon key and whose parent is a table with siblings.
        // We pick the FIRST match walking from left to right.
        for (var i = 0; i < path.Segments.Count; i++)
        {
            if (path.Segments[i] is not LuaPath.Segment.StringIndex si)
            {
                continue;
            }

            if (!LooksLikeCharacterKey(si.Value))
            {
                continue;
            }

            // Parent path string: GlobalName + segments up to (but excluding) i
            var parentPathString = BuildPathString(path.GlobalName, path.Segments.Take(i));

            if (!LuaNavigator.TryGetValueAtPath(targetDoc, parentPathString, out var parentValue))
            {
                continue;
            }

            if (parentValue is not LuaValue.LuaTable parentTable)
            {
                continue;
            }

            // Collect sibling string keys that also look like character keys
            var siblingKeys = parentTable.Entries
                .Select(e => e.Key)
                .OfType<LuaKey.StringKey>()
                .Select(k => k.Value)
                .Where(LooksLikeCharacterKey)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (siblingKeys.Count <= 1)
            {
                continue;
            }

            // Must include the original key; otherwise it’s probably not the right table
            if (!siblingKeys.Contains(si.Value, StringComparer.Ordinal))
            {
                continue;
            }

            // Expand: replace that segment with each sibling key, return as path strings
            var expanded = new List<string>(siblingKeys.Count);

            foreach (var key in siblingKeys)
            {
                var segments = path.Segments.ToArray();
                segments[i] = new LuaPath.Segment.StringIndex(key);

                expanded.Add(BuildPathString(path.GlobalName, segments));
            }

            return expanded;
        }

        return [includePath];
    }

    private static string BuildPathString(string globalName, IEnumerable<LuaPath.Segment> segments)
    {
        var s = globalName;

        foreach (var seg in segments)
        {
            s += ".";
            s += seg switch
            {
                LuaPath.Segment.Identifier id => id.Name,
                LuaPath.Segment.StringIndex si => $"[\"{Escape(si.Value)}\"]",
                LuaPath.Segment.NumberIndex ni => $"[{ni.Value.ToString("G", CultureInfo.InvariantCulture)}]",
                _ => "?",
            };
        }

        return s;
    }

    private static string Escape(string s)
    {
        return s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static bool LooksLikeCharacterKey(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        // Common SavedVariables patterns:
        // "Name - Realm"  (spaces around dash)
        // "Realm-Name"    (no spaces)
        if (s.Contains(" - ", StringComparison.Ordinal))
        {
            return true;
        }

        // More permissive fallback: contains '-' and is not insanely long.
        // Avoid expanding random GUID-like or huge keys.
        if (s.Length <= 64 && s.Contains('-', StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
