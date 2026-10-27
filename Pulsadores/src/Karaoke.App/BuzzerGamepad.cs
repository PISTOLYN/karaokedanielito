using System.Runtime.InteropServices;
using Karaoke.Core.Logging;

namespace Karaoke.App;

/// <summary>
/// Lector de pulsadores/joysticks USB mediante winmm (joyGetPosEx). No necesita paquetes NuGet
/// y funciona igual en x86 y x64. Detecta cualquier gamepad HID que Windows muestre en
/// "Configurar controladores de juego USB" (como el DragonRise 0079:0006).
/// El evento ButtonPressed(dispositivo, botón) se dispara en un hilo de fondo; botón 0 = primer botón del mando.
/// </summary>
public sealed class BuzzerGamepad : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct JOYINFOEX
    {
        public int dwSize, dwFlags, dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos,
                   dwButtons, dwButtonNumber, dwPOV, dwReserved1, dwReserved2;
    }

    [DllImport("winmm.dll")]
    private static extern int joyGetPosEx(int joyId, ref JOYINFOEX info);

    private const int JOY_RETURNBUTTONS = 0x80;
    private const int MaxDevices = 16;

    private readonly IAppLogger _log;
    private readonly int[] _prev = new int[MaxDevices];
    private readonly bool[] _present = new bool[MaxDevices];
    private volatile bool _running;
    private volatile int _connected;
    private Thread? _thread;

    public event Action<int, int>? ButtonPressed;

    /// <summary>Cantidad de pulsadores/joysticks USB conectados en este momento.</summary>
    public int ConnectedCount => _connected;

    public BuzzerGamepad(IAppLogger log) { _log = log; }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Pulsadores" };
        _thread.Start();
    }

    private bool Poll(int id, out int buttons)
    {
        var info = new JOYINFOEX { dwSize = Marshal.SizeOf<JOYINFOEX>(), dwFlags = JOY_RETURNBUTTONS };
        buttons = 0;
        if (joyGetPosEx(id, ref info) != 0) return false;
        buttons = info.dwButtons;
        return true;
    }

    private void Loop()
    {
        int iteration = 0;
        try
        {
            while (_running)
            {
                bool fullScan = iteration++ % 400 == 0;   // cada ~1 s busca dispositivos nuevos
                int connected = 0;
                for (int id = 0; id < MaxDevices; id++)
                {
                    if (!fullScan && !_present[id]) continue;
                    if (!Poll(id, out int buttons))
                    {
                        if (_present[id]) _log.Info("pulsadores", $"Dispositivo {id} desconectado");
                        _present[id] = false; _prev[id] = 0;
                        continue;
                    }
                    if (!_present[id])
                    {
                        _present[id] = true;
                        _prev[id] = buttons;   // el estado inicial no cuenta como pulsación
                        _log.Info("pulsadores", $"Dispositivo {id} conectado");
                    }
                    connected++;

                    int pressed = buttons & ~_prev[id];
                    _prev[id] = buttons;
                    for (int b = 0; b < 32; b++)
                    {
                        if (((pressed >> b) & 1) == 0) continue;
                        try { ButtonPressed?.Invoke(id, b); }
                        catch (Exception ex) { _log.Error("pulsadores", "Error en el manejador del pulsador", ex); }
                    }
                }
                _connected = connected;
                Thread.Sleep(2);
            }
        }
        catch (Exception ex)
        {
            _log.Error("pulsadores", "El lector de pulsadores se detuvo por un error", ex);
        }
    }

    public void Dispose()
    {
        _running = false;
        try { _thread?.Join(500); } catch { }
        _thread = null;
    }
}
