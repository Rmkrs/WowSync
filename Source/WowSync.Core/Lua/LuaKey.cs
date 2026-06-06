namespace WowSync.Core.Lua;

public abstract record LuaKey
{
    public sealed record StringKey(string Value) : LuaKey;

    public sealed record NumberKey(double Value) : LuaKey;

    public sealed record IdentifierKey(string Value) : LuaKey;
}