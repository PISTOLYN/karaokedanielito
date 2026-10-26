using Karaoke.Core.Audio;
using Karaoke.Core.Models;
using Karaoke.Core.Persistence;
using Karaoke.Core.Scoring;
using Karaoke.Core.Timing;
using Xunit;

namespace Karaoke.Core.Tests;

public sealed class FakeHandle : IPlaybackHandle
{
    public bool Stopped, Paused, Disposed;
    public event EventHandler? Finished;
    public void Stop() => Stopped = true;
    public void Pause() => Paused = true;
    public void Resume() => Paused = false;
    public void Dispose() => Disposed = true;
    public void RaiseFinished() => Finished?.Invoke(this, EventArgs.Empty);
}

public sealed class FakeFactory : IPlaybackFactory
{
    public List<FakeHandle> Handles = new();
    public bool Throw;
    public IPlaybackHandle Start(AudioChannel channel, string source)
    {
        if (Throw) throw new InvalidOperationException("boom");
        var h = new FakeHandle(); Handles.Add(h); return h;
    }
}

public class AudioCoordinatorTests
{
    private static (AudioCoordinator a, FakeFactory f) Make() { var f = new FakeFactory(); return (new AudioCoordinator(f), f); }

    [Fact] public void FirstPressStarts_SecondPressStops()
    {
        var (a, f) = Make();
        Assert.Equal(ToggleResult.Started, a.Toggle(AudioChannel.Music, "M1", "x"));
        Assert.Equal("M1", a.ActiveKey(AudioChannel.Music));
        Assert.Equal(ToggleResult.Stopped, a.Toggle(AudioChannel.Music, "M1", "x"));
        Assert.False(a.IsActive(AudioChannel.Music));
        Assert.True(f.Handles[0].Stopped);
    }

    [Fact] public void OtherMusicButton_ReplacesPrevious_NoOverlap()
    {
        var (a, f) = Make();
        a.Toggle(AudioChannel.Music, "M1", "x");
        a.Toggle(AudioChannel.Music, "M2", "y");
        Assert.True(f.Handles[0].Stopped);
        Assert.False(f.Handles[1].Stopped);
        Assert.Equal("M2", a.ActiveKey(AudioChannel.Music));
    }

    [Fact] public void VoiceAndMusic_AreIndependent()
    {
        var (a, f) = Make();
        a.Toggle(AudioChannel.Music, "M1", "x");
        a.Toggle(AudioChannel.Voice, "V1", "y");
        Assert.False(f.Handles[0].Stopped);
        Assert.True(a.IsActive(AudioChannel.Voice));
    }

    [Fact] public void StopAll_SilencesEverything_AndBlocksStalePendingActions()
    {
        var (a, f) = Make();
        long gen = a.Generation;
        a.Toggle(AudioChannel.Music, "M1", "x");
        a.Toggle(AudioChannel.Voice, "V1", "y");
        a.StopAll();
        Assert.All(f.Handles, h => Assert.True(h.Stopped));
        Assert.False(a.IsActive(AudioChannel.Music));
        Assert.Equal(ToggleResult.Blocked, a.Toggle(AudioChannel.Music, "M2", "z", gen));
        Assert.Equal(ToggleResult.Started, a.Toggle(AudioChannel.Music, "M2", "z", a.Generation));
    }

    [Fact] public void Pause_KeepsPlayback_NotStopped_AndResumes()
    {
        var (a, f) = Make();
        a.Toggle(AudioChannel.Music, "M1", "x");
        Assert.True(a.TogglePause());
        Assert.True(f.Handles[0].Paused);
        Assert.False(f.Handles[0].Stopped);
        Assert.False(a.TogglePause());
        Assert.False(f.Handles[0].Paused);
    }

    [Fact] public void Pause_WithoutMusic_DoesNothing() { var (a, _) = Make(); Assert.False(a.TogglePause()); }

    [Fact] public void StopAll_ClearsPause()
    {
        var (a, _) = Make();
        a.Toggle(AudioChannel.Music, "M1", "x"); a.TogglePause(); a.StopAll();
        Assert.False(a.IsPaused);
    }

    [Fact] public void Finished_RemovesActive()
    {
        var (a, f) = Make();
        a.Toggle(AudioChannel.Effect, "C1", "x");
        f.Handles[0].RaiseFinished();
        Assert.False(a.IsActive(AudioChannel.Effect));
    }

    [Fact] public void FactoryFailure_IsBlocked_NotThrown()
    {
        var (a, f) = Make(); f.Throw = true;
        Assert.Equal(ToggleResult.Blocked, a.Toggle(AudioChannel.Music, "M1", "x"));
        Assert.False(a.IsActive(AudioChannel.Music));
    }
}

public class ScoreboardTests
{
    [Fact] public void AddPoint_IsIndependentPerTeam()
    {
        var s = new Scoreboard(); s.AddPoint(Team.Red); s.AddPoint(Team.Red); s.AddPoint(Team.Blue);
        Assert.Equal(2, s.Red); Assert.Equal(1, s.Blue);
    }
    [Fact] public void Undo_RevertsLastPointOnly()
    {
        var s = new Scoreboard(); s.AddPoint(Team.Red); s.AddPoint(Team.Blue);
        Assert.True(s.UndoLast());
        Assert.Equal(1, s.Red); Assert.Equal(0, s.Blue);
    }
    [Fact] public void Undo_OnEmpty_ReturnsFalse() => Assert.False(new Scoreboard().UndoLast());
    [Fact] public void Reset_ZerosBoth()
    {
        var s = new Scoreboard(); s.AddPoint(Team.Red); s.Reset();
        Assert.Equal(0, s.Red); Assert.False(s.UndoLast());
    }
    [Fact] public void Changed_FiresOnAdd()
    {
        var s = new Scoreboard(); int n = 0; s.Changed += () => n++; s.AddPoint(Team.Blue);
        Assert.Equal(1, n);
    }
}

public class RoundTimerTests
{
    [Fact] public void SameRoundKey_WhileRunning_DoesNotRestart()
    {
        var t = new RoundTimer(); t.Start("R1", 10); t.Tick(4);
        Assert.False(t.Start("R1", 10));
        Assert.Equal(4, t.Elapsed, 3);
    }
    [Fact] public void DifferentRound_Restarts()
    {
        var t = new RoundTimer(); t.Start("R1", 10); t.Tick(4);
        Assert.True(t.Start("R2", 10));
        Assert.Equal(0, t.Elapsed);
    }
    [Fact] public void Expires_AndStops_AndFiresOnce()
    {
        var t = new RoundTimer(); int n = 0; t.Expired += () => n++;
        t.Start("R1", 2); t.Tick(1); t.Tick(1.5); t.Tick(5);
        Assert.False(t.IsRunning); Assert.Equal(1, n); Assert.Equal(0, t.Remaining, 3);
    }
    [Fact] public void Cancel_StopsWithoutExpiring()
    {
        var t = new RoundTimer(); int n = 0; t.Expired += () => n++;
        t.Start("R1", 5); t.Tick(1); t.Cancel(); t.Tick(10);
        Assert.False(t.IsRunning); Assert.Equal(0, n);
    }
}

public class SettingsTests
{
    [Fact] public void Defaults_Have18Positions_4Celebrations_And10Phrases()
    {
        var s = AppSettings.CreateDefault();
        Assert.Equal(18, s.ActivePalette.Voice.Count);
        Assert.Equal(18, s.ActivePalette.Music.Count);
        Assert.Equal(4, s.ActivePalette.Celebrations.Count);
        Assert.True(s.RatingPhrases.Count >= 10);
    }

    [Fact] public void SaveAndLoad_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "karaoke-test-" + Guid.NewGuid());
        var file = Path.Combine(dir, "settings.json");
        try
        {
            var s = AppSettings.CreateDefault(); s.AssistantName = "Lola"; s.BuzzersEnabled = true;
            JsonStore.Save(file, s);
            var loaded = JsonStore.LoadOrDefault(file, AppSettings.CreateDefault);
            Assert.Equal("Lola", loaded.AssistantName); Assert.True(loaded.BuzzersEnabled);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact] public void CorruptFile_FallsBackToDefaults_AndKeepsBackup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "karaoke-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "settings.json");
        try
        {
            File.WriteAllText(file, "{ esto no es json");
            var loaded = JsonStore.LoadOrDefault(file, AppSettings.CreateDefault);
            Assert.Equal("Alexa", loaded.AssistantName);
            Assert.Single(Directory.GetFiles(dir, "*dañado*"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
