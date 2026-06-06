namespace WowSync.App;

using System.ComponentModel;

public sealed class ProfileRow(string filePath, string fileName, bool isIncluded) : INotifyPropertyChanged
{
    public string FilePath { get; } = filePath;

    public string FileName { get; } = fileName;

    public string LuaFileName
    {
        get;
        set
        {
            field = value;
            this.PropertyChanged?.Invoke(this, new(nameof(this.LuaFileName)));
        }
    } = "";

    public bool LuaExists
    {
        get;
        set
        {
            field = value;
            this.PropertyChanged?.Invoke(this, new(nameof(this.LuaExists)));
        }
    }

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
            this.PropertyChanged?.Invoke(this, new(nameof(this.IsIncluded)));
        }
    } = isIncluded;

    public event PropertyChangedEventHandler? PropertyChanged;
}
