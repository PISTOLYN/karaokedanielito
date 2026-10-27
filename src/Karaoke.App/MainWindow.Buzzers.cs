using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Karaoke.Core.Audio;
using Karaoke.Core.Models;

namespace Karaoke.App;

/// <summary>
/// Pulsadores arcade rojo/azul. Parte de MainWindow (archivo aparte para no tocar el resto del código).
/// El primer equipo que pulsa gana el turno: suena su sonido, su cuadro parpadea y el otro queda inhabilitado
/// hasta REINICIAR PULSADORES (o DETENER TODO). Qué botón es de cada equipo, el sonido y demás se configuran
/// en Configuración → "Pulsadores (equipos)".
/// </summary>
public partial class MainWindow
{
    private const double DisabledPanelOpacity = 0.4;   // cuadro del equipo que perdió el turno

    private BuzzerGamepad? _gamepad;
    private Team? _turn;
    private bool _buzzerTestMode;
    private readonly DispatcherTimer _turnTimer = new();
    private readonly DispatcherTimer _blinkTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Border? _blinkPanel;
    private Brush? _blinkOriginalBorder;
    private int _blinkCount;

    /// <summary>Llamar una vez desde el constructor, después de _tick.Start().</summary>
    private void InitBuzzers()
    {
        _turnTimer.Tick += (_, _) => EndTurn();
        _blinkTimer.Tick += OnBlinkTick;
        PreviewKeyDown += OnBuzzerPreviewKeyDown;
        try
        {
            _gamepad = new BuzzerGamepad(_log);
            // El evento llega en un hilo de fondo; Dispatcher lo pasa al hilo de la interfaz respetando el orden de llegada
            _gamepad.ButtonPressed += (device, button) =>
                Dispatcher.BeginInvoke(new Action(() => OnGamepadButton(device, button)));
            _gamepad.Start();
            ConfigWindow.Gamepad = _gamepad;   // la pestaña "Pulsadores (equipos)" lo usa para detectar qué botón se presiona
        }
        catch (Exception ex)
        {
            _log.Error("pulsadores", "No se pudo iniciar el lector de pulsadores", ex);
        }
    }

    /// <summary>Llamar desde Window_Closing, ya confirmada la salida.</summary>
    private void DisposeBuzzers()
    {
        _turnTimer.Stop();
        StopBlink();
        ConfigWindow.Gamepad = null;
        _gamepad?.Dispose();
        _gamepad = null;
    }

    // F9 = modo prueba: muestra en el panel qué botón se presionó (no da puntos ni turnos)
    private void OnBuzzerPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F9) return;
        _buzzerTestMode = !_buzzerTestMode;
        if (_buzzerTestMode)
        {
            int n = _gamepad?.ConnectedCount ?? 0;
            SetPanel(n > 0 ? $"MODO PRUEBA: {n} pulsador(es) USB detectado(s). Presiona un botón."
                           : "MODO PRUEBA: no se detecta ningún pulsador USB");
        }
        else SetPanel("Modo prueba de pulsadores desactivado");
        e.Handled = true;
    }

    /// <summary>Equipo al que pertenece el botón presionado según la configuración, o null si no es de ningún equipo.</summary>
    private Team? WhichTeam(int device, int button)
    {
        if (button == _settings.RedBuzzerButton &&
            (_settings.RedBuzzerDevice < 0 || _settings.RedBuzzerDevice == device)) return Team.Red;
        if (button == _settings.BlueBuzzerButton &&
            (_settings.BlueBuzzerDevice < 0 || _settings.BlueBuzzerDevice == device)) return Team.Blue;
        return null;
    }

    private void OnGamepadButton(int device, int button)
    {
        _log.Info("pulsadores", $"Pulsación: dispositivo {device}, botón {button}");

        // Con la ventana de Configuración abierta no se juega: ahí los botones sirven para asignarlos
        if (OwnedWindows.OfType<ConfigWindow>().Any()) return;

        if (_buzzerTestMode)
        {
            SetPanel($"PULSADOR DETECTADO: dispositivo {device}, botón {button}");
            return;
        }
        if (!_settings.BuzzersEnabled) return;   // pulsadores deshabilitados: se ignoran
        if (_turn != null) return;               // ya hay turno: el otro equipo queda inhabilitado

        if (WhichTeam(device, button) is not Team team) return;

        _turn = team;
        if (_settings.BuzzerAwardsPoint) _score.AddPoint(team);

        var winner = team == Team.Red ? RedPanel : BluePanel;
        var loser = team == Team.Red ? BluePanel : RedPanel;
        loser.Opacity = DisabledPanelOpacity;   // el otro equipo queda inhabilitado (atenuado)
        BlinkPanel(winner);                     // el cuadro del ganador parpadea

        string text = team == Team.Red ? "¡Turno para el equipo rojo!" : "¡Turno para el equipo azul!";
        SetPanel(text);
        PlayBuzzerSound(team, text);

        int lockSeconds = _settings.BuzzerLockSeconds;
        if (lockSeconds > 0)
        {
            _turnTimer.Interval = TimeSpan.FromSeconds(lockSeconds);
            _turnTimer.Start();
        }
    }

    /// <summary>Suena el archivo del equipo (si hay) y/o la voz. Claves únicas: nunca se interpreta como "segunda pulsación = detener".</summary>
    private void PlayBuzzerSound(Team team, string spokenText)
    {
        string? file = team == Team.Red ? _settings.RedBuzzerSound : _settings.BlueBuzzerSound;
        bool hasFile = !string.IsNullOrWhiteSpace(file) && File.Exists(file);

        if (hasFile)
        {
            var r = _audio.Toggle(AudioChannel.Effect, "BUZZERFX" + Environment.TickCount64, file!);
            if (r == ToggleResult.Blocked)
                _log.Warn("pulsadores", "No se pudo reproducir el sonido del pulsador: " + file);
        }
        else if (!string.IsNullOrWhiteSpace(file))
        {
            _log.Warn("pulsadores", "El sonido del pulsador no existe, se usará la voz: " + file);
        }

        if (!hasFile || _settings.BuzzerSpeakTurn)
            _audio.Toggle(AudioChannel.Voice, "BUZZER" + Environment.TickCount64, "tts:" + spokenText);
    }

    private void ResetBuzzers_Click(object sender, RoutedEventArgs e)
    {
        EndTurn();
        _log.Info("pulsadores", "Pulsadores reiniciados por el conductor");
        SetPanel("Pulsadores reiniciados: los equipos pueden volver a pulsar");
    }

    /// <summary>Libera el turno y rehabilita a los dos equipos. También lo llama DETENER TODO.</summary>
    private void EndTurn()
    {
        _turnTimer.Stop();
        _turn = null;
        StopBlink();
        RedPanel.Opacity = 1.0;
        BluePanel.Opacity = 1.0;
    }

    private void BlinkPanel(Border panel)
    {
        StopBlink();
        _blinkPanel = panel;
        _blinkOriginalBorder = panel.BorderBrush;
        _blinkCount = 0;
        panel.Opacity = 1.0;
        panel.BorderBrush = Brushes.Gold;
        _blinkTimer.Start();
    }

    private void OnBlinkTick(object? sender, EventArgs e)
    {
        if (_blinkPanel == null) return;
        _blinkCount++;
        bool on = _blinkCount % 2 == 0;
        _blinkPanel.Opacity = on ? 1.0 : 0.35;
        _blinkPanel.BorderBrush = on ? Brushes.Gold : _blinkOriginalBorder;
    }

    private void StopBlink()
    {
        _blinkTimer.Stop();
        if (_blinkPanel != null)
        {
            _blinkPanel.Opacity = 1.0;
            _blinkPanel.BorderBrush = _blinkOriginalBorder;
        }
        _blinkPanel = null;
        _blinkOriginalBorder = null;
    }
}
