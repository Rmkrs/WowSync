namespace WowSync.Core.Lua;

public readonly record struct Token(TokenKind Kind, string Text, int Position);
