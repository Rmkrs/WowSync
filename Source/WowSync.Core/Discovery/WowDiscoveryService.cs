// ReSharper disable StringLiteralTypo
namespace WowSync.Core.Discovery;

using WowSync.Core.Config;
using WowSync.Core.Validation;
using WowSync.Plugins.Abstractions.Runs;

public sealed class WowDiscoveryService
{
    public WowDiscoveryResult Discover(AppConfig config)
    {
        var messages = new List<ValidationMessage>();

        if (string.IsNullOrWhiteSpace(config.WowRoot))
        {
            messages.Add(new ValidationMessage(
                ValidationSeverity.Error,
                "WOWROOT_MISSING",
                "Wow root folder is not configured.",
                "Pick Wow.exe (or the Wow install folder) in the UI."));
            return new WowDiscoveryResult(Context: null, ValidationResult.From(messages));
        }

        var wowRoot = Path.GetFullPath(config.WowRoot);

        if (!Directory.Exists(wowRoot))
        {
            messages.Add(new ValidationMessage(
                ValidationSeverity.Error,
                "WOWROOT_NOT_FOUND",
                $"Wow root folder does not exist: {wowRoot}",
                "Update the Wow path in settings."));
            return new WowDiscoveryResult(Context: null, ValidationResult.From(messages));
        }

        var accountRoot = WowPaths.GetAccountRoot(wowRoot);
        if (!Directory.Exists(accountRoot))
        {
            messages.Add(new ValidationMessage(
                ValidationSeverity.Error,
                "ACCOUNT_ROOT_MISSING",
                $"Expected folder not found: {accountRoot}",
                "Make sure you selected the correct Wow install root (the folder containing Wow.exe)."));
            return new WowDiscoveryResult(Context: null, ValidationResult.From(messages));
        }

        var accounts = new List<AccountSnapshot>();
        foreach (var accountDir in Directory.EnumerateDirectories(accountRoot))
        {
            var accountName = Path.GetFileName(accountDir);
            if (string.IsNullOrWhiteSpace(accountName))
            {
                continue;
            }

            var realms = new List<RealmSnapshot>();

            // Realms are directories directly under Account\<AccountName>
            foreach (var realmDir in Directory.EnumerateDirectories(accountDir))
            {
                var realmName = Path.GetFileName(realmDir);
                if (string.IsNullOrWhiteSpace(realmName))
                {
                    continue;
                }

                // Skip SavedVariables folder, it's not a realm
                if (realmName.Equals("SavedVariables", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var chars = new List<CharacterSnapshot>();
                foreach (var charDir in Directory.EnumerateDirectories(realmDir))
                {
                    var charName = Path.GetFileName(charDir);
                    if (string.IsNullOrWhiteSpace(charName))
                    {
                        continue;
                    }

                    chars.Add(new CharacterSnapshot(
                        AccountName: accountName,
                        RealmName: realmName,
                        CharacterName: charName,
                        CharacterPath: charDir));
                }

                realms.Add(new RealmSnapshot(realmName, chars));
            }

            accounts.Add(new AccountSnapshot(accountName, accountDir, realms));
        }

        // Validate configured accounts exist
        string? mainAccountName = null;
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(config.MainAccountName))
        {
            if (accounts.Exists(a => a.AccountName.Equals(config.MainAccountName, StringComparison.OrdinalIgnoreCase)))
            {
                mainAccountName = config.MainAccountName;
            }
            else
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    "MAIN_ACCOUNT_MISSING",
                    $"Configured main account was not found: {config.MainAccountName}",
                    "Pick an existing account as main."));
            }
        }

        foreach (var name in config.IncludedAccountNames)
        {
            if (accounts.Exists(a => a.AccountName.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                included.Add(name);
            }
            else
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Warning,
                    "INCLUDED_ACCOUNT_MISSING",
                    $"Configured included account was not found: {name}",
                    "Rescan accounts and re-select which accounts to include."));
            }
        }

        // If user hasn't chosen included accounts yet, we treat it as "all included" in UI,
        // but validation should still require a choice before apply.
        if (config.IncludedAccountNames.Count == 0)
        {
            messages.Add(new ValidationMessage(
                ValidationSeverity.Error,
                "INCLUDED_NOT_SET",
                "No included accounts are configured.",
                "Select which accounts to include and save the config."));
        }

        if (string.IsNullOrWhiteSpace(config.MainAccountName))
        {
            messages.Add(new ValidationMessage(
                ValidationSeverity.Error,
                "MAIN_NOT_SET",
                "No main account is configured.",
                "Select a main account and save the config."));
        }
        else if (config.IncludedAccountNames.Count > 0 &&
                 !config.IncludedAccountNames.Contains(config.MainAccountName))
        {
            messages.Add(new ValidationMessage(
                ValidationSeverity.Error,
                "MAIN_NOT_INCLUDED",
                "Main account must be included.",
                "Mark the main account as included and save the config."));
        }

        // Resolve main character if configured
        CharacterSnapshot? mainChar = null;
        if (!string.IsNullOrWhiteSpace(mainAccountName) &&
            !string.IsNullOrWhiteSpace(config.MainRealmName) &&
            !string.IsNullOrWhiteSpace(config.MainCharacterName))
        {
            mainChar = accounts
                .Where(a => a.AccountName.Equals(mainAccountName, StringComparison.OrdinalIgnoreCase))
                .SelectMany(a => a.Realms)
                .Where(r => r.RealmName.Equals(config.MainRealmName, StringComparison.OrdinalIgnoreCase))
                .SelectMany(r => r.Characters)
                .FirstOrDefault(c => c.CharacterName.Equals(config.MainCharacterName, StringComparison.OrdinalIgnoreCase));

            if (mainChar is null)
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    "MAIN_CHARACTER_MISSING",
                    $"Configured main character was not found: {config.MainRealmName}\\{config.MainCharacterName}",
                    "Pick an existing main character."));
            }
        }

        // Backup folder checks (basic here; we’ll do write-test in ValidationService below)
        if (string.IsNullOrWhiteSpace(config.BackupRoot))
        {
            messages.Add(new ValidationMessage(
                ValidationSeverity.Error,
                "BACKUPROOT_MISSING",
                "Backup folder is not configured.",
                "Choose a backup folder in the UI before applying any changes."));
        }

        var context = new WowContextSnapshot(
            WowRoot: wowRoot,
            Accounts: accounts,
            MainAccountName: mainAccountName,
            IncludedAccountNames: [.. included],
            MainCharacter: mainChar);

        return new WowDiscoveryResult(context, ValidationResult.From(messages));
    }
}
