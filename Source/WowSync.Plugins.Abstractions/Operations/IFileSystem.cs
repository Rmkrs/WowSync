namespace WowSync.Plugins.Abstractions.Operations;

public interface IFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    Task<string> ReadAllTextAsync(string path, CancellationToken ct);

    Task WriteAllTextAtomicAsync(string path, string content, CancellationToken ct);

    Task CopyFileAsync(string source, string destination, bool overwrite, CancellationToken ct);

    Task DeleteFileAsync(string path, CancellationToken ct);

    Task CreateDirectoryAsync(string path, CancellationToken ct);
}
