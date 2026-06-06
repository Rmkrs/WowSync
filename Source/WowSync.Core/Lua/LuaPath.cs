namespace WowSync.Core.Lua;

using System;
using System.Collections.Generic;
using System.Globalization;

public sealed class LuaPath
{
    private LuaPath(string globalName, IReadOnlyList<Segment> segments)
    {
        this.GlobalName = globalName;
        this.Segments = segments;
    }

    public string GlobalName { get; }

    public IReadOnlyList<Segment> Segments { get; }

    public static LuaPath Parse(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be empty.", nameof(path));
        }

        var i = 0;
        ReadIdentifier(path, ref i, out var globalName);

        var segments = new List<Segment>();

        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                i++;

                // After '.', must be identifier (no ".[" legacy anymore)
                ReadIdentifier(path, ref i, out var ident);
                segments.Add(new Segment.Identifier(ident));
                continue;
            }

            if (path[i] == '[')
            {
                segments.Add(ReadBracketSegment(path, ref i));
                continue;
            }

            throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"Expected '.' or '[' at position {i} in path: {path}"));
        }

        return new LuaPath(globalName, segments);
    }

    public override string ToString()
    {
        var s = this.GlobalName;
        foreach (var seg in this.Segments)
        {
            s += ".";
            s += seg switch
            {
                Segment.Identifier id => id.Name,
                Segment.StringIndex si => $"[\"{Escape(si.Value)}\"]",
                Segment.NumberIndex ni => $"[{ni.Value.ToString("G", CultureInfo.InvariantCulture)}]",
                _ => "?",
            };
        }

        return s;
    }

    private static Segment ReadBracketSegment(string text, ref int i)
    {
        // Expect '['
        if (text[i] != '[')
        {
            throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"Expected '[' at position {i}."));
        }

        i++;

        if (i >= text.Length)
        {
            throw new FormatException("Unexpected end while reading bracket segment.");
        }

        // ["string"]
        if (text[i] == '"')
        {
            i++; // skip quote
            var start = i;
            var value = "";

            while (i < text.Length)
            {
                var c = text[i++];

                if (c == '"')
                {
                    // done
                    value = text[start..(i - 1)];
                    break;
                }

                // We allow escaped quotes/backslashes in the path
                if (c == '\\' && i < text.Length)
                {
                    i++; // skip escaped char in raw scan; we’ll unescape later
                }
            }

            value = Unescape(value);

            if (i >= text.Length || text[i] != ']')
            {
                throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"Expected ']' after string bracket segment at position {i}."));
            }

            i++; // skip ]
            return new Segment.StringIndex(value);
        }

        // [number]
        var numStart = i;
        while (i < text.Length && text[i] != ']')
        {
            i++;
        }

        if (i >= text.Length)
        {
            throw new FormatException("Unterminated bracket segment, missing ']'.");
        }

        var raw = text[numStart..i].Trim();
        i++; // skip ]

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            throw new FormatException($"Invalid numeric bracket segment: [{raw}]");
        }

        return new Segment.NumberIndex(number);
    }

    private static void ReadIdentifier(string text, ref int i, out string identifier)
    {
        if (i >= text.Length || !(char.IsLetter(text[i]) || text[i] == '_'))
        {
            throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"Expected identifier at position {i}."));
        }

        var start = i;
        i++;

        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
        {
            i++;
        }

        identifier = text[start..i];
    }

    private static string Escape(string s)
    {
        return s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string Unescape(string s)
    {
        return s.Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    public abstract record Segment
    {
        public sealed record Identifier(string Name) : Segment;
        public sealed record StringIndex(string Value) : Segment;
        public sealed record NumberIndex(double Value) : Segment;
    }
}
