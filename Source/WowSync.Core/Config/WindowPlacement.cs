namespace WowSync.Core.Config;

using System.Text.Json.Serialization;

public sealed record WindowPlacement
{
    public double Left { get; init; }

    public double Top { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppWindowState State { get; init; } = AppWindowState.Normal;
}