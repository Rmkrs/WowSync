// ReSharper disable CommentTypo
namespace WowSync.Core.Profiles;

using System.Text.Json.Serialization;

public sealed record LuaSyncProfile
{
    public string Name { get; init; } = "New Profile";

    /// <summary>SavedVariables file name, e.g. "ZygorGuidesViewer.lua"</summary>
    public string FileName { get; init; } = "";

    /// <summary>Exact paths selected in the inspector (v1: no wildcards yet).</summary>
    public List<string> IncludePaths { get; init; } = [];

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PatchMode PatchMode { get; init; } = PatchMode.AddIfParentExists;
}
