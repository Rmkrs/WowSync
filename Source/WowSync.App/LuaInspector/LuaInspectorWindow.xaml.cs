namespace WowSync.App.LuaInspector;

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using WowSync.Core.Lua;
using WowSync.Core.Profiles;

public partial class LuaInspectorWindow
{
    private readonly ProfileStore profileStore = new();
    private readonly LuaInspectorVm vm = new();

    // Search index
    private readonly List<LuaTreeNode> flatNodes = [];
    private readonly Dictionary<LuaTreeNode, LuaTreeNode?> parentMap = [];

    // Current selection (for "start from where you are" semantics)
    private LuaTreeNode? selectedNode;

    // Search state (global match list + current position inside it)
    private string lastSearchTerm = "";
    private readonly List<int> matchIndices = []; // indices into flatNodes
    private int currentMatchListIndex = -1;          // -1 = not positioned yet for current term

    public bool WasSaved { get; private set; }

    public LuaInspectorWindow()
    {
        this.InitializeComponent();
        this.DataContext = this.vm;
        this.vm.SearchStatus = "";
        this.vm.SetStatus("Ready.");
    }

    // Open directly (and optionally load an existing profile into the editor)
    public LuaInspectorWindow(string luaFilePath, LuaSyncProfile? profileToEdit = null, string? profileJsonFileName = null)
        : this()
    {
        if (profileToEdit is not null)
        {
            this.vm.LoadProfile(profileToEdit, profileJsonFileName);
        }

        if (!string.IsNullOrWhiteSpace(luaFilePath))
        {
            this.TryLoadLuaFile(luaFilePath);
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Lua SavedVariables (*.lua)|*.lua|All files (*.*)|*.*",
            Title = "Open SavedVariables file",
        };

        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        this.TryLoadLuaFile(dlg.FileName);
    }

    private void TryLoadLuaFile(string path)
    {
        if (!File.Exists(path))
        {
            this.vm.SetStatus($"Lua file not found: {path}", isError: true);
            return;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            this.vm.SetStatus($"Failed to read lua: {ex.Message}", isError: true);
            return;
        }

        LuaDocument doc;
        try
        {
            doc = new LuaParser(text).ParseDocument();
        }
        catch (Exception ex)
        {
            this.vm.SetStatus($"Failed to parse lua: {ex.Message}", isError: true);
            return;
        }

        this.vm.FilePath = path;
        this.vm.GlobalName = doc.HasSingleAssignment
            ? doc.SingleGlobalName ?? ""
            : $"(multiple: {doc.Assignments.Count})";

        if (string.IsNullOrWhiteSpace(this.vm.ProfileFileName))
        {
            this.vm.ProfileFileName = Path.GetFileName(path);
        }

        // Only auto-default these when we're NOT editing an existing profile
        if (!this.vm.IsEditingExistingProfile)
        {
            if (string.IsNullOrWhiteSpace(this.vm.ProfileName) || string.Equals(this.vm.ProfileName, "New Profile", StringComparison.OrdinalIgnoreCase))
            {
                this.vm.ProfileName = "New Profile";
            }
        }

        this.vm.Nodes.Clear();
        foreach (var a in doc.Assignments)
        {
            this.vm.Nodes.Add(LuaTreeBuilder.Build(a));
        }

        this.RebuildSearchIndex();
        this.ResetSearchState();

        this.vm.SearchStatus = this.flatNodes.Count == 0 ? "No nodes." : string.Create(CultureInfo.InvariantCulture, $"{this.flatNodes.Count:n0} nodes.");
        this.vm.SetStatus($"Loaded: {Path.GetFileName(path)}");
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not LuaTreeNode node)
        {
            return;
        }

        this.selectedNode = node;

        this.vm.SelectedPath = node.Path;
        this.vm.SelectedValue = node.DisplayValue;
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(this.vm.SelectedPath))
        {
            Clipboard.SetText(this.vm.SelectedPath);
            this.vm.SetStatus("Path copied.");
        }
    }

    private void AddPath_Click(object sender, RoutedEventArgs e)
    {
        var path = this.vm.SelectedPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!this.vm.IncludePaths.Contains(path))
        {
            this.vm.IncludePaths.Add(path);
            this.vm.SetStatus("Path added to profile.");
        }
        else
        {
            this.vm.SetStatus("Path already in profile.");
        }
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        var listBox = FindProfileListBox(this);
        if (listBox?.SelectedItem is string path)
        {
            this.vm.IncludePaths.Remove(path);
            this.vm.SetStatus("Path removed.");
        }
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = this.vm.BuildProfile();
            var savedPath = this.profileStore.Save(profile);

            this.WasSaved = true;
            this.vm.SetStatus($"Saved profile: {Path.GetFileName(savedPath)}");
        }
        catch (Exception ex)
        {
            this.vm.SetStatus($"Save failed: {ex.Message}", isError: true);
        }
    }

    private static ListBox? FindProfileListBox(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is ListBox { ItemsSource: not null } lb)
            {
                return lb;
            }

            var result = FindProfileListBox(child);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    // ---------------------------
    // Search
    // ---------------------------

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            this.FindNext();
        }
    }

    private void FindNext_Click(object sender, RoutedEventArgs e)
    {
        this.FindNext();
    }

    private void FindNext()
    {
        var term = this.vm.SearchText.Trim();

        if (this.flatNodes.Count == 0)
        {
            this.vm.SearchStatus = "Open a .lua file first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(term))
        {
            this.vm.SearchStatus = "Type something to search for.";
            return;
        }

        var termChanged = !term.Equals(this.lastSearchTerm, StringComparison.OrdinalIgnoreCase);

        if (termChanged)
        {
            this.lastSearchTerm = term;
            this.BuildMatches(term);
            this.currentMatchListIndex = -1;
        }

        if (this.matchIndices.Count == 0)
        {
            this.vm.SearchStatus = $"No matches for \"{term}\".";
            return;
        }

        if (this.currentMatchListIndex < 0)
        {
            var cursorFlatIndex = this.GetCursorFlatIndex();
            this.currentMatchListIndex = this.FindNextMatchListIndexAfter(cursorFlatIndex);
        }
        else
        {
            this.currentMatchListIndex++;
            if (this.currentMatchListIndex >= this.matchIndices.Count)
            {
                this.currentMatchListIndex = 0;
            }
        }

        var flatIndex = this.matchIndices[this.currentMatchListIndex];
        var node = this.flatNodes[flatIndex];

        this.SelectAndRevealNode(node);

        this.vm.SearchStatus = string.Create(CultureInfo.InvariantCulture, $"{this.currentMatchListIndex + 1:n0}/{this.matchIndices.Count:n0} (\"{term}\")");
    }

    private void BuildMatches(string term)
    {
        this.matchIndices.Clear();

        for (var i = 0; i < this.flatNodes.Count; i++)
        {
            var n = this.flatNodes[i];

            if (Contains(n.Name, term) ||
                Contains(n.Path, term) ||
                Contains(n.DisplayValue, term))
            {
                this.matchIndices.Add(i);
            }
        }
    }

    private int GetCursorFlatIndex()
    {
        if (this.selectedNode is null)
        {
            return -1;
        }

        return this.flatNodes.IndexOf(this.selectedNode);
    }

    private int FindNextMatchListIndexAfter(int cursorFlatIndex)
    {
        for (var i = 0; i < this.matchIndices.Count; i++)
        {
            if (this.matchIndices[i] > cursorFlatIndex)
            {
                return i;
            }
        }

        return 0;
    }

    private static bool Contains(string? haystack, string needle)
    {
        return !string.IsNullOrEmpty(haystack) &&
               haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private void ResetSearchState()
    {
        this.lastSearchTerm = "";
        this.matchIndices.Clear();
        this.currentMatchListIndex = -1;
    }

    private void RebuildSearchIndex()
    {
        this.flatNodes.Clear();
        this.parentMap.Clear();

        foreach (var root in this.vm.Nodes)
        {
            this.AddNodeRecursive(root, parent: null);
        }
    }

    private void AddNodeRecursive(LuaTreeNode node, LuaTreeNode? parent)
    {
        this.flatNodes.Add(node);
        this.parentMap[node] = parent;

        foreach (var child in node.Children)
        {
            this.AddNodeRecursive(child, node);
        }
    }

    private void SelectAndRevealNode(LuaTreeNode node)
    {
        var chain = new Stack<LuaTreeNode>();
        var cur = node;

        while (true)
        {
            chain.Push(cur);

            if (!this.parentMap.TryGetValue(cur, out var parent) || parent is null)
            {
                break;
            }

            cur = parent;
        }

        ItemsControl currentContainer = this.AstTree;
        TreeViewItem? itemContainer = null;

        while (chain.Count > 0)
        {
            var nextNode = chain.Pop();

            itemContainer = GetContainerForItem(currentContainer, nextNode);
            if (itemContainer is null)
            {
                currentContainer.UpdateLayout();
                itemContainer = GetContainerForItem(currentContainer, nextNode);
            }

            if (itemContainer is null)
            {
                return;
            }

            itemContainer.IsExpanded = true;
            currentContainer = itemContainer;
        }

        if (itemContainer is not null)
        {
            itemContainer.IsSelected = true;
            itemContainer.BringIntoView();
            itemContainer.Focus();
        }
    }

    private static TreeViewItem? GetContainerForItem(ItemsControl parent, object item)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem tvi)
        {
            return tvi;
        }

        for (var i = 0; i < parent.Items.Count; i++)
        {
            if (parent.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem child)
            {
                continue;
            }

            if (ReferenceEquals(parent.Items[i], item))
            {
                return child;
            }
        }

        return null;
    }
}
