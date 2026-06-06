namespace WowSync.Core.Lua;

public static class LuaRoundTripValidator
{
    public static void ValidateOrThrow(string targetFile, string luaText)
    {
        try
        {
            _ = new LuaParser(luaText).ParseDocument();
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"Refusing to write invalid Lua for target '{targetFile}'. Generated output failed to parse.\n\n{ex.Message}",
                ex);
        }
    }
}