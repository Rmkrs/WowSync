namespace WowSync.App.LuaInspector;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using WowSync.Core.Profiles;

public sealed class LuaInspectorVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string FilePath
    {
        get;
        set
        {
            field = value;
            this.OnPropertyChanged();
        }
    } = "";
    public string GlobalName
    {
        get;
        set
        {
            field = value;
            this.OnPropertyChanged();
        }
    } = "";
    public string SelectedPath
    {
        get;
        set
        {
            field = value;
            this.OnPropertyChanged();
        }
    } = "";
    public string SelectedValue
    {
        get;
        set
        {
            field = value;
            this.OnPropertyChanged();
        }
    } = "";

    public string ProfileName
    {
        get;
        set
        {
            field = value;
            this.OnPropertyChanged();
        }
    } = "New Profile";
    public string ProfileFileName
    {
        get;
        set
        {
            field = value;
            this.OnPropertyChanged();
        }
    } = "";

    public string SearchText
    {
        get;
        set
        {
            field = value;
            this.OnPropertyChanged();
        }
    } = "";
    public string SearchStatus
    {
        get;
        set
        {
            field = value;
            this.OnPropertyChanged();
        }
    } = "";

    public PatchMode ProfilePatchMode
    {
        get;
        set
        {
            field = value;
            this.OnPropertyChanged();
        }
    } = PatchMode.AddIfParentExists;

    public ObservableCollection<PatchMode> PatchModeOptions { get; } =
    [
        PatchMode.AddIfParentExists,
        PatchMode.UpdateOnly,
    ];

    public string StatusMessage
    {
        get;
        set
        {
            field = value;
            this.OnPropertyChanged();
        }
    } = "";

    public bool StatusIsError
    {
        get;
        set
        {
            field = value;
            this.OnPropertyChanged();
        }
    }

    public bool IsEditingExistingProfile
    {
        get;
        set
        {
            field = value;
            this.OnPropertyChanged();
        }
    }

    public ObservableCollection<LuaTreeNode> Nodes { get; } = [];
    public ObservableCollection<string> IncludePaths { get; } = [];

    public LuaSyncProfile BuildProfile()
    {
        return new()
        {
            Name = this.ProfileName,
            FileName = this.ProfileFileName,
            IncludePaths = [.. this.IncludePaths],
            PatchMode = this.ProfilePatchMode,
        };
    }

    public void SetStatus(string message, bool isError = false)
    {
        this.StatusMessage = message;
        this.StatusIsError = isError;
    }

    public void LoadProfile(LuaSyncProfile p, string? profileJsonFileName = null)
    {
        // If we opened an existing profile from disk, use the JSON filename as the "profile name"
        if (!string.IsNullOrWhiteSpace(profileJsonFileName))
        {
            this.IsEditingExistingProfile = true;
            this.ProfileName = Path.GetFileNameWithoutExtension(profileJsonFileName);
        }
        else
        {
            this.IsEditingExistingProfile = false;
            this.ProfileName = p.Name;
        }

        this.ProfileFileName = p.FileName;

        this.IncludePaths.Clear();
        foreach (var x in p.IncludePaths)
        {
            this.IncludePaths.Add(x);
        }

        this.ProfilePatchMode = p.PatchMode;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
