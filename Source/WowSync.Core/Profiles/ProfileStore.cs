namespace WowSync.Core.Profiles;

using System.Text.Json;
using System.Text.RegularExpressions;

public sealed partial class ProfileStore
{
    private static readonly JsonSerializerOptions options = new()
    {
        WriteIndented = true,
    };

    public string ProfilesFolder { get; }

    public ProfileStore()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        this.ProfilesFolder = Path.Combine(root, "WowSync", "profiles");
        Directory.CreateDirectory(this.ProfilesFolder);
    }

    public IReadOnlyList<string> ListProfileFiles()
    {
        return [.. Directory.EnumerateFiles(this.ProfilesFolder, "*.json")];
    }

    public LuaSyncProfile Load(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<LuaSyncProfile>(json, options)
               ?? throw new InvalidOperationException("Failed to deserialize profile.");
    }

    public string Save(LuaSyncProfile profile)
    {
        var safeName = string.Join('_', profile.Name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "profile";
        }

        var path = Path.Combine(this.ProfilesFolder, safeName + ".json");
        var json = JsonSerializer.Serialize(profile, options);
        File.WriteAllText(path, json);
        return path;
    }

    public void Delete(string profileFilePath)
    {
        if (File.Exists(profileFilePath))
        {
            File.Delete(profileFilePath);
        }
    }

    public string Rename(string existingProfilePath, string newBaseName)
    {
        if (string.IsNullOrWhiteSpace(newBaseName))
        {
            throw new ArgumentException("New name cannot be empty.", nameof(newBaseName));
        }

        var dir = Path.GetDirectoryName(existingProfilePath) ?? this.ProfilesFolder;
        var safe = MakeSafeFileName(newBaseName);
        var newPath = Path.Combine(dir, safe + ".json");

        if (File.Exists(newPath))
        {
            throw new IOException($"Target already exists: {newPath}");
        }

        File.Move(existingProfilePath, newPath);
        return newPath;
    }

    private static string MakeSafeFileName(string name)
    {
        // Boring and Windows-safe
        var s = name.Trim();
        s = WhitespaceRegex().Replace(s, " ");
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            s = s.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(s) ? "profile" : s;
    }

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WhitespaceRegex();
}
