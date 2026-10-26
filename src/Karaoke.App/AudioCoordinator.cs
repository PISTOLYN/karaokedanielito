using Karaoke.Core.Logging;

namespace Karaoke.Core.Audio;

public enum AudioChannel { Voice, Music, Effect }
public enum ToggleResult { Started, Stopped, Blocked }

/// <summary>Una reproducción en curso. La implementación real (NAudio / SAPI) llega en la etapa 2.</summary>
public interface IPlaybackHandle : IDisposable
{
    void Stop();
    void Pause();
    void Resume();
    event EventHandler? Finished;
}

public interface IPlaybackFactory
{
    IPlaybackHandle Start(AudioChannel channel, string source);
}

/// <summary>
/// Única puerta de entrada al audio. Garantiza: un solo elemento activo por canal,
/// doble pulsación = detener, pausa distinta de detener, y DETENER TODO con token de generación
/// que invalida cualquier acción pendiente.
/// Decisión actual: los canales Voz, Música y Efecto son independientes entre sí
/// (una celebración puede sonar sobre la música). Si prefieres que se excluyan, es un cambio local aquí.
/// </summary>
public sealed class AudioCoordinator
{
    private sealed class Active
    {
        public Active(string key, IPlaybackHandle handle) { Key = key; Handle = handle; }
        public string Key { get; }
        public IPlaybackHandle Handle { get; }
        public EventHandler? OnFinished { get; set; }
    }

    private readonly object _gate = new();
    private readonly IPlaybackFactory _factory;
    private readonly IAppLogger _log;
    private readonly Dictionary<AudioChannel, Active> _active = new();
    private long _generation;
    private bool _paused;

    public event Action? Changed;

    public AudioCoordinator(IPlaybackFactory factory, IAppLogger? log = null)
    {
        _factory = factory;
        _log = log ?? new NullLogger();
    }

    public long Generation { get { lock (_gate) return _generation; } }
    public bool IsPaused { get { lock (_gate) return _paused; } }
    public bool IsActive(AudioChannel c) { lock (_gate) return _active.ContainsKey(c); }
    public string? ActiveKey(AudioChannel c) { lock (_gate) return _active.TryGetValue(c, out var a) ? a.Key : null; }

    /// <summary>
    /// Misma clave activa en el canal → detiene (vuelve al principio). Otra clave → reemplaza la anterior.
    /// Si expectedGeneration no coincide (hubo DETENER TODO), la acción pendiente se bloquea.
    /// </summary>
    public ToggleResult Toggle(AudioChannel channel, string key, string source, long? expectedGeneration = null)
    {
        ToggleResult result;
        lock (_gate)
        {
            if (expectedGeneration.HasValue && expectedGeneration.Value != _generation)
                return ToggleResult.Blocked;

            if (_active.TryGetValue(channel, out var current) && current.Key == key)
            {
                StopLocked(channel);
                result = ToggleResult.Stopped;
            }
            else
            {
                StopLocked(channel);
                try
                {
                    var handle = _factory.Start(channel, source);
                    var entry = new Active(key, handle);
                    entry.OnFinished = (_, _) => HandleFinished(channel, entry);
                    handle.Finished += entry.OnFinished;
                    _active[channel] = entry;
                    result = ToggleResult.Started;
                }
                catch (Exception ex)
                {
                    _log.Error("audio", $"No se pudo reproducir '{source}' ({channel})", ex);
                    result = ToggleResult.Blocked;
                }
            }
        }
        Changed?.Invoke();
        return result;
    }

    /// <summary>Pausa/reanuda solo la música activa, conservando su posición. Devuelve el estado de pausa resultante.</summary>
    public bool TogglePause()
    {
        bool paused;
        lock (_gate)
        {
            if (!_active.TryGetValue(AudioChannel.Music, out var music)) return false;
            try
            {
                if (_paused) { music.Handle.Resume(); _paused = false; }
                else { music.Handle.Pause(); _paused = true; }
            }
            catch (Exception ex) { _log.Error("audio", "Fallo al pausar/reanudar", ex); }
            paused = _paused;
        }
        Changed?.Invoke();
        return paused;
    }

    /// <summary>DETENER TODO: silencio total, invalida acciones pendientes. No reanuda nada solo.</summary>
    public void StopAll()
    {
        lock (_gate)
        {
            _generation++;
            foreach (var ch in Enum.GetValues<AudioChannel>()) StopLocked(ch);
            _paused = false;
        }
        Changed?.Invoke();
    }

    private void StopLocked(AudioChannel channel)
    {
        if (!_active.Remove(channel, out var a)) return;
        a.Handle.Finished -= a.OnFinished;
        try { a.Handle.Stop(); } catch (Exception ex) { _log.Error("audio", "Fallo al detener", ex); }
        try { a.Handle.Dispose(); } catch { }
        if (channel == AudioChannel.Music) _paused = false;
    }

    private void HandleFinished(AudioChannel channel, Active entry)
    {
        bool changed = false;
        lock (_gate)
        {
            if (_active.TryGetValue(channel, out var cur) && ReferenceEquals(cur, entry))
            {
                _active.Remove(channel);
                entry.Handle.Finished -= entry.OnFinished;
                if (channel == AudioChannel.Music) _paused = false;
                changed = true;
            }
        }
        if (changed)
        {
            try { entry.Handle.Dispose(); } catch { }
            Changed?.Invoke();
        }
    }
}
