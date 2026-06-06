namespace WowSync.App.LuaInspector;

using System.Globalization;
using WowSync.Core.Lua;

public static class LuaTreeBuilder
{
    public static LuaTreeNode Build(LuaAssignment a)
    {
        return BuildValue(a.Name, a.Value, a.Name);
    }

    private static LuaTreeNode BuildValue(string name, LuaValue value, string path)
    {
        var displayValue = Describe(value);

        var node = new LuaTreeNode(name, path, displayValue);

        AddChildren(node, value, path);

        return node;
    }

    private static void AddChildren(LuaTreeNode parent, LuaValue value, string path)
    {
        if (value is not LuaValue.LuaTable table)
        {
            return;
        }

        foreach (var entry in table.Entries)
        {
            var keyText = KeyToPathSegment(entry.Key);
            var childPath = keyText.StartsWith('[')
                ? path + keyText
                : path + "." + keyText;

            var name = KeyToDisplay(entry.Key);
            var displayValue = Describe(entry.Value);

            var child = new LuaTreeNode(name, childPath, displayValue);
            parent.Children.Add(child);

            AddChildren(child, entry.Value, childPath);
        }
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
            _ => "",
        };
    }

    private static string KeyToDisplay(LuaKey k)
    {
        return k switch
        {
            LuaKey.IdentifierKey id => id.Value,
            LuaKey.StringKey s => s.Value,
            LuaKey.NumberKey n => n.Value.ToString("G", CultureInfo.InvariantCulture),
            _ => "?",
        };
    }

    private static string KeyToPathSegment(LuaKey k)
    {
        return k switch
        {
            LuaKey.IdentifierKey id => id.Value,
            LuaKey.StringKey s => $"[\"{Escape(s.Value)}\"]",
            LuaKey.NumberKey n => string.Create(CultureInfo.InvariantCulture, $"[{n.Value:G}]"),
            _ => "?",
        };
    }

    private static string Escape(string s)
    {
        return s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
