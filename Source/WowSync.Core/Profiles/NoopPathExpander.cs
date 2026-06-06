namespace WowSync.Core.Profiles;

using System.Collections.Generic;
using WowSync.Core.Lua;

public sealed class NoopPathExpander : IPathExpander
{
    public static readonly NoopPathExpander Instance = new();

    public IReadOnlyList<string> Expand(LuaDocument targetDoc, string includePath)
    {
        return [includePath];
    }
}