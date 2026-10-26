using System.Speech.Synthesis;
using Karaoke.Core.Audio;
using Karaoke.Core.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Karaoke.App;

/// <summary>
/// Reproducción real. Fuente "tts:texto" → voz de Windows (SAPI, sin Internet).
/// Cualquier otra fuente se trata como ruta de archivo (mp3, wav, wma, m4a, aac, flac...) vía NAudio.
/// Los niveles L/R salen de la señal real de los archivos; la voz sintética no se mide (SAPI no lo permite directamente).
/// </summary>
public sealed class NAudioPlaybackFactory : IPlaybackFactory
{
    private readonly IAppLogger _log;
    private readonly object _gate = new();
    private readonly List<FileHandle> _files = new();
    private string? _spanishVoice;
    private bool _voiceChecked;

    public NAudioPlaybackFactory(IAppLogger log) { _log = log; }

    /// <summary>Nombre de la voz en español usada, o null si no hay ninguna instalada.</summary>
    public string? SpanishVoice { get { EnsureVoice(); return _spanishVoice; } }

    public IPlaybackHandle Start(AudioChannel channel, string source)
    {
        if (source.StartsWith("tts:", StringComparison.Ordinal))
        {
            EnsureVoice();
            return new SpeechHandle(source.Substring(4), _spanishVoice, _log);
        }
        return new FileHandle(this, source);
    }

    /// <summary>Nivel pico actual (0..1) de los canales izquierdo y derecho, tomando el máximo de lo que suene.</summary>
    public (float Left, float Right) GetLevels()
    {
        lock (_gate)
        {
            float l = 0, r = 0;
            foreach (var f in _files) { l = Math.Max(l, f.Left); r = Math.Max(r, f.Right); }
            return (l, r);
        }
    }

    private void EnsureVoice()
    {
        if (_voiceChecked) return;
        _voiceChecked = true;
        try
        {
            using var s = new SpeechSynthesizer();
            var v = s.GetInstalledVoices()
                     .Where(x => x.Enabled)
                     .Select(x => x.VoiceInfo)
                     .FirstOrDefault(i => i.Culture.TwoLetterISOLanguageName == "es");
            _spanishVoice = v?.Name;
            if (_spanishVoice == null)
                _log.Warn("voz", "No hay voz en español instalada en Windows; se usará la voz predeterminada.");
            else
                _log.Info("voz", "Voz en español: " + _spanishVoice);
        }
        catch (Exception ex) { _log.Error("voz", "No se pudo consultar las voces instaladas", ex); }
    }

    // ------------------------------------------------------------ archivo de audio

    private sealed class FileHandle : IPlaybackHandle
    {
        private readonly NAudioPlaybackFactory _owner;
        private AudioFileReader? _reader;
        private WaveOutEvent? _out;
        private bool _stopRequested, _disposed;
        private volatile float _l, _r;

        public event EventHandler? Finished;
        public float Left => _l;
        public float Right => _r;

        public FileHandle(NAudioPlaybackFactory owner, string path)
        {
            _owner = owner;
            try
            {
                _reader = new AudioFileReader(path);
                var meter = new MeteringSampleProvider(_reader, Math.Max(1, _reader.WaveFormat.SampleRate / 20));
                meter.StreamVolume += OnVolume;
                _out = new WaveOutEvent();
                _out.Init(meter);
                _out.PlaybackStopped += OnStopped;
                lock (owner._gate) owner._files.Add(this);
                _out.Play();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void OnVolume(object? sender, StreamVolumeEventArgs e)
        {
            var v = e.MaxSampleValues;
            if (v == null || v.Length == 0) return;
            _l = v[0];
            _r = v.Length > 1 ? v[1] : v[0];
        }

        private void OnStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null) _owner._log.Error("audio", "La reproducción terminó con error", e.Exception);
            if (_stopRequested) return;
            Finished?.Invoke(this, EventArgs.Empty);
        }

        public void Stop()
        {
            _stopRequested = true;
            _l = _r = 0;
            try { _out?.Stop(); } catch (Exception ex) { _owner._log.Error("audio", "Fallo al detener", ex); }
        }

        public void Pause()
        {
            try { _out?.Pause(); } catch (Exception ex) { _owner._log.Error("audio", "Fallo al pausar", ex); }
            _l = _r = 0;
        }

        public void Resume()
        {
            try { _out?.Play(); } catch (Exception ex) { _owner._log.Error("audio", "Fallo al reanudar", ex); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _l = _r = 0;
            lock (_owner._gate) _owner._files.Remove(this);
            try { if (_out != null) _out.PlaybackStopped -= OnStopped; } catch { }
            try { _out?.Dispose(); } catch { }
            try { _reader?.Dispose(); } catch { }
        }
    }

    // ------------------------------------------------------------ voz sintética

    private sealed class SpeechHandle : IPlaybackHandle
    {
        private SpeechSynthesizer? _synth;
        private volatile bool _stopped;
        public event EventHandler? Finished;

        public SpeechHandle(string text, string? voiceName, IAppLogger log)
        {
            _synth = new SpeechSynthesizer();
            try { if (voiceName != null) _synth.SelectVoice(voiceName); }
            catch (Exception ex) { log.Warn("voz", "No se pudo seleccionar la voz '" + voiceName + "': " + ex.Message); }
            _synth.SetOutputToDefaultAudioDevice();
            _synth.SpeakCompleted += (_, _) => { if (!_stopped) Finished?.Invoke(this, EventArgs.Empty); };
            _synth.SpeakAsync(text);
        }

        public void Stop()
        {
            _stopped = true;
            try { _synth?.SpeakAsyncCancelAll(); } catch { }
        }

        public void Pause() { }   // la pausa solo aplica a la música
        public void Resume() { }

        public void Dispose()
        {
            _stopped = true;
            try { _synth?.Dispose(); } catch { }
            _synth = null;
        }
    }
}
