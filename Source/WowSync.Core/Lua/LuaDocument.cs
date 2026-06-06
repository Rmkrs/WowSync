namespace WowSync.Core.Lua;

using System.Collections.Generic;

public sealed class LuaDocument(IReadOnlyList<LuaAssignment> assignments)
{
    public IReadOnlyList<LuaAssignment> Assignments { get; } = assignments;

    public bool HasSingleAssignment => this.Assignments.Count == 1;

    public string? SingleGlobalName => this.Assignments.Count == 1 ? this.Assignments[0].Name : null;
}