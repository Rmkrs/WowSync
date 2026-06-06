// ReSharper disable AsyncVoidEventHandlerMethod
// ReSharper disable IdentifierTypo
// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo
// ReSharper disable CollectionNeverQueried.Global
// ReSharper disable NotAccessedPositionalProperty.Global
// ReSharper disable UnusedMember.Global
namespace WowSync.App;

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using WowSync.App.LuaInspector;
using WowSync.Core.Config;
using WowSync.Core.Discovery;
using WowSync.Core.Engine;
using WowSync.Core.Profiles;
using WowSync.Core.Validation;
using WowSync.Plugins.Abstractions.Operations;
using WowSync.Plugins.Abstractions.Runs;
using WowSync.Plugins.BuiltIn;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfWindowState = System.Windows.WindowState;

public partial class MainWindow
{
    private readonly IConfigStore configStore = new JsonConfigStore();
    private readonly WowDiscoveryService discoveryService = new();
    private readonly ValidationService validationService = new();
    private readonly RunEngine runEngine = new();
    private readonly ProfileStore profileStore = new();

    private AppConfig config = new();
    private bool isUiInitialized;
    private bool suppressAutoSave;
    private RunResult? lastApplyRun;
    private bool mainToonChangeIsUserInitiated;
    private WowContextSnapshot? lastContext;

    public MainWindow()
    {
        this.InitializeComponent();
        this.DataContext = new MainVm();

        this.Vm.MainAccountChanged += (_, _) =>
        {
            this.suppressAutoSave = true;
            try
            {
                this.Vm.SelectedMainCharacter = null;
                this.RebuildMainCharacterOptions();
                this.RefreshProfiles();
                this.RefreshPlugins();
                this.RefreshAccountSvExistsFlags();
            }
            finally
            {
                this.suppressAutoSave = false;
            }
        };

        this.Loaded += async (_, _) => await this.LoadAndScanAsync();

        this.Closing += async (_, _) =>
        {
            this.config = this.config with
            {
                MainWindowPlacement = TryCapturePlacement(this) ?? this.config.MainWindowPlacement,
                LastSelectedTabIndex = this.Vm.SelectedTabIndex,
            };

            await this.configStore.SaveAsync(this.config, CancellationToken.None);
        };
    }

    private MainVm Vm => (MainVm)this.DataContext;

    private static bool IsFromInteractiveElement(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is System.Windows.Controls.Button)
            {
                return true;
            }

            if (d is System.Windows.Controls.Primitives.ToggleButton)
            {
                return true; // includes CheckBox/RadioButton
            }

            if (d is System.Windows.Controls.ComboBox)
            {
                return true;
            }

            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }

        return false;
    }

    private static WpfWindowState ToWpfState(AppWindowState state)
    {
        return state switch
        {
            AppWindowState.Maximized => WpfWindowState.Maximized,
            AppWindowState.Minimized => WpfWindowState.Minimized,
            _ => WpfWindowState.Normal,
        };
    }

    private static WindowPlacement? TryCapturePlacement(Window w)
    {
        // When maximized/minimized, use RestoreBounds (the “normal” rectangle)
        var rb = w.RestoreBounds;

        var state = w.WindowState switch
        {
            System.Windows.WindowState.Maximized => AppWindowState.Maximized,
            System.Windows.WindowState.Minimized => AppWindowState.Minimized,
            _ => AppWindowState.Normal,
        };

        // Use restore bounds for sizing/positioning data
        var left = rb.Left;
        var top = rb.Top;
        var width = rb.Width;
        var height = rb.Height;

        // Sanity checks (avoid JSON NaN/Infinity + obviously broken sizes)
        if (IsBad(left) || IsBad(top) || IsBad(width) || IsBad(height))
        {
            return null;
        }

        if (width < 200 || height < 200)
        {
            return null;
        }

        return new WindowPlacement
        {
            Left = left,
            Top = top,
            Width = width,
            Height = height,
            State = state,
        };
    }

    private static bool IsBad(double v)
    {
        return double.IsNaN(v) || double.IsInfinity(v);
    }

    private async Task LoadAndScanAsync()
    {
        this.config = await this.configStore.LoadAsync(CancellationToken.None);

        this.ApplyPlacement(this, this.config.MainWindowPlacement);

        this.Vm.WowRoot = this.config.WowRoot ?? string.Empty;
        this.Vm.BackupRoot = this.config.BackupRoot ?? string.Empty;
        this.RebuildAccountSvRowsFromConfig();
        this.Vm.SelectedSyncScope = this.config.SelectedSyncScope;
        this.Vm.SelectedTabIndex = this.config.LastSelectedTabIndex;
        this.Scan();

        // ✅ restore last tab
        this.Vm.SelectedTabIndex = this.config.LastSelectedTabIndex;

        this.isUiInitialized = true;

        this.Vm.PropertyChanged += async (_, args) =>
        {
            if (!string.Equals(args.PropertyName, nameof(MainVm.SelectedTabIndex), StringComparison.Ordinal))
            {
                return;
            }

            if (!this.isUiInitialized || this.suppressAutoSave)
            {
                return;
            }

            this.config = this.config with { LastSelectedTabIndex = this.Vm.SelectedTabIndex };
            await this.configStore.SaveAsync(this.config, CancellationToken.None);
        };
    }

    private void Scan()
    {
        this.Vm.Accounts.Clear();
        this.Vm.ValidationLines.Clear();

        var initialDiscovery = this.discoveryService.Discover(this.config);

        if (initialDiscovery.Context is not null)
        {
            // Determine defaults from persisted config.
            var hasIncluded = this.config.IncludedAccountNames.Count > 0;
            var configuredMain = this.config.MainAccountName;

            // If no main configured yet, pick first discovered account as main by default.
            var defaultMain = configuredMain;
            if (string.IsNullOrWhiteSpace(defaultMain))
            {
                defaultMain = initialDiscovery.Context.Accounts.Count > 0
                    ? initialDiscovery.Context.Accounts[0].AccountName
                    : null;
            }

            foreach (var a in initialDiscovery.Context.Accounts)
            {
                var realms = a.Realms.Count;
                var chars = a.Realms.Sum(r => r.Characters.Count);

                var isIncluded = !hasIncluded || this.config.IncludedAccountNames.Contains(a.AccountName);

                var isMain = !string.IsNullOrWhiteSpace(defaultMain) &&
                             a.AccountName.Equals(defaultMain, StringComparison.OrdinalIgnoreCase);

                var row = new AccountRow(
                    owner: this.Vm,
                    accountName: a.AccountName,
                    realms: realms,
                    characters: chars,
                    path: a.AccountPath)
                {
                    IsIncluded = isIncluded,
                };

                // Set main WITHOUT firing MainAccountChanged during scan.
                row.SetIsMainSilently(isMain);

                // Rule: main implies included.
                if (row.IsMain)
                {
                    row.IsIncluded = true;
                }

                this.Vm.Accounts.Add(row);

                row.PropertyChanged += async (_, args) =>
                {
                    if (args.PropertyName is nameof(AccountRow.IsIncluded) or nameof(AccountRow.IsMain))
                    {
                        var included = this.Vm.Accounts
                            .Where(accountRow => accountRow.IsIncluded)
                            .Select(accountRow => accountRow.AccountName)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        var main = this.Vm.Accounts.FirstOrDefault(accountRow => accountRow.IsMain)?.AccountName;

                        this.config = this.config with
                        {
                            IncludedAccountNames = included,
                            MainAccountName = main,
                        };

                        await this.configStore.SaveAsync(this.config, CancellationToken.None);
                        this.Vm.StatusLine = "✅ Account selection saved.";
                    }
                };
            }

            // Persist the default main account so future scans use the same effective state.
            if (string.IsNullOrWhiteSpace(this.config.MainAccountName) && !string.IsNullOrWhiteSpace(defaultMain))
            {
                this.config = this.config with { MainAccountName = defaultMain };
                _ = this.SaveConfigAsync("✅ Default main account saved.");
            }
        }

        var effectiveConfig = this.BuildEffectiveConfigFromUi();
        var effectiveDiscovery = this.discoveryService.Discover(effectiveConfig);

        this.lastContext = effectiveDiscovery.Context ?? initialDiscovery.Context;

        foreach (var m in effectiveDiscovery.Validation.Messages)
        {
            this.Vm.ValidationLines.Add(new ValidationLine(m.Severity.ToString(), m.Code, m.Message));
        }

        var applyValidation = this.validationService.ValidateForApply(effectiveConfig);
        this.Vm.StatusLine = applyValidation.IsOk
            ? "✅ Ready (validation OK for apply)."
            : "⛔ Blocked (fix validation errors before apply).";

        this.RebuildMainCharacterOptions();
        this.RefreshProfiles();
        this.RefreshPlugins();
        this.RefreshAccountSvExistsFlags();
    }

    private void RefreshProfiles()
    {
        this.Vm.LuaProfiles.Clear();
        this.Vm.ProfileRows.Clear();

        var files = this.profileStore.ListProfileFiles().OrderBy(Path.GetFileName, StringComparer.Ordinal).ToList();

        foreach (var file in files)
        {
            var profileFileName = Path.GetFileName(file);

            this.Vm.LuaProfiles.Add(new LuaProfileOption(file, profileFileName));

            var included = this.config.IncludedProfileFiles.Contains(profileFileName);
            var row = new ProfileRow(file, profileFileName, included);

            // Load profile to get lua filename
            try
            {
                var p = this.profileStore.Load(file);
                row.LuaFileName = string.IsNullOrWhiteSpace(p.FileName) ? "(no lua set)" : p.FileName;

                var luaPath = !string.IsNullOrWhiteSpace(row.LuaFileName)
                    ? this.ResolveMainSavedVariablesLuaPath(row.LuaFileName)
                    : null;

                row.LuaExists = !string.IsNullOrWhiteSpace(luaPath) && File.Exists(luaPath);
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"Profile load failed for {file}: {ex}");
#endif
                row.LuaFileName = "(invalid profile)";
                row.LuaExists = false;
            }

            this.Vm.ProfileRows.Add(row);

            // after 'row' created in RefreshProfiles()
            row.PropertyChanged += async (_, args) =>
            {
                if (!string.Equals(args.PropertyName, nameof(ProfileRow.IsIncluded), StringComparison.Ordinal))
                {
                    return;
                }

                var includedProfiles = this.Vm.ProfileRows
                    .Where(p => p.IsIncluded)
                    .Select(p => p.FileName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                this.config = this.config with { IncludedProfileFiles = includedProfiles };
                await this.configStore.SaveAsync(this.config, CancellationToken.None);

                // optional: status line update
                this.Vm.StatusLine = "✅ Profile selection saved.";
            };
        }
    }

    private void RefreshPlugins()
    {
        this.Vm.PluginRows.Clear();

        var plugins = WowSync.Plugins.BuiltIn.BuiltInPluginCatalog.CreateAll();

        foreach (var p in plugins)
        {
            var included = this.config.IncludedPluginIds.Contains(p.Id);
            var row = new PluginRow(p.Id, p.DisplayName, p.Description, included);

            this.Vm.PluginRows.Add(row);

            row.PropertyChanged += async (_, args) =>
            {
                if (!string.Equals(args.PropertyName, nameof(PluginRow.IsIncluded), StringComparison.Ordinal))
                {
                    return;
                }

                var includedPlugins = this.Vm.PluginRows
                    .Where(x => x.IsIncluded)
                    .Select(x => x.PluginId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                this.config = this.config with { IncludedPluginIds = includedPlugins };
                await this.configStore.SaveAsync(this.config, CancellationToken.None);

                this.Vm.StatusLine = "✅ Plugin selection saved.";
            };
        }
    }

    private WowContextSnapshot BuildFreshContextOrThrow(AppConfig effectiveConfig)
    {
        var discovery = this.discoveryService.Discover(effectiveConfig);

        if (discovery.Context is null)
        {
            throw new InvalidOperationException("Discovery failed. Fix validation errors first.");
        }

        return discovery.Context;
    }

    private AppConfig BuildEffectiveConfigFromUi()
    {
        var included = this.Vm.Accounts
            .Where(a => a.IsIncluded)
            .Select(a => a.AccountName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var main = this.Vm.Accounts.FirstOrDefault(a => a.IsMain)?.AccountName;
        var selected = this.Vm.SelectedMainCharacter;

        var files = this.GetAccountSvFilesFromUi();

        return this.config with
        {
            IncludedAccountNames = included,
            MainAccountName = main,
            MainRealmName = selected?.Realm,
            MainCharacterName = selected?.Name,
            AccountSavedVariablesFiles = files,
            WowRoot = string.IsNullOrWhiteSpace(this.Vm.WowRoot) ? this.config.WowRoot : this.Vm.WowRoot,
            BackupRoot = string.IsNullOrWhiteSpace(this.Vm.BackupRoot) ? this.config.BackupRoot : this.Vm.BackupRoot,
        };
    }

    private void RebuildAccountSvRowsFromConfig()
    {
        this.Vm.AccountSvRows.Clear();

        foreach (var file in this.config.AccountSavedVariablesFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var exists = this.DoesMainSavedVariablesFileExist(file);

            // Default behavior: if “Included set” is empty, treat all as included
            var included = this.config.IncludedAccountSavedVariablesFiles.Count == 0 || this.config.IncludedAccountSavedVariablesFiles.Contains(file);

            var row = new AccountSvRow(file, exists, included);
            this.Vm.AccountSvRows.Add(row);

            row.PropertyChanged += async (_, args) =>
            {
                if (!string.Equals(args.PropertyName, nameof(AccountSvRow.IsIncluded), StringComparison.Ordinal))
                {
                    return;
                }

                var includedSet = this.Vm.AccountSvRows
                    .Where(r => r.IsIncluded)
                    .Select(r => r.FileName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                this.config = this.config with { IncludedAccountSavedVariablesFiles = includedSet };
                await this.configStore.SaveAsync(this.config, CancellationToken.None);
                this.Vm.StatusLine = "✅ SavedVariables selection saved.";
            };
        }
    }

    private void RefreshAccountSvExistsFlags()
    {
        foreach (var row in this.Vm.AccountSvRows)
        {
            row.Exists = this.DoesMainSavedVariablesFileExist(row.FileName);
        }
    }

    private bool DoesMainSavedVariablesFileExist(string fileName)
    {
        var path = this.ResolveMainSavedVariablesLuaPath(fileName);
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    private async Task SaveConfigAsync(string statusLine, bool rescan = false)
    {
        await this.configStore.SaveAsync(this.config, CancellationToken.None);
        this.Vm.StatusLine = statusLine;

        if (rescan)
        {
            this.Scan();
        }
    }

    private async void PickWow_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Wow Executable|Wow.exe;WowClassic.exe;WowClassicT.exe|Executable|*.exe|All files|*.*",
            Title = "Select Wow.exe",
        };

        if (dlg.ShowDialog(this) == true)
        {
            var root = WowPaths.NormalizeRootFromExePath(dlg.FileName);
            this.Vm.WowRoot = root;

            this.config = this.BuildEffectiveConfigFromUi() with
            {
                WowRoot = root,
            };

            await this.SaveConfigAsync("✅ Wow root saved.", rescan: true);
        }
    }

    private async void PickBackup_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog();
        dlg.Description = "Select backup folder for WowSync";
        dlg.ShowNewFolderButton = true;

        var result = dlg.ShowDialog();
        if (result == System.Windows.Forms.DialogResult.OK)
        {
            this.Vm.BackupRoot = dlg.SelectedPath;

            this.config = this.BuildEffectiveConfigFromUi() with
            {
                BackupRoot = dlg.SelectedPath,
            };

            await this.SaveConfigAsync("✅ Backup folder saved.", rescan: true);
        }
    }

    private void Rescan_Click(object sender, RoutedEventArgs e)
    {
        this.Scan();
    }

    private async void DryRunSync_Click(object sender, RoutedEventArgs e)
    {
        await this.RunWithBusyUiAsync("Running dry run…", async () =>
        {
            try
            {
                if (this.lastContext is null)
                {
                    this.WriteRunOutput(["No context. Run scan first."]);
                    return;
                }

                var main = this.Vm.Accounts.FirstOrDefault(a => a.IsMain)?.AccountName;
                if (string.IsNullOrWhiteSpace(main))
                {
                    this.WriteRunOutput(["No main account selected."]);
                    return;
                }

                var effectiveConfig = this.BuildEffectiveConfigFromUi();
                var freshContext = this.BuildFreshContextOrThrow(effectiveConfig);

                this.lastContext = freshContext;

                var plan = this.BuildCombinedPlanOrThrow(freshContext);
                var result = await this.runEngine.DryRunExecuteAsync(effectiveConfig, plan);

                var lines = new List<string>
            {
                $"Dry run: {plan.Title}",
                $"Operations: {plan.Operations.Count}",
                string.Empty,
            };

                lines.AddRange(result.LogLines);
                this.WriteRunOutput(lines);
            }
            catch (Exception ex)
            {
                this.WriteRunOutput([$"Dry run failed: {ex.GetType().Name}: {ex.Message}"]);
            }
        });
    }

    private async void ApplySync_Click(object sender, RoutedEventArgs e)
    {
        await this.RunWithBusyUiAsync("Applying changes…", async () =>
        {
            try
            {
                if (this.lastContext is null)
                {
                    this.WriteRunOutput(["No context. Run scan first."]);
                    return;
                }

                var main = this.Vm.Accounts.FirstOrDefault(a => a.IsMain)?.AccountName;
                if (string.IsNullOrWhiteSpace(main))
                {
                    this.WriteRunOutput(["No main account selected."]);
                    return;
                }

                var effectiveConfig = this.BuildEffectiveConfigFromUi();
                var freshContext = this.BuildFreshContextOrThrow(effectiveConfig);

                this.lastContext = freshContext;

                var plan = this.BuildCombinedPlanOrThrow(freshContext);
                var result = await this.runEngine.ApplyAsync(effectiveConfig, plan);

                var lines = new List<string>
            {
                $"Applied: {plan.Title}",
                $"Backup: {result.RunResult.BackupPath}",
                $"Touched: {result.RunResult.TouchedPaths.Count}",
                string.Empty,
            };

                lines.AddRange(result.LogLines);
                this.WriteRunOutput(lines);

                this.lastApplyRun = result.RunResult;
                this.Vm.CanUndoLastApply = true;
            }
            catch (Exception ex)
            {
                this.WriteRunOutput([$"Apply failed: {ex.GetType().Name}: {ex.Message}"]);
            }
        });
    }

    private void LuaInspector_Click(object sender, RoutedEventArgs e)
    {
        var win = new LuaInspectorWindow { Owner = this };

        this.ShowInspector(win);

        if (win.WasSaved)
        {
            this.RefreshProfiles();
        }
    }

    private ProfileRow? GetProfileRow(object sender)
    {
        return (sender as FrameworkElement)?.Tag as ProfileRow;
    }

    private void RefreshProfiles_Click(object sender, RoutedEventArgs e)
    {
        this.RefreshProfiles();
        this.WriteRunOutput([$"Profiles folder: {this.profileStore.ProfilesFolder}", $"Profiles found: {this.Vm.LuaProfiles.Count}"]);
    }

    private void ProfilesGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IsFromInteractiveElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        // If the user double-clicks the header/empty area, ignore.
        if (sender is not DataGridRow row)
        {
            return;
        }

        if (row.Item is not ProfileRow pr)
        {
            return;
        }

        this.OpenProfileInInspector(pr);
        e.Handled = true;
    }

    private void OpenProfileInInspector(ProfileRow row)
    {
        try
        {
            var profile = this.profileStore.Load(row.FilePath);

            var luaPath = this.ResolveMainSavedVariablesLuaPath(profile.FileName);
            if (string.IsNullOrWhiteSpace(luaPath))
            {
                MessageBox.Show(this, "Cannot resolve lua path (no main account selected / no context).", "WowSync");
                return;
            }

            var win = new LuaInspectorWindow(luaPath, profile, profileJsonFileName: row.FileName) { Owner = this };

            this.ApplyPlacement(win, this.config.InspectorWindowPlacement);

            win.Closing += async (_, _) => await this.SaveInspectorPlacementAsync(win);

            win.ShowDialog();

            if (win.WasSaved)
            {
                this.RefreshProfiles();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Open inspector failed: {ex.Message}", "WowSync", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenProfileInInspector_Click(object sender, RoutedEventArgs e)
    {
        var row = this.GetProfileRow(sender);
        if (row == null)
        {
            return;
        }

        this.OpenProfileInInspector(row);
    }

    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        var row = this.GetProfileRow(sender);
        if (row == null)
        {
            return;
        }

        var currentBase = Path.GetFileNameWithoutExtension(row.FileName);

        var newName = Microsoft.VisualBasic.Interaction.InputBox(
            Prompt: "New profile file name (without .json):",
            Title: "Rename profile",
            DefaultResponse: currentBase);

        newName = newName.Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName.Equals(currentBase, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            // keep included flag by filename
            var wasIncluded = this.config.IncludedProfileFiles.Contains(row.FileName);

            var newPath = this.profileStore.Rename(row.FilePath, newName);
            var newFileName = Path.GetFileName(newPath);

            if (wasIncluded)
            {
                var set = new HashSet<string>(this.config.IncludedProfileFiles, StringComparer.OrdinalIgnoreCase);
                set.Remove(row.FileName);
                set.Add(newFileName);
                this.config = this.config with { IncludedProfileFiles = set };
            }

            this.RefreshProfiles();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Rename failed: {ex.Message}", "WowSync", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        var row = this.GetProfileRow(sender);
        if (row == null)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Delete profile?\n\n{row.FileName}",
            "WowSync",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            this.profileStore.Delete(row.FilePath);

            if (this.config.IncludedProfileFiles.Contains(row.FileName))
            {
                var set = new HashSet<string>(this.config.IncludedProfileFiles, StringComparer.OrdinalIgnoreCase);
                set.Remove(row.FileName);
                this.config = this.config with { IncludedProfileFiles = set };
            }

            this.RefreshProfiles();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Delete failed: {ex.Message}", "WowSync", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddAccountSvFile_Click(object sender, RoutedEventArgs e)
    {
        if (this.lastContext is null)
        {
            MessageBox.Show(this, "Run scan first.", "WowSync");
            return;
        }

        var dlg = new OpenFileDialog
        {
            Title = "Select SavedVariables .lua file(s)",
            Filter = "Lua files|*.lua|All files|*.*",
            Multiselect = true,
        };

        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        var pickedNames = dlg.FileNames
            .Select(Path.GetFileName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (pickedNames.Count == 0)
        {
            return;
        }

        // Merge into config list
        var merged = this.config.AccountSavedVariablesFiles
            .Concat(pickedNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var included = new HashSet<string>(this.config.IncludedAccountSavedVariablesFiles, StringComparer.OrdinalIgnoreCase);
        foreach (var name in pickedNames)
        {
            included.Add(name!); // default: included
        }

        this.config = this.config with
        {
            AccountSavedVariablesFiles = merged!,
            IncludedAccountSavedVariablesFiles = included,
        };

        await this.configStore.SaveAsync(this.config, CancellationToken.None);
        this.RebuildAccountSvRowsFromConfig();

        this.Vm.StatusLine = "✅ SavedVariables list saved.";
    }

    private async void DeleteAccountSvFile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AccountSvRow row)
        {
            return;
        }

        var updated = this.config.AccountSavedVariablesFiles
            .Where(x => !x.Equals(row.FileName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        this.config = this.config with { AccountSavedVariablesFiles = updated };
        await this.configStore.SaveAsync(this.config, CancellationToken.None);

        this.RebuildAccountSvRowsFromConfig();
        this.Vm.StatusLine = "✅ SavedVariables list saved.";
    }

    private void UndoLastApply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (this.lastApplyRun is null)
            {
                this.WriteRunOutput(["Nothing to undo yet. Run Apply first."]);
                return;
            }

            var undo = this.runEngine.UndoLastApply(this.config, this.lastApplyRun);

            var lines = new List<string>
            {
                "Undo: restored from backup",
                $"Backup: {undo.BackupPath}",
                string.Create(CultureInfo.InvariantCulture, $"Restored: {undo.RestoredFiles}, Deleted: {undo.DeletedFiles}, Skipped: {undo.SkippedFiles}"),
                string.Empty,
            };

            lines.AddRange(undo.LogLines);
            this.WriteRunOutput(lines);
            this.lastApplyRun = null;
            this.Vm.CanUndoLastApply = false;
        }
        catch (Exception ex)
        {
            this.WriteRunOutput([$"Undo failed: {ex.GetType().Name}: {ex.Message}"]);
        }
    }

    private async void SyncScope_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!this.isUiInitialized || this.suppressAutoSave)
        {
            return;
        }

        this.config = this.config with { SelectedSyncScope = this.Vm.SelectedSyncScope };
        await this.SaveConfigAsync("✅ Scope saved.");
    }

    private void ProfileUseCheckBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not WpfCheckBox cb)
        {
            return;
        }

        // Stop DataGrid from consuming the click for selection/focus first.
        e.Handled = true;

        // Make sure the cell enters edit mode on first click.
        cb.Focus();

        cb.IsChecked = !(cb.IsChecked ?? false);

        // Push value to VM immediately.
        cb.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateSource();
    }

    private void MainToon_UserInteraction(object sender, MouseButtonEventArgs e)
    {
        this.mainToonChangeIsUserInitiated = true;
    }

    private void MainToon_UserInteraction_KeyDown(object sender, KeyEventArgs e)
    {
        // e.g. keyboard navigation
        if (e.Key is Key.Up or Key.Down or Key.Enter)
        {
            this.mainToonChangeIsUserInitiated = true;
        }
    }

    private async void MainToon_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!this.isUiInitialized || this.suppressAutoSave)
        {
            return;
        }

        if (!this.mainToonChangeIsUserInitiated)
        {
            return;
        }

        this.mainToonChangeIsUserInitiated = false;

        var selected = this.Vm.SelectedMainCharacter;
        if (selected is null)
        {
            return; // user cleared selection? unlikely, but safe
        }

        this.config = this.config with
        {
            MainRealmName = selected.Realm,
            MainCharacterName = selected.Name,
        };

        await this.SaveConfigAsync("✅ Main toon saved.");
    }

    private void RebuildMainCharacterOptions()
    {
        this.Vm.MainCharacterOptions.Clear();

        var mainAccount = this.Vm.Accounts.FirstOrDefault(a => a.IsMain)?.AccountName;

        if (string.IsNullOrWhiteSpace(mainAccount) || this.lastContext is null)
        {
            this.suppressAutoSave = true;
            try
            {
                this.Vm.IsMainCharacterPickerEnabled = false;
                this.Vm.MainCharacterHint = "Select a main account to choose a main toon.";
                this.Vm.SelectedMainCharacter = null;
            }
            finally
            {
                this.suppressAutoSave = false;
            }

            return;
        }

        var account = this.lastContext.Accounts
            .FirstOrDefault(a => a.AccountName.Equals(mainAccount, StringComparison.OrdinalIgnoreCase));

        if (account is null)
        {
            this.suppressAutoSave = true;
            try
            {
                this.Vm.IsMainCharacterPickerEnabled = false;
                this.Vm.MainCharacterHint = "Main account not found in discovery.";
                this.Vm.SelectedMainCharacter = null;
            }
            finally
            {
                this.suppressAutoSave = false;
            }

            return;
        }

        var options = account.Realms
            .OrderBy(r => r.RealmName, StringComparer.Ordinal)
            .SelectMany(r => r.Characters.OrderBy(c => c.CharacterName, StringComparer.Ordinal)
                .Select(c => new CharacterOption(r.RealmName, c.CharacterName)))
            .ToList();

        foreach (var opt in options)
        {
            this.Vm.MainCharacterOptions.Add(opt);
        }

        this.Vm.IsMainCharacterPickerEnabled = options.Count > 0;
        this.Vm.MainCharacterHint = options.Count > 0
            ? "Used as template toon for certain plugins (eg. Zygor normalization)."
            : "No characters found under the selected main account.";

        this.suppressAutoSave = true;
        try
        {
            // Try re-selecting configured main toon if it exists
            if (!string.IsNullOrWhiteSpace(this.config.MainRealmName) &&
                !string.IsNullOrWhiteSpace(this.config.MainCharacterName))
            {
                var match = this.Vm.MainCharacterOptions.FirstOrDefault(o =>
                    o.Realm.Equals(this.config.MainRealmName, StringComparison.OrdinalIgnoreCase) &&
                    o.Name.Equals(this.config.MainCharacterName, StringComparison.OrdinalIgnoreCase));

                this.Vm.SelectedMainCharacter = match ?? this.Vm.MainCharacterOptions.FirstOrDefault();
            }
            else
            {
                if (this.Vm.SelectedMainCharacter is not null &&
                    !this.Vm.MainCharacterOptions.Contains(this.Vm.SelectedMainCharacter))
                {
                    this.Vm.SelectedMainCharacter = null;
                }
            }
        }
        finally
        {
            this.suppressAutoSave = false;
        }
    }

    private List<string> GetAccountSvFilesFromUi()
    {
        return [.. this.Vm.AccountSvRows.Where(r => r.IsIncluded).Select(r => r.FileName)];
    }

    private void WriteRunOutput(IEnumerable<string> lines)
    {
        this.Vm.RunOutputText = string.Join(Environment.NewLine, lines);
        this.Vm.IsRunOutputExpanded = true;
    }

    private string? ResolveMainSavedVariablesLuaPath(string luaFileName)
    {
        if (this.lastContext is null)
        {
            return null;
        }

        var main = this.Vm.Accounts.FirstOrDefault(a => a.IsMain)?.AccountName;
        if (string.IsNullOrWhiteSpace(main))
        {
            return null;
        }

        var acct = this.lastContext.Accounts.FirstOrDefault(a =>
                                                           a.AccountName.Equals(main, StringComparison.OrdinalIgnoreCase));

        if (acct is null)
        {
            return null;
        }

        // AccountPath should already be ...\WTF\Account\<ACCOUNT>
        return Path.Combine(acct.AccountPath, "SavedVariables", luaFileName);
    }

    private OperationPlan BuildCombinedPlanOrThrow(WowContextSnapshot ctx)
    {
        var builder = new CombinedRunPlanBuilder(BuiltInPluginCatalog.CreateAll());
        return builder.Build(
            ctx: ctx,
            accountSvFiles: this.GetAccountSvFilesFromUi(),
            selectedProfiles: [.. this.Vm.ProfileRows.Where(p => p.IsIncluded)],
            selectedPluginIds: this.Vm.PluginRows.Where(p => p.IsIncluded).Select(p => p.PluginId).ToHashSet(StringComparer.OrdinalIgnoreCase),
            scope: this.Vm.SelectedSyncScope,
            profileStore: this.profileStore);
    }

    private void ShowInspector(Window win)
    {
        // Apply placement at the *right* time.
        // SourceInitialized fires once the HWND exists, before first render.
        win.SourceInitialized += (_, _) => this.ApplyPlacement(win, this.config.InspectorWindowPlacement);

        // Save placement *before* teardown, and actually await it.
        win.Closing += async (_, _) => await this.SaveInspectorPlacementAsync(win);

        // Critical: if the InspectorWindow XAML uses WindowStartupLocation=CenterOwner/CenterScreen,
        // that will override Left/Top. Force manual for restore.
        win.WindowStartupLocation = WindowStartupLocation.Manual;

        win.ShowDialog();
    }

    private async Task SaveInspectorPlacementAsync(Window inspector)
    {
        var placement = TryCapturePlacement(inspector);
        if (placement is null)
        {
            return; // keep old value, don’t poison config
        }

        this.config = this.config with { InspectorWindowPlacement = placement };
        await this.configStore.SaveAsync(this.config, CancellationToken.None);
    }

    private async Task RunWithBusyUiAsync(string busyText, Func<Task> action)
    {
        this.Vm.IsBusy = true;
        this.Vm.BusyText = busyText;

        var prevCursor = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;

        try
        {
            // Give WPF a chance to repaint + apply bindings (disable buttons, etc.)
            await this.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            await action();
        }
        finally
        {
            Mouse.OverrideCursor = prevCursor;
            this.Vm.IsBusy = false;
            this.Vm.BusyText = "";
        }
    }

    private void ApplyPlacement(Window w, WindowPlacement? p)
    {
        if (p == null)
        {
            return;
        }

        // Basic sanity clamp (avoid off-screen weirdness)
        if (p is { Width: > 100, Height: > 100 })
        {
            w.Left = p.Left;
            w.Top = p.Top;
            w.Width = p.Width;
            w.Height = p.Height;
        }

        w.WindowState = ToWpfState(p.State);
    }
}
