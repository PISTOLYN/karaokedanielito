using System.Text.Json;
using Karaoke.Core.Logging;

namespace Karaoke.Core.Persistence;

/// <summary>Ubicaciones persistentes (%AppData%), nunca temporales.</summary>
public sealed class AppPaths
{
    public string Root { get; }
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string LogsDir => Path.Combine(Root, "Logs");

    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KaraokeInteractivo");
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDir);
    }
}

public static class JsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static T LoadOrDefault<T>(string path, Func<T> factory, IAppLogger? log = null)
    {
        if (!File.Exists(path)) return factory();
        try
        {
            var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
            return value ?? factory();
        }
        catch (Exception ex)
        {
            log?.Error("persistencia", $"Archivo dañado, se respalda y se usan valores iniciales: {path}", ex);
            try { File.Move(path, path + $".dañado-{DateTime.Now:yyyyMMddHHmmss}", true); } catch { }
            return factory();
        }
    }

    /// <summary>Escritura atómica: escribe a .tmp y reemplaza, para no corromper el archivo si se corta la luz.</summary>
    public static void Save<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, true);
    }
}
