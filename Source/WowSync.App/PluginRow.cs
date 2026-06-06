// ReSharper disable UnusedMember.Global
namespace WowSync.App;

using System.ComponentModel;

public sealed class PluginRow(string pluginId, string displayName, string description, bool isIncluded) : INotifyPropertyChanged
{
    public string PluginId { get; } = pluginId;

    public string DisplayName { get; } = displayName;

    public string Description { get; } = description;

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
