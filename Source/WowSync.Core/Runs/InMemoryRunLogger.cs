namespace WowSync.Core.Runs;

using WowSync.Plugins.Abstractions.Operations;

public sealed class InMemoryRunLogger : IRunLogger
{
    private readonly List<string> lines = [];

    public IReadOnlyList<string> Lines => this.lines;

    public void Info(string message)
    {
        this.lines.Add($"INFO  {message}");
    }

    public void Warn(string message)
    {
        this.lines.Add($"WARN  {message}");
    }

    public void Err(string message, Exception? ex = null)
    {
        this.lines.Add(ex is null ? $"ERROR {message}" : $"ERROR {message} | {ex.GetType().Name}: {ex.Message}");
    }
}
