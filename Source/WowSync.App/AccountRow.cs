// ReSharper disable UnusedMember.Global
namespace WowSync.App;

using System.ComponentModel;
using System.Runtime.CompilerServices;

public sealed class AccountRow(MainVm owner, string accountName, int realms, int characters, string path) : INotifyPropertyChanged
{
    private bool isMain;
    private bool suppressMainEnforcement;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string AccountName { get; } = accountName;

    public int Realms { get; } = realms;

    public int Characters { get; } = characters;

    public string Path { get; } = path;

    public bool IsIncluded
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;

            // If user un-includes the current main, unset main too
            if (!field && this.IsMain)
            {
                this.SetIsMainSilently(value: false);
            }

            this.OnPropertyChanged();
        }
    }

    public bool IsMain
    {
        get => this.isMain;
        set
        {
            if (this.isMain == value)
            {
                return;
            }

            this.isMain = value;

            if (this.isMain)
            {
                // Main implies included
                this.IsIncluded = true;

                if (!this.suppressMainEnforcement)
                {
                    owner.EnforceSingleMain(this);
                }
            }

            this.OnPropertyChanged();
        }
    }

    internal void SetIsMainSilently(bool value)
    {
        this.suppressMainEnforcement = true;
        try
        {
            this.isMain = value;
            this.OnPropertyChanged(nameof(this.IsMain));
        }
        finally
        {
            this.suppressMainEnforcement = false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
