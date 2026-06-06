// ReSharper disable StringLiteralTypo
namespace WowSync.Core.Validation;

using WowSync.Core.Config;
using WowSync.Core.Discovery;

public sealed class ValidationService
{
    public ValidationResult ValidateForApply(AppConfig config)
    {
        var messages = new List<ValidationMessage>();

        // Must be discoverable first
        var discovery = new WowDiscoveryService().Discover(config);
        messages.AddRange(discovery.Validation.Messages);

        // If Wow is running, block apply
        if (IsWowRunning())
        {
            messages.Add(new ValidationMessage(
                ValidationSeverity.Error,
                "WOW_RUNNING",
                "World of Warcraft appears to be running.",
                "Close Wow before applying changes."));
        }

        // Backup folder must exist/creatable and writable
        if (!string.IsNullOrWhiteSpace(config.BackupRoot))
        {
            try
            {
                var backupRoot = Path.GetFullPath(config.BackupRoot);
                Directory.CreateDirectory(backupRoot);

                var probe = Path.Combine(backupRoot, ".wowsync_write_probe.tmp");
                File.WriteAllText(probe, "probe");
                File.Delete(probe);

                // Warn if backup inside Wow tree
                if (!string.IsNullOrWhiteSpace(config.WowRoot))
                {
                    var wowRoot = Path.GetFullPath(config.WowRoot);
                    if (backupRoot.StartsWith(wowRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        messages.Add(new ValidationMessage(
                            ValidationSeverity.Warning,
                            "BACKUP_INSIDE_WOW",
                            "Backup folder is inside the Wow install folder.",
                            "Prefer a backup folder outside the Wow tree."));
                    }
                }
            }
            catch (Exception ex)
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    "BACKUP_NOT_WRITABLE",
                    $"Backup folder is not writable: {config.BackupRoot}. {ex.Message}",
                    "Pick a different backup folder with write access."));
            }
        }

        return ValidationResult.From(messages);
    }

    private static bool IsWowRunning()
    {
        try
        {
            // Retail + Classic variants exist; check broadly.
            var names = new[] { "Wow", "World of Warcraft", "WowClassic", "WowClassicT" };
            foreach (var n in names)
            {
                if (System.Diagnostics.Process.GetProcessesByName(n).Length > 0)
                {
                    return true;
                }
            }
        }
        catch
        {
            // If process enumeration fails, don't block.
        }

        return false;
    }
}
