namespace WowSync.Core.Lua;

public static class LuaTableExtensions
{
    public static LuaKey ToLuaKey(this LuaPath.Segment seg)
    {
        return seg switch
        {
            LuaPath.Segment.Identifier id => new LuaKey.IdentifierKey(id.Name),
            LuaPath.Segment.StringIndex s => new LuaKey.StringKey(s.Value),
            LuaPath.Segment.NumberIndex n => new LuaKey.NumberKey(n.Value),
            _ => new LuaKey.StringKey(seg.ToString() ?? "?"),
        };
    }
}
