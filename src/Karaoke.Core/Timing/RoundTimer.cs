namespace Karaoke.Core.Timing;

/// <summary>
/// Temporizador de ronda controlado por Tick(dt): sin hilos propios, por eso es determinista y probable.
/// Start con la misma clave de ronda mientras corre NO lo reinicia (la lectura de la pregunta no resetea el contador).
/// </summary>
public sealed class RoundTimer
{
    public string? RoundKey { get; private set; }
    public double Duration { get; private set; }
    public double Elapsed { get; private set; }
    public bool IsRunning { get; private set; }
    public double Remaining => Math.Max(0, Duration - Elapsed);
    public double Progress => Duration <= 0 ? 0 : Math.Min(1, Elapsed / Duration);
    public event Action? Expired;

    public bool Start(string roundKey, double seconds)
    {
        if (IsRunning && roundKey == RoundKey) return false;
        RoundKey = roundKey;
        Duration = Math.Max(0, seconds);
        Elapsed = 0;
        IsRunning = Duration > 0;
        return IsRunning;
    }

    public void Tick(double deltaSeconds)
    {
        if (!IsRunning) return;
        Elapsed += deltaSeconds;
        if (Elapsed >= Duration)
        {
            Elapsed = Duration;
            IsRunning = false;
            Expired?.Invoke();
        }
    }

    /// <summary>Usado por DETENER TODO. No reinicia nada automáticamente.</summary>
    public void Cancel()
    {
        IsRunning = false;
        Elapsed = 0;
        Duration = 0;
        RoundKey = null;
    }
}
