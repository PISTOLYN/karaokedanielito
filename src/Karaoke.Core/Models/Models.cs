namespace Karaoke.Core.Models;

public enum Team { Red, Blue }

/// <summary>Botón celeste (asistente de voz). Configuración independiente por botón.</summary>
public sealed class VoiceButtonConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string SpokenText { get; set; } = "";
    public string? VoiceName { get; set; }
    public string? AudioFile { get; set; }
    public int RoundNumber { get; set; }
    public int TimeSeconds { get; set; } = 30;
    public string HeaderText { get; set; } = "";
    public string PanelText { get; set; } = "";
}

/// <summary>Botón blanco (música).</summary>
public sealed class MusicButtonConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public string? FilePath { get; set; }
    public int RoundNumber { get; set; }
    public string PanelText { get; set; } = "";
}

/// <summary>Botón verde (celebración).</summary>
public sealed class CelebrationConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = "CELEBRACION";
    public string? FilePath { get; set; }
}

/// <summary>Paleta tipo Jingle Palette: un conjunto completo de botones para un show.</summary>
public sealed class Palette
{
    public string Name { get; set; } = "Paleta";
    public List<VoiceButtonConfig> Voice { get; set; } = new();
    public List<MusicButtonConfig> Music { get; set; } = new();
    public List<CelebrationConfig> Celebrations { get; set; } = new();

    public static Palette CreateDefault(string name, int positions = 18)
    {
        var p = new Palette { Name = name };
        for (int i = 1; i <= positions; i++)
        {
            string header = i == 1 ? "¡Primera canción!" : $"¡Canción número {i}!";
            p.Voice.Add(new VoiceButtonConfig
            {
                Id = i, Name = $"{i} ALEXA", HeaderText = header,
                SpokenText = "Alexa. " + header, RoundNumber = i, TimeSeconds = 30,
                PanelText = "ADIVINA LA CANCION....."
            });
            p.Music.Add(new MusicButtonConfig
            {
                Id = i, Name = $"{i} CANCION", Title = $"Canción {i}", RoundNumber = i,
                PanelText = "ADIVINA LA CANCION....."
            });
        }
        for (int c = 1; c <= 4; c++)
            p.Celebrations.Add(new CelebrationConfig { Id = c, Name = "CELEBRACION" });
        return p;
    }
}
