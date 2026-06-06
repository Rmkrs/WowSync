// ReSharper disable GrammarMistakeInComment
namespace WowSync.Core.Lua;

using System.Globalization;

public sealed class LuaParser
{
    private readonly LuaTokenizer tokenizer;
    private Token current;

    public LuaParser(string text)
    {
        this.tokenizer = new LuaTokenizer(text);
        this.current = this.tokenizer.Next();
    }

    public LuaDocument ParseDocument()
    {
        var assignments = new List<LuaAssignment>();

        while (this.current.Kind != TokenKind.Eof)
        {
            assignments.Add(this.ParseAssignment());

            // Optional: allow stray semicolons between statements.
            while (this.Match(TokenKind.Semicolon))
            {
            }
        }

        return new LuaDocument(assignments);
    }

    private LuaAssignment ParseAssignment()
    {
        var name = this.Expect(TokenKind.Identifier).Text;
        this.Expect(TokenKind.Equals);

        var value = this.ParseValue();

        // Some files may end statements with ';' (rare but valid)
        this.Match(TokenKind.Semicolon);

        return new LuaAssignment(name, value);
    }

    private LuaValue ParseValue()
    {
        switch (this.current.Kind)
        {
            case TokenKind.LBrace:
                return this.ParseTable();

            case TokenKind.Text:
                return new LuaValue.LuaString(this.Expect(TokenKind.Text).Text);

            case TokenKind.Number:
                return new LuaValue.LuaNumber(double.Parse(this.Expect(TokenKind.Number).Text, CultureInfo.InvariantCulture));

            case TokenKind.True:
                this.Advance();
                return new LuaValue.LuaBoolean(Value: true);

            case TokenKind.False:
                this.Advance();
                return new LuaValue.LuaBoolean(Value: false);

            case TokenKind.Nil:
                this.Advance();
                return new LuaValue.LuaNil();

            case TokenKind.Eof:
            case TokenKind.Identifier:
            case TokenKind.RBrace:
            case TokenKind.LBracket:
            case TokenKind.RBracket:
            case TokenKind.Equals:
            case TokenKind.Comma:
            case TokenKind.Semicolon:
            default:
                {
                    var (line, col) = this.tokenizer.GetLineCol(this.current.Position);

                    throw new FormatException(
                        $"Unexpected token {this.current.Kind} ('{this.current.Text}') in value " +
                        string.Create(CultureInfo.InvariantCulture, $"at position {this.current.Position} (line {line}, col {col}).\n") +
                        this.tokenizer.Context(this.current.Position));
                }
        }
    }

    private LuaValue.LuaTable ParseTable()
    {
        this.Expect(TokenKind.LBrace);

        var entries = new List<LuaTableEntry>();

        while (this.current.Kind != TokenKind.RBrace)
        {
            // Allow trailing separators
            if (this.Match(TokenKind.Comma) || this.Match(TokenKind.Semicolon))
            {
                continue;
            }

            // Entry can be:
            // [ "key" ] = value
            // [ 1 ] = value
            // identifier = value
            // value (array-style)  -> implicit numeric keys (we’ll store as NumberKey increment)
            if (this.current.Kind == TokenKind.LBracket)
            {
                var key = this.ParseBracketKey();
                this.Expect(TokenKind.Equals);
                var value = this.ParseValue();
                entries.Add(new LuaTableEntry(key, value));
            }
            else if (this.current.Kind == TokenKind.Identifier)
            {
                // Could be identifier = value OR bare identifier as value? In SV it’s almost always key=value.
                var ident = this.Expect(TokenKind.Identifier).Text;
                if (this.Match(TokenKind.Equals))
                {
                    var value = this.ParseValue();
                    entries.Add(new LuaTableEntry(new LuaKey.IdentifierKey(ident), value));
                }
                else
                {
                    // Treat as value in array-style: { foo, bar }
                    // Represent foo as string value for now (rare in SV).
                    entries.Add(new LuaTableEntry(new LuaKey.NumberKey(entries.Count + 1), new LuaValue.LuaString(ident)));
                }
            }
            else
            {
                // Array-style value
                var value = this.ParseValue();
                entries.Add(new LuaTableEntry(new LuaKey.NumberKey(entries.Count + 1), value));
            }

            // Optional separators
            this.Match(TokenKind.Comma);
            this.Match(TokenKind.Semicolon);
        }

        this.Expect(TokenKind.RBrace);
        return new LuaValue.LuaTable(entries);
    }

    private LuaKey ParseBracketKey()
    {
        this.Expect(TokenKind.LBracket);

        LuaKey key = this.current.Kind switch
        {
            TokenKind.Text => new LuaKey.StringKey(this.Expect(TokenKind.Text).Text),
            TokenKind.Number => new LuaKey.NumberKey(double.Parse(this.Expect(TokenKind.Number).Text, CultureInfo.InvariantCulture)),
            TokenKind.Identifier => new LuaKey.IdentifierKey(this.Expect(TokenKind.Identifier).Text),
            _ => throw new FormatException($"Unexpected token {this.current.Kind} in bracket key."),
        };

        this.Expect(TokenKind.RBracket);
        return key;
    }

    private Token Expect(TokenKind kind)
    {
        if (this.current.Kind != kind)
        {
            var (line, col) = this.tokenizer.GetLineCol(this.current.Position);
            throw new FormatException(
                $"Expected {kind} but found {this.current.Kind} ('{this.current.Text}') " +
                string.Create(CultureInfo.InvariantCulture, $"at position {this.current.Position} (line {line}, col {col}).\n") +
                this.tokenizer.Context(this.current.Position));
        }

        var t = this.current;
        this.Advance();
        return t;
    }

    private bool Match(TokenKind kind)
    {
        if (this.current.Kind == kind)
        {
            this.Advance();
            return true;
        }

        return false;
    }

    private void Advance()
    {
        this.current = this.tokenizer.Next();
    }
}
