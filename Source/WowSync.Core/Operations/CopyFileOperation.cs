// ReSharper disable MemberCanBePrivate.Global
namespace WowSync.Core.Operations;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WowSync.Plugins.Abstractions.Operations;

public sealed class CopyFileOperation(string sourcePath, string destinationPath, bool overwrite = true) : IOperation
{
    public string SourcePath { get; } = sourcePath;

    public string DestinationPath { get; } = destinationPath;

    public bool Overwrite { get; } = overwrite;

    public string Description { get; } = $"Copy: {sourcePath} -> {destinationPath}";

    public IReadOnlyList<string> TouchedPaths { get; } = [destinationPath];

    public async Task ExecuteAsync(IOperationContext context, CancellationToken ct)
    {
        if (!context.FileSystem.FileExists(this.SourcePath))
        {
            context.Logger.Warn($"Source missing, skipping: {this.SourcePath}");
            return;
        }

        // If destination exists, avoid copying when there's no effective change.
        if (context.FileSystem.FileExists(this.DestinationPath))
        {
            if (!this.Overwrite)
            {
                context.Logger.Info($"Destination exists and overwrite=False, skipping: {this.DestinationPath}");
                return;
            }

            // SavedVariables are text; compare content to avoid pointless copy + backup noise.
            var src = await context.FileSystem.ReadAllTextAsync(this.SourcePath, ct).ConfigureAwait(false);
            var dst = await context.FileSystem.ReadAllTextAsync(this.DestinationPath, ct).ConfigureAwait(false);

            if (string.Equals(src, dst, StringComparison.Ordinal))
            {
                // Keep this quiet: it’s the whole point of de-noising.
                return;
            }
        }

        await context.FileSystem.CopyFileAsync(this.SourcePath, this.DestinationPath, this.Overwrite, ct).ConfigureAwait(false);

        context.Logger.Info(
            context.IsDryRun
                ? $"Would copy to: {this.DestinationPath}"
                : $"Copied to: {this.DestinationPath}");
    }
}
