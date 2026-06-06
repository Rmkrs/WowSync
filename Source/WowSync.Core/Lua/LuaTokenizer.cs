namespace WowSync.Core.Lua;

using System.Globalization;
using System.Text;

public sealed class LuaTokenizer(string text)
{
    private int i;

    public Token Next()
    {
        this.SkipWhitespaceAndComments();

        if (this.i >= text.Length)
        {
            return new Token(TokenKind.Eof, "", this.i);
        }

        var start = this.i;
        var c = text[this.i];

        switch (c)
        {
            case '{':
                this.i++;
                return new Token(TokenKind.LBrace, "{", start);
            case '}':
                this.i++;
                return new Token(TokenKind.RBrace, "}", start);
            case '[':
                this.i++;
                return new Token(TokenKind.LBracket, "[", start);
            case ']':
                this.i++;
                return new Token(TokenKind.RBracket, "]", start);
            case '=':
                this.i++;
                return new Token(TokenKind.Equals, "=", start);
            case ',':
                this.i++;
                return new Token(TokenKind.Comma, ",", start);
            case ';':
                this.i++;
                return new Token(TokenKind.Semicolon, ";", start);
            default:
                break;
        }

        if (c is '"' or '\'')
        {
            return this.ReadString(start);
        }

        if (char.IsDigit(c) || (c == '-' && this.i + 1 < text.Length && char.IsDigit(text[this.i + 1])))
        {
            return this.ReadNumber(start);
        }

        if (IsIdentStart(c))
        {
            return this.ReadIdentifierOrKeyword(start);
        }

        throw new FormatException(this.BuildUnexpectedCharMessage(c, this.i));
    }

    public (int line, int col) GetLineCol(int pos)
    {
        var line = 1;
        var col = 1;

        for (var j = 0; j < pos && j < text.Length; j++)
        {
            if (text[j] == '\n')
            {
                line++;
                col = 1;
            }
            else
            {
                col++;
            }
        }

        return (line, col);
    }

    public string Context(int pos, int radius = 120)
    {
        pos = Math.Clamp(pos, 0, text.Length);
        var start = Math.Max(0, pos - radius);
        var end = Math.Min(text.Length, pos + radius);

        var snippet = text[start..end]
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

        var caretPos = pos - start;
        var caret = new string(' ', caretPos) + "^";

        return $"Context: \"{snippet}\"\n         {caret}";
    }

    private string BuildUnexpectedCharMessage(char c, int pos)
    {
        var (line, col) = this.GetLineCol(pos);

        var start = Math.Max(0, pos - 40);
        var end = Math.Min(text.Length, pos + 40);
        var snippet = text[start..end]
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

        // caret points to the character (approx, within snippet window)
        var caretPos = pos - start;
        var caret = new string(' ', Math.Max(0, caretPos)) + "^";

        return string.Create(CultureInfo.InvariantCulture, $"Unexpected character '{c}' at position {pos} (line {line}, col {col}). Context: \"{snippet}\" {caret}");
    }

    private void SkipWhitespaceAndComments()
    {
        while (this.i < text.Length)
        {
            // whitespace
            if (char.IsWhiteSpace(text[this.i]))
            {
                this.i++;
                continue;
            }

            // comment: -- ...
            if (text[this.i] == '-' && this.i + 1 < text.Length && text[this.i + 1] == '-')
            {
                this.i += 2;
                while (this.i < text.Length && text[this.i] != '\n')
                {
                    this.i++;
                }

                continue;
            }

            break;
        }
    }

    private Token ReadString(int startPos)
    {
        var quote = text[this.i];
        this.i++;

        var sb = new StringBuilder();
        while (this.i < text.Length)
        {
            var c = text[this.i++];

            if (c == quote)
            {
                return new Token(TokenKind.Text, sb.ToString(), startPos);
            }

            if (c == '\\' && this.i < text.Length)
            {
                var esc = text[this.i++];
                sb.Append(esc switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '\\' => '\\',
                    '"' => '"',
                    '\'' => '\'',
                    _ => esc,
                });
                continue;
            }

            sb.Append(c);
        }

        throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"Unterminated string literal at {startPos}."));
    }

    private Token ReadNumber(int startPos)
    {
        var start = this.i;

        // optional leading '-'
        if (text[this.i] == '-')
        {
            this.i++;
        }

        var hasAnyDigit = false;

        // integer / fractional part
        while (this.i < text.Length)
        {
            var c = text[this.i];
            if (char.IsDigit(c))
            {
                hasAnyDigit = true;
                this.i++;
                continue;
            }

            if (c == '.')
            {
                this.i++;
                continue;
            }

            break;
        }

        if (!hasAnyDigit)
        {
            // This also protects you from returning "" or "-" etc.
            throw new FormatException(this.BuildUnexpectedCharMessage(text[Math.Min(start, text.Length - 1)], start) + " (invalid number start)");
        }

        // exponent part: e.g. 1e+06, 1E-3
        if (this.i < text.Length && (text[this.i] == 'e' || text[this.i] == 'E'))
        {
            this.i++; // consume e/E

            if (this.i < text.Length && (text[this.i] == '+' || text[this.i] == '-'))
            {
                this.i++;
            }

            var expStart = this.i;
            while (this.i < text.Length && char.IsDigit(text[this.i]))
            {
                this.i++;
            }

            if (expStart == this.i)
            {
                throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"Invalid numeric exponent at position {expStart}."));
            }
        }

        var raw = text[start..this.i];

        // Validate (and ensure raw is never empty)
        _ = double.Parse(raw, CultureInfo.InvariantCulture);

        return new Token(TokenKind.Number, raw, startPos);
    }

    private Token ReadIdentifierOrKeyword(int startPos)
    {
        var start = this.i;
        this.i++;
        while (this.i < text.Length && IsIdentPart(text[this.i]))
        {
            this.i++;
        }

        var raw = text[start..this.i];

        return raw switch
        {
            "true" => new Token(TokenKind.True, raw, startPos),
            "false" => new Token(TokenKind.False, raw, startPos),
            "nil" => new Token(TokenKind.Nil, raw, startPos),
            _ => new Token(TokenKind.Identifier, raw, startPos),
        };
    }

    private static bool IsIdentStart(char c)
    {
        return char.IsLetter(c) || c == '_';
    }

    private static bool IsIdentPart(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }
}
