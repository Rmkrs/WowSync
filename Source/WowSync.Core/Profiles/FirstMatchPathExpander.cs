namespace WowSync.Core.Profiles;

public sealed class FirstMatchPathExpander(params IPathExpander[] expanders) : IPathExpander
{
    private readonly IReadOnlyList<IPathExpander> expanders = expanders;

    public IReadOnlyList<string> Expand(WowSync.Core.Lua.LuaDocument targetDoc, string includePath)
    {
        foreach (var e in this.expanders)
        {
            var expanded = e.Expand(targetDoc, includePath);

            // "Match" means: either multiple results, or one result different from input.
            if (expanded.Count != 1 || !string.Equals(expanded[0], includePath, StringComparison.Ordinal))
            {
                return expanded;
            }
        }

        return [includePath];
    }
}