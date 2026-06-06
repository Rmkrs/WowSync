namespace WowSync.Core.Profiles;

using System.Collections.Generic;
using WowSync.Core.Lua;

public interface IPathExpander
{
    /// <summary>
    /// Returns expanded target paths for this include path, based on the TARGET document.
    /// If no expansion applies, return the original includePath as a single item.
    /// </summary>
    IReadOnlyList<string> Expand(LuaDocument targetDoc, string includePath);
}