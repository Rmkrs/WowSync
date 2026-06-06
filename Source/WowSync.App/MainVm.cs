// ReSharper disable CollectionNeverQueried.Global
// ReSharper disable UnusedMember.Global
namespace WowSync.App;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WowSync.Plugins.Abstractions.Contracts;

public sealed class MainVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? MainAccountChanged;

    public ObservableCollection<AccountRow> Accounts { get; } = [];

    public ObservableCollection<ValidationLine> ValidationLines { get; } = [];

    public ObservableCollection<CharacterOption> MainCharacterOptions { get; } = [];

    public ObservableCollection<LuaProfileOption> LuaProfiles { get; } = [];

    public ObservableCollection<ProfileRow> ProfileRows { get; } = [];

    public ObservableCollection<PluginRow> PluginRows { get; } = [];

    public ObservableCollection<AccountSvRow> AccountSvRows { get; } = [];

    public ObservableCollection<SyncScope> SyncScopes { get; } =
    [
        SyncScope.MainToMainAccountOtherToons,
        SyncScope.MainToSubAccounts,
        SyncScope.MainToAllAccountsAndToons,
    ];

    public string WowRoot
    {
        get;
        set
        {
            field = value;
            this.OnPropertyChanged();
        }
    } = string.Empty;

    public string BackupRoot
    {
        get;
        set
        {
            field = value;
            this.OnPropertyChanged();
        }
    } = string.Empty;

    public string StatusLine
    {
        get; set
        {
            field = value;
            this.OnPropertyChanged();
        }
    } = string.Empty;

    public CharacterOption? SelectedMainCharacter
    {
        get;
        set
        {
            field = value;
            this.OnPropertyChanged();
        }
    }

    public bool IsMainCharacterPickerEnabled
    {
        get;
        set
        {
            field = value;
            this.OnPropertyChanged();
        }
    }

    public string MainCharacterHint
    {
        get;
        set
        {
            field = value;
            this.OnPropertyChanged();
        }
    }
        = "";

    public SyncScope SelectedSyncScope
    {
        get;
        set
        {
            field = value;
            this.OnPropertyChanged();
        }
    }
        = SyncScope.MainToAllAccountsAndToons;

    public string RunOutputText
    {
        get;
        set
        {
            field = value;
            this.OnPropertyChanged();
        }
    }
        = "";

    public ProfileRow? SelectedProfileRow
    {
        get;
        set
        {
            field = value;
            this.OnPropertyChanged();
        }
    }

    public bool CanUndoLastApply
    {
        get;
        set
        {
            field = value;
            this.OnPropertyChanged();
        }
    }

    public bool IsRunOutputExpanded
    {
        get;
        set
        {
            field = value;
            this.OnPropertyChanged();
        }
    }

    public int SelectedTabIndex
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            this.OnPropertyChanged();
        }
    }

    public bool IsBusy
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            this.OnPropertyChanged();
            this.OnPropertyChanged(nameof(this.IsNotBusy));
        }
    }

    public bool IsNotBusy => !this.IsBusy;

    public string BusyText
    {
        get; set
        {
            field = value;
            this.OnPropertyChanged();
        }
    } = "";

    internal void EnforceSingleMain(AccountRow selected)
    {
        foreach (var a in this.Accounts)
        {
            if (!ReferenceEquals(a, selected) && a.IsMain)
            {
                a.SetIsMainSilently(value: false);
            }
        }

        this.MainAccountChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
