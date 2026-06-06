namespace WowSync.Plugins.Abstractions.Operations;

public interface IRunLogger
{
    void Info(string message);

    void Warn(string message);

    void Err(string message, Exception? ex = null);
}
