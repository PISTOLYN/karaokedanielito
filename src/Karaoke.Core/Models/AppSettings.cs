using System.Text.Json.Serialization;

namespace Karaoke.Core.Models;

public sealed class AppSettings
{
    public string AssistantName { get; set; } = "Alexa";
    public List<Palette> Palettes { get; set; } = new();
    public string ActivePaletteName { get; set; } = "";
    public List<string> RatingPhrases { get; set; } = new();
    public bool SpeakRatingPhrase { get; set; }
    public string MarqueeText { get; set; } = "ADIVINA LA CANCION.....";
    public bool BuzzersEnabled { get; set; }
    public string InstructionsWithBuzzers { get; set; } = DefaultInstructionsWithBuzzers;
    public string InstructionsWithoutBuzzers { get; set; } = DefaultInstructionsWithoutBuzzers;
    public int DefaultRoundSeconds { get; set; } = 30;
    public string? LogoLeftPath { get; set; }
    public string? LogoRightPath { get; set; }

    public const string DefaultInstructionsWithBuzzers =
        "¡Vamos a jugar Adivina la canción! Escuchen atentamente el fragmento musical. " +
        "Cuando crean saber qué canción es, corran a presionar el pulsador de su equipo y tomen el micrófono. " +
        "¡El primero en presionar tendrá el turno para cantar!";

    public const string DefaultInstructionsWithoutBuzzers =
        "¡Vamos a jugar Adivina la canción! Escuchen el fragmento musical. " +
        "Cuando sepan qué canción es, corran por el micrófono. " +
        "¡La primera persona que lo tome tendrá el turno para cantar con su equipo!";

    public static readonly string[] DefaultRatingPhrases =
    {
        "¡Cantaron bien, pero mejor dedíquense a otra cosa!",
        "¡Eso estuvo de otro mundo!",
        "¡La intención es lo que cuenta!",
        "¡Tenemos una estrella en el escenario!",
        "¡El micrófono sobrevivió, eso es lo importante!",
        "¡Que alguien les consiga un contrato!",
        "¡Casi nos hacen llorar, pero de emoción!",
        "¡El público pide otra canción!",
        "¡Hay talento, solo falta encontrarlo!",
        "¡Se ganaron un aplauso!"
    };

    [JsonIgnore]
    public Palette ActivePalette =>
        Palettes.FirstOrDefault(p => p.Name == ActivePaletteName) ?? Palettes[0];

    public static AppSettings CreateDefault()
    {
        var s = new AppSettings();
        s.Palettes.Add(Palette.CreateDefault("Paleta 1"));
        s.Palettes.Add(Palette.CreateDefault("Paleta 2"));
        s.RatingPhrases.AddRange(DefaultRatingPhrases);
        s.ActivePaletteName = s.Palettes[0].Name;
        return s;
    }

    /// <summary>Repara una configuración cargada de disco para que siempre sea utilizable.</summary>
    public void Normalize()
    {
        if (Palettes.Count == 0) Palettes.Add(Palette.CreateDefault("Paleta 1"));
        if (!Palettes.Any(p => p.Name == ActivePaletteName)) ActivePaletteName = Palettes[0].Name;
        if (RatingPhrases.Count == 0) RatingPhrases.AddRange(DefaultRatingPhrases);
        if (DefaultRoundSeconds <= 0) DefaultRoundSeconds = 30;
        if (string.IsNullOrWhiteSpace(MarqueeText)) MarqueeText = "ADIVINA LA CANCION.....";
    }
}
