using Karaoke.Core.Audio;
using Karaoke.Core.Logging;

namespace Karaoke.App;

/// <summary>
/// ETAPA 1: reproductor SIMULADO. No emite sonido; solo permite validar el estado de la interfaz
/// y la lógica del AudioCoordinator. La etapa 2 lo reemplaza por NAudio (música) y SAPI (voz).
/// </summary>
public sealed class SilentPlaybackFactory : IPlaybackFactory
{
    private readonly IAppLogger _log;
    public SilentPlaybackFactory(IAppLogger log) { _log = log; }

    public IPlaybackHandle Start(AudioChannel channel, string source)
    {
        _log.Info("audio-simulado", $"Inicio {channel}: {source}");
        return new SilentHandle();
    }

#pragma warning disable CS0067 // el evento Finished no se usa en el simulador
    private sealed class SilentHandle : IPlaybackHandle
    {
        public event EventHandler? Finished;
        public void Stop() { }
        public void Pause() { }
        public void Resume() { }
        public void Dispose() { }
    }
#pragma warning restore CS0067
}
