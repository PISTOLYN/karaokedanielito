using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Karaoke.App;

/// <summary>Barra blanca con mensaje que corre hacia la izquierda, y cronómetro de la música (00:00).</summary>
public partial class MainWindow
{
    private const double MarqueeSpeed = 60;   // píxeles por segundo (más bajo = más lento)
    private readonly TranslateTransform _marqueeShift = new();
    private readonly Stopwatch _musicSw = new();
    private string? _lastMusicKey;

    private void InitExtras()
    {
        PanelTitle.RenderTransform = _marqueeShift;
        MarqueeHost.SizeChanged += (_, _) =>
        {
            PanelTitle.FontSize = Math.Max(14, MarqueeHost.ActualHeight * 0.7);
            RestartMarquee();
        };
        SetPanel(_settings.MarqueeText);
    }

    /// <summary>Reinicia el desplazamiento del texto de la barra con el texto actual.</summary>
    private void RestartMarquee()
    {
        double host = MarqueeHost.ActualWidth;
        if (host <= 0) return;   // todavía no hay diseño; SizeChanged lo vuelve a llamar
        PanelTitle.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = PanelTitle.DesiredSize.Width, h = PanelTitle.DesiredSize.Height;
        Canvas.SetTop(PanelTitle, Math.Max(0, (MarqueeHost.ActualHeight - h) / 2));
        var anim = new DoubleAnimation(host, -w, TimeSpan.FromSeconds((host + w) / MarqueeSpeed))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        _marqueeShift.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    /// <summary>El 00:00 corre desde que inicia una canción, se congela en pausa y vuelve a 0 al detenerla.</summary>
    private void UpdateMusicClock(string? musicKey)
    {
        if (musicKey != _lastMusicKey)
        {
            _lastMusicKey = musicKey;
            if (musicKey == null) _musicSw.Reset(); else _musicSw.Restart();
        }
        if (musicKey != null)
        {
            if (_audio.IsPaused) _musicSw.Stop();
            else if (!_musicSw.IsRunning) _musicSw.Start();
        }
    }
}
