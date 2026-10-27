using Karaoke.Core.Models;

namespace Karaoke.Core.Scoring;

/// <summary>Marcador rojo/azul. Solo cambia cuando el conductor (o el pulsador) llama a AddPoint/AddPoints.</summary>
public sealed class Scoreboard
{
    private readonly Stack<(Team Team, int Amount)> _history = new();
    public int Red { get; private set; }
    public int Blue { get; private set; }
    public event Action? Changed;

    public int Get(Team t) => t == Team.Red ? Red : Blue;

    public void AddPoint(Team t) => AddPoints(t, 1);

    /// <summary>Suma varios puntos de una vez (por ejemplo la calificación). Deshacer revierte el total completo.</summary>
    public void AddPoints(Team t, int amount)
    {
        if (amount <= 0) return;
        if (t == Team.Red) Red += amount; else Blue += amount;
        _history.Push((t, amount));
        Changed?.Invoke();
    }

    public bool UndoLast()
    {
        if (!_history.TryPop(out var last)) return false;
        if (last.Team == Team.Red) Red -= last.Amount; else Blue -= last.Amount;
        Changed?.Invoke();
        return true;
    }

    public void Reset()
    {
        Red = 0; Blue = 0; _history.Clear();
        Changed?.Invoke();
    }
}
