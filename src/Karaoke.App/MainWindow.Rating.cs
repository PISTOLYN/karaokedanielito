using System.Windows;
using System.Windows.Threading;
using Karaoke.Core.Audio;
using Karaoke.Core.Models;

namespace Karaoke.App;

/// <summary>Calificación aleatoria estilo rockola: contador 0-10 que se frena solo y espera a que el conductor dé el punto.</summary>
public partial class MainWindow
{
    /// <summary>true: PUNTO suma el número de la calificación al equipo (se va acumulando). false: suma solo 1.</summary>
    private const bool RatingGivesScoreValue = true;

    private readonly DispatcherTimer _ratingTimer = new();
    private readonly Random _ratingRng = new();
    private List<int> _ratingSeq = new();
    private int _ratingStep;
    private bool _ratingSpinning;
    private int? _ratingResult;

    private void StartRating()
    {
        if (_ratingSpinning) return;   // ya está girando
        _ratingTimer.Stop();
        _ratingTimer.Tick -= OnRatingTick;
        _ratingTimer.Tick += OnRatingTick;

        int target = _ratingRng.Next(0, 11);   // 0..10, ambos incluidos
        _ratingSeq = new List<int>();
        for (int c = 0; c < 3; c++)
            for (int n = 0; n <= 10; n++) _ratingSeq.Add(n);
        for (int n = 0; n <= target; n++) _ratingSeq.Add(n);

        _ratingStep = 0;
        _ratingResult = null;
        _ratingSpinning = true;
        RatingPhrase.Text = "";
        RatingHint.Text = "Calificando…";
        RatingOverlay.Visibility = Visibility.Visible;
        ShowRatingStep();
        _ratingTimer.Interval = StepDelay(0);
        _ratingTimer.Start();
    }

    private TimeSpan StepDelay(int step)
    {
        // Rápido al principio y frenando en los últimos 12 números, como el contador de una rockola
        int slow = 12, k = step - (_ratingSeq.Count - slow);
        return TimeSpan.FromMilliseconds(55 + (k > 0 ? 3 * k * k : 0));
    }

    private void ShowRatingStep() => RatingNumber.Text = _ratingSeq[_ratingStep].ToString();

    private void OnRatingTick(object? sender, EventArgs e)
    {
        _ratingStep++;
        ShowRatingStep();
        if (_ratingStep >= _ratingSeq.Count - 1) { FinishRating(); return; }
        _ratingTimer.Interval = StepDelay(_ratingStep);
    }

    private void FinishRating()
    {
        _ratingTimer.Stop();
        _ratingSpinning = false;
        int result = _ratingSeq[_ratingSeq.Count - 1];
        _ratingResult = result;

        string phrase = _settings.RatingPhrases.Count > 0
            ? _settings.RatingPhrases[_ratingRng.Next(_settings.RatingPhrases.Count)]
            : "";
        RatingPhrase.Text = phrase;
        RatingHint.Text = RatingGivesScoreValue
            ? $"Presiona PUNTO rojo o PUNTO azul para darle {result} punto(s) al equipo"
            : "Presiona PUNTO rojo o PUNTO azul para darle 1 punto al equipo";
        _log.Info("calificacion", $"Resultado {result}: {phrase}");

        if (_settings.SpeakRatingPhrase && phrase.Length > 0)
            _audio.Toggle(AudioChannel.Voice, "RATING" + Environment.TickCount64, "tts:" + phrase);
    }

    /// <summary>Cierra la ventana sin dar puntos. También lo usa DETENER TODO.</summary>
    private void CancelRating()
    {
        _ratingTimer.Stop();
        _ratingSpinning = false;
        _ratingResult = null;
        RatingOverlay.Visibility = Visibility.Collapsed;
    }

    private void RatingClose_Click(object sender, RoutedEventArgs e) => CancelRating();

    /// <summary>Botones PUNTO. Con una calificación pendiente le dan ese resultado al equipo; si no, suman 1.</summary>
    private void GivePoints(Team team)
    {
        if (_ratingSpinning) return;   // hay que esperar a que el contador se detenga
        string name = team == Team.Red ? "rojo" : "azul";
        if (_ratingResult is int result)
        {
            int amount = RatingGivesScoreValue ? result : 1;
            _score.AddPoints(team, amount);
            _log.Info("calificacion", $"{amount} punto(s) para el equipo {name}");
            CancelRating();
            SetPanel($"{amount} punto(s) para el equipo {name}");
            return;
        }
        _score.AddPoint(team);
    }
}
