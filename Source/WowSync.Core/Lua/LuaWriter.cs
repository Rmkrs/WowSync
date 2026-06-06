namespace WowSync.Core.Lua;

using System.Globalization;
using System.Text;

public static class LuaWriter
{
    public static string WriteDocument(LuaDocument doc)
    {
        var sb = new StringBuilder();

        for (var i = 0; i < doc.Assignments.Count; i++)
        {
            var a = doc.Assignments[i];
            sb.Append(a.Name).Append(" = ");
            WriteValue(sb, a.Value, indent: 0);

            if (i < doc.Assignments.Count - 1)
            {
                sb.AppendLine();
                sb.AppendLine();
            }
        }

        sb.AppendLine(); // ✅
        return sb.ToString();
    }

    private static void WriteLuaStringLiteral(StringBuilder sb, string value)
    {
        sb.Append('"');

        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\':
                    sb.Append(@"\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\n':
                    sb.Append(@"\n");
                    break;
                case '\r':
                    sb.Append(@"\r");
                    break;
                case '\t':
                    sb.Append(@"\t");
                    break;
                case '\0':
                    sb.Append(@"\0");
                    break;

                default:
                    if (char.IsControl(ch))
                    {
                        sb.Append('\\');
                        sb.Append(((int)ch).ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }

        sb.Append('"');
    }

    private static void WriteValue(StringBuilder sb, LuaValue value, int indent)
    {
        switch (value)
        {
            case LuaValue.LuaNil:
                sb.Append("nil");
                return;

            case LuaValue.LuaBoolean b:
                sb.Append(b.Value ? "true" : "false");
                return;

            case LuaValue.LuaNumber n:
                sb.Append(n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return;

            case LuaValue.LuaString s:
                WriteLuaStringLiteral(sb, s.Value);
                return;

            case LuaValue.LuaTable t:
                WriteTable(sb, t, indent);
                return;

            default:
                throw new NotSupportedException($"Unsupported LuaValue: {value.GetType().Name}");
        }
    }

    private static void WriteTable(StringBuilder sb, LuaValue.LuaTable table, int indent)
    {
        sb.AppendLine("{");
        var childIndent = indent + 1;

        foreach (var entry in table.Entries)
        {
            sb.Append(new string('\t', childIndent));
            WriteKey(sb, entry.Key);
            sb.Append(" = ");
            WriteValue(sb, entry.Value, childIndent);
            sb.AppendLine(",");
        }

        sb.Append(new string('\t', indent));
        sb.Append('}');
    }

    private static void WriteKey(StringBuilder sb, LuaKey key)
    {
        switch (key)
        {
            case LuaKey.IdentifierKey i:
                if (IsValidLuaIdentifier(i.Value))
                {
                    sb.Append(i.Value);
                }
                else
                {
                    sb.Append('[');
                    WriteLuaStringLiteral(sb, i.Value);
                    sb.Append(']');
                }
                return;

            case LuaKey.StringKey s:
                sb.Append('[');
                WriteLuaStringLiteral(sb, s.Value);
                sb.Append(']');
                return;

            case LuaKey.NumberKey n:
                sb.Append('[');
                sb.Append(n.Value.ToString(CultureInfo.InvariantCulture));
                sb.Append(']');
                return;

            default:
                throw new NotSupportedException($"Unsupported LuaKey: {key.GetType().Name}");
        }
    }

    private static bool IsValidLuaIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return false;
        }

        if (!(char.IsLetter(s[0]) || s[0] == '_'))
        {
            return false;
        }

        for (var idx = 1; idx < s.Length; idx++)
        {
            var c = s[idx];
            if (!(char.IsLetterOrDigit(c) || c == '_'))
            {
                return false;
            }
        }

        // Optional: exclude keywords (rare in SavedVariables keys, but safe)
        return s is not ("true" or "false" or "nil");
    }
}
