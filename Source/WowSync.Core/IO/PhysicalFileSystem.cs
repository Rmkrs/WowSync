namespace WowSync.Core.IO;

using System.Text;
using WowSync.Plugins.Abstractions.Operations;

public sealed class PhysicalFileSystem : IFileSystem
{
    public bool FileExists(string path)
    {
        return File.Exists(path);
    }

    public bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }

    public Task<string> ReadAllTextAsync(string path, CancellationToken ct)
    {
        return File.ReadAllTextAsync(path, ct);
    }

    public async Task WriteAllTextAtomicAsync(string path, string content, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, content, Encoding.UTF8, ct);

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(tmp, path);
    }

    public Task CopyFileAsync(string source, string destination, bool overwrite, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // No async API, but this is fast for small files (SavedVariables).
        File.Copy(source, destination, overwrite);
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path, CancellationToken ct)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task CreateDirectoryAsync(string path, CancellationToken ct)
    {
        Directory.CreateDirectory(path);
        return Task.CompletedTask;
    }
}