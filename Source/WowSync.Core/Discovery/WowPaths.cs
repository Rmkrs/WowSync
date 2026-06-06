namespace WowSync.Core.Discovery;

public static class WowPaths
{
    public static string NormalizeRootFromExePath(string wowExePath)
    {
        if (string.IsNullOrWhiteSpace(wowExePath))
        {
            throw new ArgumentException("Path is null/empty.", nameof(wowExePath));
        }

        var full = Path.GetFullPath(wowExePath);

        if (Directory.Exists(full))
        {
            // user picked a folder (allow it)
            return full;
        }

        // user picked wow.exe
        return Path.GetDirectoryName(full) ?? full;
    }

    public static string GetWtfPath(string wowRoot)
    {
        return Path.Combine(wowRoot, "WTF");
    }

    public static string GetAccountRoot(string wowRoot)
    {
        return Path.Combine(GetWtfPath(wowRoot), "Account");
    }
}
