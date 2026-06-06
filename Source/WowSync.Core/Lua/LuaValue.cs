namespace WowSync.Core.Lua;

public abstract record LuaValue
{
    public sealed record LuaNil : LuaValue;

    public sealed record LuaBoolean(bool Value) : LuaValue;

    public sealed record LuaNumber(double Value) : LuaValue;

    public sealed record LuaString(string Value) : LuaValue;

    /// <summary>
    /// Represents { [key]=value, key=value, 1=value, ... }
    /// We store entries as list to preserve ordering.
    /// </summary>
    public sealed record LuaTable(IReadOnlyList<LuaTableEntry> Entries) : LuaValue;
}
