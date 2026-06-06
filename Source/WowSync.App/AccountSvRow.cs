namespace WowSync.App;

using System.ComponentModel;

public sealed class AccountSvRow(string fileName, bool exists, bool isIncluded) : INotifyPropertyChanged
{
    public string FileName { get; } = fileName;

    public bool Exists
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            this.PropertyChanged?.Invoke(this, new(nameof(this.Exists)));
        }
    } = exists;

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
