namespace Karaoke.Core.Logging;

public interface IAppLogger
{
    void Info(string area, string message);
    void Warn(string area, string message);
    void Error(string area, string message, Exception? ex = null);
}

public sealed class NullLogger : IAppLogger
{
    public void Info(string area, string message) { }
    public void Warn(string area, string message) { }
    public void Error(string area, string message, Exception? ex = null) { }
}

/// <summary>Registro en archivo diario. Nunca lanza excepciones: un fallo de log no debe tumbar el show.</summary>
public sealed class FileLogger : IAppLogger
{
    private readonly string _dir;
    private readonly object _gate = new();

    public FileLogger(string directory) { _dir = directory; }

    public void Info(string area, string message) => Write("INFO", area, message, null);
    public void Warn(string area, string message) => Write("WARN", area, message, null);
    public void Error(string area, string message, Exception? ex = null) => Write("ERROR", area, message, ex);

    private void Write(string level, string area, string message, Exception? ex)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_dir);
                var file = Path.Combine(_dir, $"karaoke-{DateTime.Now:yyyyMMdd}.log");
                var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] [{area}] {message}";
                if (ex != null) line += " | " + ex;
                File.AppendAllText(file, line + Environment.NewLine);
            }
        }
        catch { /* ignorar */ }
    }
}
