namespace WowSync.App.LuaInspector;

using System.Collections.ObjectModel;

public sealed class LuaTreeNode(string name, string path, string displayValue)
{
    public string Name { get; } = name;

    public string Path { get; } = path;

    public string DisplayValue { get; } = displayValue;

    public ObservableCollection<LuaTreeNode> Children { get; } = [];

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(this.DisplayValue) ? this.Name : $"{this.Name} = {this.DisplayValue}";
    }
}