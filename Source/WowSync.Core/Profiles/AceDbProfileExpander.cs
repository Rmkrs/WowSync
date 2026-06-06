// ReSharper disable InvalidXmlDocComment
namespace WowSync.Core.Profiles;

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WowSync.Core.Lua;

public sealed class AceDbProfileExpander : IPathExpander
{
    /// <summary>
    /// If includePath looks like:
    ///   Global.["profiles"].["SomeProfile"].(...)
    /// Expand it into:
    ///   Global.["profiles"].["--each-profile-name-from-profileKeys--"].(...)
    /// using profileKeys within the SAME Global table in the TARGET doc.
    /// </summary>
    public IReadOnlyList<string> Expand(LuaDocument targetDoc, string includePath)
    {
        LuaPath p;

        try
        {
            p = LuaPath.Parse(includePath);
        }
        catch
        {
            return [includePath];
        }

        // Must have at least: .["profiles"].["X"]
        if (p.Segments.Count < 2)
        {
            return [includePath];
        }

        // First segment must be ["profiles"]
        if (p.Segments[0] is not LuaPath.Segment.StringIndex s0 || !string.Equals(s0.Value, "profiles", StringComparison.Ordinal))
        {
            return [includePath];
        }

        // Second segment must be a string index (profile name)
        if (p.Segments[1] is not LuaPath.Segment.StringIndex)
        {
            return [includePath];
        }

        var profileNames = LuaNavigator.GetProfileNamesFromProfileKeys(targetDoc, p.GlobalName);
        if (profileNames.Count == 0)
        {
            return [includePath];
        }

        var results = new List<string>(profileNames.Count);
        foreach (var name in profileNames)
        {
            results.Add(RebuildWithProfile(p, name));
        }

        return results;
    }

    private static string RebuildWithProfile(LuaPath path, string profileName)
    {
        // Replace segment[1]
        var segments = path.Segments.ToList();
        segments[1] = new LuaPath.Segment.StringIndex(profileName);

        var s = path.GlobalName;
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
}
