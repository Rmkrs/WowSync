namespace WowSync.Core.IO;

using WowSync.Plugins.Abstractions.Operations;

public sealed class TrackingFileSystem(IFileSystem inner) : IFileSystem
{
    private readonly HashSet<string> mutatedFiles = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> MutatedFiles => this.mutatedFiles;

    public bool FileExists(string path)
    {
        return inner.FileExists(path);
    }

    public bool DirectoryExists(string path)
    {
        return inner.DirectoryExists(path);
    }

    public Task<string> ReadAllTextAsync(string path, CancellationToken ct)
    {
        return inner.ReadAllTextAsync(path, ct);
    }

    public async Task WriteAllTextAtomicAsync(string path, string content, CancellationToken ct)
    {
        await inner.WriteAllTextAtomicAsync(path, content, ct).ConfigureAwait(false);
        this.mutatedFiles.Add(path);
    }

    public async Task CopyFileAsync(string source, string destination, bool overwrite, CancellationToken ct)
    {
        await inner.CopyFileAsync(source, destination, overwrite, ct).ConfigureAwait(false);
        this.mutatedFiles.Add(destination);
    }

    public async Task DeleteFileAsync(string path, CancellationToken ct)
    {
        await inner.DeleteFileAsync(path, ct).ConfigureAwait(false);
        this.mutatedFiles.Add(path);
    }

    public Task CreateDirectoryAsync(string path, CancellationToken ct)
    {
        return inner.CreateDirectoryAsync(path, ct);
    }
}