namespace WowSync.Core.Lua;

public enum TokenKind
{
    Eof,
    Identifier,
    Text,
    Number,
    True,
    False,
    Nil,
    LBrace,
    RBrace,
    LBracket,
    RBracket,
    Equals,
    Comma,
    Semicolon,
}
