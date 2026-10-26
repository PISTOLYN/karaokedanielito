using Karaoke.Core.Models;

namespace Karaoke.Core.Scoring;

/// <summary>Marcador rojo/azul. Solo cambia cuando el conductor llama a AddPoint.</summary>
public sealed class Scoreboard
{
    private readonly Stack<Team> _history = new();
    public int Red { get; private set; }
    public int Blue { get; private set; }
    public event Action? Changed;

    public int Get(Team t) => t == Team.Red ? Red : Blue;

    public void AddPoint(Team t)
    {
        if (t == Team.Red) Red++; else Blue++;
        _history.Push(t);
        Changed?.Invoke();
    }

    public bool UndoLast()
    {
        if (!_history.TryPop(out var t)) return false;
        if (t == Team.Red) Red--; else Blue--;
        Changed?.Invoke();
        return true;
    }

    public void Reset()
    {
        Red = 0; Blue = 0; _history.Clear();
        Changed?.Invoke();
    }
}
