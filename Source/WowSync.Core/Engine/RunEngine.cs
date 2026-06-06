// ReSharper disable StringLiteralTypo
namespace WowSync.Core.Engine;

using System.Globalization;
using WowSync.Core.Backup;
using WowSync.Core.Config;
using WowSync.Core.Discovery;
using WowSync.Core.IO;
using WowSync.Core.Runs;
using WowSync.Core.Validation;
using WowSync.Plugins.Abstractions.Operations;
using WowSync.Plugins.Abstractions.Runs;

public sealed class RunEngine
{
    private readonly WowDiscoveryService discoveryService = new();
    private readonly ValidationService validationService = new();
    private readonly BackupService backupService = new();
    private readonly PhysicalFileSystem fileSystem = new();

    /// <summary>
    /// Executes the plan in dry-run mode: operations run, logging is real, but filesystem writes/copies/deletes are blocked.
    /// This is the method we want the UI to call for "dry run output" in the textbox.
    /// </summary>
    public async Task<ApplyResult> DryRunExecuteAsync(AppConfig config, OperationPlan plan)
    {
        // For dry-run we still want discovery to work, and we want obvious validation failures to show early.
        var discovery = this.discoveryService.Discover(config);
        if (discovery.Context is null)
        {
            throw new InvalidOperationException("Discovery failed. Fix validation errors first.");
        }

        var runId = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture);
        var logger = new InMemoryRunLogger();

        logger.Info($"RunId: {runId}");
        logger.Info("Mode: DRY-RUN");
        logger.Info($"Plan: {plan.Title}");
        logger.Info($"Operations: {plan.Operations.Count}");
        logger.Info("");

        // Use a filesystem wrapper that blocks all mutations (and tracks what would have happened)
        var dryFs = new DryRunFileSystem(this.fileSystem, logger);
        var ctx = new OperationContext(dryFs, logger, isDryRun: true);

        // "Targets" = declared potential touch paths from op.TouchedPaths
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var applied = new List<string>();

        var startedAt = DateTimeOffset.Now;

        for (var opIndex = 0; opIndex < plan.Operations.Count; opIndex++)
        {
            var op = plan.Operations[opIndex];

            applied.Add(op.Description);

            logger.Info(string.Create(CultureInfo.InvariantCulture, $"== Operation {opIndex + 1}/{plan.Operations.Count} =="));
            logger.Info(op.Description);

            var opTargets = op.TouchedPaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (opTargets.Count == 0)
            {
                logger.Info("Targets: (none)");
            }
            else
            {
                logger.Info($"Targets: {opTargets.Count}");
                foreach (var p in opTargets.Order(StringComparer.OrdinalIgnoreCase))
                {
                    logger.Info($"  - {p}");
                    targets.Add(p);
                }
            }

            var opStarted = DateTimeOffset.Now;

            try
            {
                await op.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(false);
                var elapsed = DateTimeOffset.Now - opStarted;
                logger.Info(string.Create(CultureInfo.InvariantCulture, $"Result: OK ({elapsed.TotalMilliseconds:n0} ms)"));
            }
            catch (Exception ex)
            {
                var elapsed = DateTimeOffset.Now - opStarted;
                logger.Info(string.Create(CultureInfo.InvariantCulture, $"Result: FAILED ({elapsed.TotalMilliseconds:n0} ms)"));
                logger.Info(ex.ToString());
                throw;
            }

            logger.Info("");
        }

        var finishedAt = DateTimeOffset.Now;

        logger.Info("== Summary ==");
        logger.Info($"Targets paths: {targets.Count}");
        foreach (var p in targets.Order(StringComparer.OrdinalIgnoreCase))
        {
            logger.Info($"  - {p}");
        }

        logger.Info("");
        logger.Info("== Would mutate summary ==");
        logger.Info($"Would write files: {dryFs.WouldWriteFiles.Count}");
        logger.Info($"Would copy destinations: {dryFs.WouldCopyDestinations.Count}");
        logger.Info($"Would delete files: {dryFs.WouldDeleteFiles.Count}");
        logger.Info($"Would create directories: {dryFs.WouldCreateDirectories.Count}");

        var wouldMutateFiles = dryFs.AllWouldMutateFiles;
        logger.Info($"Would mutate unique files: {wouldMutateFiles.Count}");
        foreach (var p in wouldMutateFiles.Order(StringComparer.OrdinalIgnoreCase))
        {
            logger.Info($"  - {p}");
        }

        // Note: BackupPath is meaningless in dry-run; keep it explicit.
        // We keep RunResult.TouchedPaths populated with "targets" for now (declared intent),
        // and the "would mutate" list is printed separately above.
        var result = new RunResult(
            RunId: runId,
            StartedAt: startedAt,
            FinishedAt: finishedAt,
            Succeeded: true,
            BackupPath: "(dry-run)",
            TouchedPaths: [.. targets],
            AppliedOperationDescriptions: applied,
            Context: discovery.Context);

        return new ApplyResult(result, logger.Lines);
    }

    public async Task<ApplyResult> ApplyAsync(AppConfig config, OperationPlan plan)
    {
        var applyValidation = this.validationService.ValidateForApply(config);
        if (!applyValidation.IsOk)
        {
            throw new InvalidOperationException("Validation failed for apply. Fix issues first.");
        }

        var discovery = this.discoveryService.Discover(config);
        if (discovery.Context is null)
        {
            throw new InvalidOperationException("Discovery failed. Fix validation errors first.");
        }

        var runId = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture);
        var logger = new InMemoryRunLogger();

        logger.Info($"RunId: {runId}");
        logger.Info($"Plan: {plan.Title}");
        logger.Info($"Operations: {plan.Operations.Count}");
        logger.Info("");

        // Determine touched paths up-front from the plan.
        // This lets the backup be "only what we might change", while still being "before execute".
        var touchedFromPlan = plan.Operations
            .SelectMany(o => o.TouchedPaths)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        logger.Info($"Touched paths (from plan): {touchedFromPlan.Count}");

        // Backup only touched paths (and optionally .lua.bak companions depending on config)
        var backupPath = this.backupService.CreateBackup(config, discovery.Context, runId, touchedFromPlan);
        logger.Info($"Backup created: {backupPath}");
        logger.Info("");

        // Execute using tracking FS so we can return ACTUAL mutations
        var trackingFs = new TrackingFileSystem(this.fileSystem);
        var ctx = new OperationContext(trackingFs, logger, isDryRun: false);

        var applied = new List<string>();
        var startedAt = DateTimeOffset.Now;

        foreach (var op in plan.Operations)
        {
            applied.Add(op.Description);
            await op.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(false);
        }

        var finishedAt = DateTimeOffset.Now;

        var result = new RunResult(
            RunId: runId,
            StartedAt: startedAt,
            FinishedAt: finishedAt,
            Succeeded: true,
            BackupPath: backupPath,
            TouchedPaths: [.. trackingFs.MutatedFiles],
            AppliedOperationDescriptions: applied,
            Context: discovery.Context);

        return new ApplyResult(result, logger.Lines);
    }

    public UndoResult UndoLastApply(AppConfig config, RunResult lastApplyRun)
    {
        ArgumentNullException.ThrowIfNull(lastApplyRun);

        if (string.IsNullOrWhiteSpace(lastApplyRun.BackupPath) || string.Equals(lastApplyRun.BackupPath, "(dry-run)", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cannot undo: last run has no valid backup path.");
        }

        var logger = new InMemoryRunLogger();

        logger.Info("Mode: UNDO");
        logger.Info($"Backup: {lastApplyRun.BackupPath}");
        logger.Info($"Touched: {lastApplyRun.TouchedPaths.Count}");
        logger.Info("");

        if (!Directory.Exists(lastApplyRun.BackupPath))
        {
            throw new DirectoryNotFoundException($"Backup folder not found: {lastApplyRun.BackupPath}");
        }

        var backupAccountRoot = Path.Combine(lastApplyRun.BackupPath, "Account");
        if (!Directory.Exists(backupAccountRoot))
        {
            throw new DirectoryNotFoundException($"Backup does not contain 'Account' folder: {backupAccountRoot}");
        }

        var discovery = this.discoveryService.Discover(config);
        if (discovery.Context is null)
        {
            throw new InvalidOperationException("Discovery failed. Fix validation errors first.");
        }

        var touched = lastApplyRun.TouchedPaths.ToList();
        if (touched.Count == 0)
        {
            logger.Info("Nothing to undo: touched list is empty.");
            return new UndoResult(lastApplyRun.BackupPath, 0, 0, 0, logger.Lines);
        }

        var restored = 0;
        var deleted = 0;
        var skipped = 0;

        foreach (var destPath in touched)
        {
            if (string.IsNullOrWhiteSpace(destPath))
            {
                skipped++;
                continue;
            }

            // Find which discovered account this touched path belongs to
            var owningAccount = discovery.Context.Accounts
                .FirstOrDefault(a => IsUnderDirectory(destPath, a.AccountPath));

            if (owningAccount is null)
            {
                logger.Warn($"Skip (unknown account): {destPath}");
                skipped++;
                continue;
            }

            var relative = Path.GetRelativePath(owningAccount.AccountPath, destPath);
            var backupSource = Path.Combine(backupAccountRoot, owningAccount.AccountName, relative);

            if (File.Exists(backupSource))
            {
                // Restore from backup
                var dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.Copy(backupSource, destPath, overwrite: true);

                logger.Info($"Restored: {destPath}");
                restored++;
                continue;
            }

            // Backup did not contain the file.
            // That usually means the file was CREATED during apply.
            if (File.Exists(destPath))
            {
                File.Delete(destPath);
                logger.Info($"Deleted (did not exist in backup): {destPath}");
                deleted++;
            }
            else
            {
                logger.Info($"No-op (missing in backup and already missing): {destPath}");
                skipped++;
            }
        }

        logger.Info("");
        logger.Info(string.Create(CultureInfo.InvariantCulture, $"Undo summary: restored={restored}, deleted={deleted}, skipped={skipped}"));

        return new UndoResult(lastApplyRun.BackupPath, restored, deleted, skipped, logger.Lines);
    }

    private static bool IsUnderDirectory(string fullPath, string directory)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var dir = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        var file = Path.GetFullPath(fullPath);
        return file.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------
    // Dry-run filesystem wrapper
    // ---------------------------------

    private sealed class DryRunFileSystem(IFileSystem inner, IRunLogger logger) : IFileSystem
    {
        private readonly HashSet<string> wouldWriteFiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> wouldCopyDestinations = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> wouldDeleteFiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> wouldCreateDirectories = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<string> WouldWriteFiles => this.wouldWriteFiles;
        public IReadOnlyCollection<string> WouldCopyDestinations => this.wouldCopyDestinations;
        public IReadOnlyCollection<string> WouldDeleteFiles => this.wouldDeleteFiles;
        public IReadOnlyCollection<string> WouldCreateDirectories => this.wouldCreateDirectories;

        public IReadOnlyCollection<string> AllWouldMutateFiles
        {
            get
            {
                // Keep this allocation small and only used for summary printing.
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.UnionWith(this.wouldWriteFiles);
                set.UnionWith(this.wouldCopyDestinations);
                set.UnionWith(this.wouldDeleteFiles);
                return set;
            }
        }

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

        public Task WriteAllTextAtomicAsync(string path, string content, CancellationToken ct)
        {
            this.wouldWriteFiles.Add(path);
            logger.Info(string.Create(CultureInfo.InvariantCulture, $"DRYRUN: would write file: {path} ({content.Length:n0} chars)"));
            return Task.CompletedTask;
        }

        public Task CopyFileAsync(string source, string destination, bool overwrite, CancellationToken ct)
        {
            this.wouldCopyDestinations.Add(destination);
            logger.Info($"DRYRUN: would copy file: {source} -> {destination} (overwrite={overwrite})");
            return Task.CompletedTask;
        }

        public Task DeleteFileAsync(string path, CancellationToken ct)
        {
            this.wouldDeleteFiles.Add(path);
            logger.Info($"DRYRUN: would delete file: {path}");
            return Task.CompletedTask;
        }

        public Task CreateDirectoryAsync(string path, CancellationToken ct)
        {
            this.wouldCreateDirectories.Add(path);
            logger.Info($"DRYRUN: would create directory: {path}");
            return Task.CompletedTask;
        }
    }
}
