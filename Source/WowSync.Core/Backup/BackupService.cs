// ReSharper disable CommentTypo
namespace WowSync.Core.Backup;

using WowSync.Core.Config;
using WowSync.Plugins.Abstractions.Operations;
using WowSync.Plugins.Abstractions.Runs;

public sealed class BackupService
{
    private const string LastBackupPointerFileName = "_last_backup.txt";
    private const string TouchedPathsManifestFileName = "_touched_paths.txt";

    public string CreateBackup(
        AppConfig config,
        WowContextSnapshot context,
        string runId,
        IReadOnlyList<string> touchedPaths)
    {
        if (string.IsNullOrWhiteSpace(config.BackupRoot))
        {
            throw new InvalidOperationException("BackupRoot is not configured.");
        }

        var backupPath = Path.Combine(config.BackupRoot, runId);
        Directory.CreateDirectory(backupPath);

        // Copy each touched file into the backup folder (mirroring under Account\{AccountName}\...)
        foreach (var p in touchedPaths)
        {
            if (string.IsNullOrWhiteSpace(p))
            {
                continue;
            }

            if (!TryMapTouchedPathToAccountRelative(context, p, out var accountName, out var relative))
            {
                continue;
            }

            var src = p;

            if (!File.Exists(src))
            {
                // Missing is fine (some ops might create new files, etc.)
                continue;
            }

            var dst = Path.Combine(backupPath, "Account", accountName, relative);

            var dstDir = Path.GetDirectoryName(dst);
            if (!string.IsNullOrWhiteSpace(dstDir))
            {
                Directory.CreateDirectory(dstDir);
            }

            File.Copy(src, dst, overwrite: true);
        }

        // Write manifests/pointers so Undo can work without UI holding state.
        WriteTouchedPathsManifest(backupPath, touchedPaths);
        WriteLastBackupPointer(config.BackupRoot, backupPath);

        return backupPath;
    }


    public IReadOnlyList<string> ReadTouchedPathsFromBackup(string backupPath)
    {
        var manifest = Path.Combine(backupPath, TouchedPathsManifestFileName);
        if (!File.Exists(manifest))
        {
            return [];
        }

        var lines = File.ReadAllLines(manifest)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return lines;
    }

    public bool TryGetLatestBackupPath(AppConfig config, out string backupPath)
    {
        backupPath = string.Empty;

        var root = config.BackupRoot;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return false;
        }

        var dirs = Directory.GetDirectories(root);
        if (dirs.Length == 0)
        {
            return false;
        }

        // Backup folder names are runId-like: yyyyMMdd_HHmmss_fff
        // Sorting by folder name descending is a decent "latest" signal.
        var latest = dirs
            .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(latest))
        {
            return false;
        }

        backupPath = latest;
        return true;
    }

    public int RestoreBackup(WowContextSnapshot context, string backupPath, IRunLogger logger)
    {
        var accountRoot = Path.Combine(backupPath, "Account");
        if (!Directory.Exists(accountRoot))
        {
            throw new DirectoryNotFoundException($"Backup does not contain 'Account' folder: {accountRoot}");
        }

        var restoredFiles = 0;

        foreach (var accountDir in Directory.GetDirectories(accountRoot))
        {
            var accountName = Path.GetFileName(accountDir);

            var account = context.Accounts.FirstOrDefault(a =>
                string.Equals(a.AccountName, accountName, StringComparison.OrdinalIgnoreCase));

            if (account is null)
            {
                logger.Warn($"Backup contains account '{accountName}', but it was not found in current discovery. Skipping.");
                continue;
            }

            var files = Directory.GetFiles(accountDir, "*", SearchOption.AllDirectories);
            logger.Info($"Restoring account: {accountName} ({files.Length} files)");

            foreach (var src in files)
            {
                var relative = Path.GetRelativePath(accountDir, src);
                var dst = Path.Combine(account.AccountPath, relative);

                var dstDir = Path.GetDirectoryName(dst);
                if (!string.IsNullOrWhiteSpace(dstDir))
                {
                    Directory.CreateDirectory(dstDir);
                }

                File.Copy(src, dst, overwrite: true);
                restoredFiles++;

                logger.Info($"Restored: {dst}");
            }

            logger.Info("");
        }

        return restoredFiles;
    }

    private static void WriteLastBackupPointer(string backupRoot, string backupPath)
    {
        Directory.CreateDirectory(backupRoot);
        var ptr = Path.Combine(backupRoot, LastBackupPointerFileName);
        File.WriteAllText(ptr, backupPath);
    }

    private static void WriteTouchedPathsManifest(string backupPath, IReadOnlyList<string> touchedPaths)
    {
        var manifest = Path.Combine(backupPath, TouchedPathsManifestFileName);
        File.WriteAllLines(
            manifest,
            touchedPaths
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static bool TryMapTouchedPathToAccountRelative(
        WowContextSnapshot context,
        string absolutePath,
        out string accountName,
        out string relativePath)
    {
        accountName = string.Empty;
        relativePath = string.Empty;

        foreach (var acc in context.Accounts)
        {
            if (IsUnderDirectory(absolutePath, acc.AccountPath, out var rel))
            {
                accountName = acc.AccountName;
                relativePath = rel;
                return true;
            }
        }

        return false;
    }

    private static bool IsUnderDirectory(string path, string rootDir, out string relative)
    {
        relative = string.Empty;

        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(rootDir))
        {
            return false;
        }

        var root = Path.GetFullPath(rootDir)
                       .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;

        var full = Path.GetFullPath(path);

        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        relative = full[root.Length..];
        return true;
    }
}
