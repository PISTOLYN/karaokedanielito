using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Karaoke.Core.Audio;
using Karaoke.Core.Models;

namespace Karaoke.App;

/// <summary>Pulsadores arcade rojo/azul. Parte de MainWindow (archivo aparte para no tocar el resto del código).</summary>
public partial class MainWindow
{
    private const bool BuzzerAwardsPoint = true;   // false = el pulsador solo da el turno; el punto lo da el conductor
    private const int BuzzerLockSeconds = 0;       // 0 = el turno queda bloqueado hasta pulsar REINICIAR PULSADORES (o DETENER TODO)
    private const int RedButtonIndex = 0;          // botón 0 del mando = equipo rojo
    private const int BlueButtonIndex = 1;         // botón 1 del mando = equipo azul

    private BuzzerGamepad? _gamepad;
    private Team? _turn;
    private bool _buzzerTestMode;
    private readonly DispatcherTimer _turnTimer = new();
    private readonly DispatcherTimer _blinkTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Border? _blinkPanel;
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

    private void OnGamepadButton(int device, int button)
    {
        _log.Info("pulsadores", $"Pulsación: dispositivo {device}, botón {button}");

        if (_buzzerTestMode)
        {
            SetPanel($"PULSADOR DETECTADO: dispositivo {device}, botón {button}");
            return;
        }
        if (!_settings.BuzzersEnabled) return;   // pulsadores deshabilitados: se ignoran
        if (_turn != null) return;               // ya hay turno: gana el primer evento válido

        Team team;
        if (button == RedButtonIndex) team = Team.Red;
        else if (button == BlueButtonIndex) team = Team.Blue;
        else return;

        _turn = team;
        if (BuzzerAwardsPoint) _score.AddPoint(team);
        BlinkPanel(team == Team.Red ? RedPanel : BluePanel);

        string text = team == Team.Red ? "¡Turno para el equipo rojo!" : "¡Turno para el equipo azul!";
        SetPanel(text);
        // Clave única en cada llamada: nunca se interpreta como "segunda pulsación = detener"
        _audio.Toggle(AudioChannel.Voice, "BUZZER" + Environment.TickCount64, "tts:" + text);

        if (BuzzerLockSeconds > 0)
        {
            _turnTimer.Interval = TimeSpan.FromSeconds(BuzzerLockSeconds);
            _turnTimer.Start();
        }
    }

    private void ResetBuzzers_Click(object sender, RoutedEventArgs e)
    {
        EndTurn();
        _log.Info("pulsadores", "Pulsadores reiniciados por el conductor");
        SetPanel("Pulsadores reiniciados: los equipos pueden volver a pulsar");
    }

    /// <summary>Libera el turno. También lo llama DETENER TODO.</summary>
    private void EndTurn()
    {
        _turnTimer.Stop();
        _turn = null;
        StopBlink();
    }

    private void BlinkPanel(Border panel)
    {
        StopBlink();
        _blinkPanel = panel;
        _blinkCount = 0;
        _blinkTimer.Start();
    }

    private void OnBlinkTick(object? sender, EventArgs e)
    {
        if (_blinkPanel == null) return;
        _blinkCount++;
        _blinkPanel.Opacity = _blinkCount % 2 == 0 ? 1.0 : 0.25;
    }

    private void StopBlink()
    {
        _blinkTimer.Stop();
        if (_blinkPanel != null) _blinkPanel.Opacity = 1.0;
        _blinkPanel = null;
    }
}
