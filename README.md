# WowSync

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D4)](https://www.microsoft.com/windows)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> [!WARNING]
> WowSync modifies World of Warcraft SavedVariables files. Always close World of Warcraft before applying changes and keep backups of your `WTF` folder.

> [!NOTE]
> WowSync is local-first. It does not require a server, cloud account, subscription, or login.

WowSync is a local Windows desktop tool for synchronizing and merging World of Warcraft SavedVariables across multiple WoW accounts.

It is built for players who use several WoW accounts and want one "main" account to act as the source of truth for selected addon settings, profile data, and account-wide SavedVariables.

The goal is not blind file spam. WowSync tries to be careful: it discovers your WoW accounts, lets you choose what is included, supports dry runs, creates backups before applying changes, and can undo the last apply run.

## What it does

WowSync can:

- Discover WoW accounts, realms, and characters from your `WTF\Account` folder.
- Let you choose a main account.
- Let you choose which accounts participate in syncing.
- Let you select a main character for character-aware sync operations.
- Copy selected account-wide SavedVariables files from the main account to other included accounts.
- Apply profile-based Lua patches to selected SavedVariables paths.
- Inspect Lua SavedVariables files and create reusable sync profiles.
- Run built-in addon-specific merge operations.
- Perform dry runs before touching files.
- Create backups before applying changes.
- Undo the last apply run from backup.

## Current built-in plugins

### Altoholic DataStore merge

Merges Altoholic DataStore character branches from included alt accounts into the main account.

The merge is strict around character identity. If a character GUID/name mismatch is detected, the operation aborts instead of guessing.

Currently supported DataStore modules include:

- `DataStore.lua`
- `DataStore_Inventory.lua`
- `DataStore_Containers.lua`
- `DataStore_Achievements.lua`
- `DataStore_Quests.lua`
- `DataStore_Reputations.lua`
- `DataStore_Stats.lua`
- `DataStore_Auctions.lua`
- `DataStore_Crafts.lua`
- `DataStore_Currencies.lua`
- `DataStore_Agenda.lua`
- `DataStore_Mails.lua`
- `DataStore_Pets.lua`
- `DataStore_Spells.lua`
- `DataStore_Talents.lua`
- `DataStore_Garrisons.lua`
- `DataStore_Characters.lua`

### Accountant merge and distribute

Merges character branches from included alt accounts into the main account's `Accountant.lua`, then distributes the merged result back to included accounts.

Each target account receives only the character branches that already exist on that target.

## Lua profiles

WowSync includes a Lua inspector that can open SavedVariables `.lua` files and display their table structure as a tree.

From the inspector, you can select Lua paths and save them into reusable profiles. These profiles can later be applied during a sync run.

Profile patch modes:

- `UpdateOnly`: only update existing leaf keys. Missing keys are skipped.
- `AddIfParentExists`: allow adding missing leaf keys only when the parent branch already exists.

This makes profile syncing more controlled than copying an entire SavedVariables file.

## Sync scopes

WowSync supports multiple sync scopes:

- Main account to sub accounts.
- Main account to other characters on the main account.
- Main account to all included accounts and characters.

The exact behavior depends on the selected files, profiles, plugins, and scope.

## Safety features

WowSync is designed to avoid turning your `WTF` folder into confetti.

Safety features include:

- Dry-run mode.
- Apply mode with backup creation.
- Undo last apply.
- Validation before apply.
- Detection of running WoW processes.
- Lua parsing before modification.
- Lua round-trip validation before writing generated Lua.
- Atomic file writes where possible.
- Change tracking for touched files.

You should still keep your own backups, especially while the project is young.

## Important warning

Close World of Warcraft before applying changes.

WoW writes SavedVariables when the game exits. If you edit SavedVariables while WoW is running, WoW may overwrite your changes or leave files in an unexpected state.

WowSync validates for running WoW processes and blocks apply when WoW appears to be active.

## Configuration

WowSync stores its configuration under the user profile application data folder:

```text
%APPDATA%\WowSync\config.json
```

Lua sync profiles are stored under:

```text
%APPDATA%\WowSync\profiles
```

## Backups

Before applying changes, WowSync creates a backup of touched files under the configured backup folder.

The backup includes:

- touched file manifest
- latest backup pointer
- mirrored account-relative backup files

Undo uses the last apply run information and backup folder to restore or delete files as needed.

## Project structure

```text
Source/
  WowSync.App/
    WPF desktop application
    Main window
    Lua inspector
    View models and UI rows

  WowSync.Core/
    Configuration
    Discovery
    Backup
    Run engine
    Lua parser/writer/navigation
    Profile patching
    Validation
    File system abstractions

  WowSync.Plugins.Abstractions/
    Plugin contracts
    Operation contracts
    Run context models

  WowSync.Plugins.BuiltIn/
    Built-in addon-specific sync and merge plugins
```

## How it works

At a high level:

1. WowSync discovers accounts, realms, and characters under the configured WoW root.
2. You choose a main account and included accounts.
3. You configure account-wide SavedVariables files, Lua profiles, and plugins.
4. You run a dry run to see what would happen.
5. You apply the run.
6. WowSync creates a backup, executes the operation plan, and tracks touched files.
7. If needed, you can undo the last apply.

## Requirements

- Windows
- World of Warcraft installed locally
- .NET desktop runtime compatible with the application build
- WoW must be closed before applying changes

## Status

This project is currently early and personal-tool grade.

It works for the author's use cases, but addon SavedVariables can vary wildly between versions and setups. Treat new plugins and profiles carefully. Always dry-run first. Always keep backups.

## Development philosophy

WowSync is intentionally local-first.

It does not need a server, cloud account, subscription, or login. It operates on your local WoW files and keeps the control on your machine.

The design favors explicit operations, dry-run visibility, and reversible changes over "magic sync."

## Planned / possible future ideas

Possible future improvements:

- More built-in addon plugins.
- Better profile editing.
- More detailed diff output.
- Import/export of profiles.
- Improved validation for specific addon formats.
- Plugin loading from external assemblies.
- Better setup documentation and screenshots.

## Disclaimer

WowSync is an unofficial tool and is not affiliated with Blizzard Entertainment.

World of Warcraft and related names are trademarks of Blizzard Entertainment, Inc.

Use at your own risk. Back up your `WTF` folder before experimenting.
