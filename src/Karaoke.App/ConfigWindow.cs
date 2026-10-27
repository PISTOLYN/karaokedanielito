using System.IO;
using System.Windows;
using System.Windows.Controls;
using Karaoke.Core.Models;
using Microsoft.Win32;

namespace Karaoke.App;

/// <summary>
/// Ventana de configuración (parcial de la etapa 3): editar textos y asignar archivos a cada botón.
/// Los cambios se escriben directamente en los objetos de configuración; MainWindow los guarda al cerrar.
/// </summary>
public sealed class ConfigWindow : Window
{
    private const string AudioFilter = "Audio|*.mp3;*.wav;*.wma;*.m4a;*.aac;*.ogg;*.flac|Todos los archivos|*.*";
    private const string ImageFilter = "Imágenes|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Todos los archivos|*.*";

    private readonly AppSettings _s;
    private readonly Palette _p;

    public ConfigWindow(AppSettings settings, Palette palette, int startTab = 0)
    {
        _s = settings;
        _p = palette;
        Title = "Configuración — " + palette.Name;
        Width = 920;
        Height = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var tabs = new TabControl { Margin = new Thickness(8) };
        tabs.Items.Add(new TabItem { Header = "Botones de voz (celestes)", Content = BuildVoiceTab() });
        tabs.Items.Add(new TabItem { Header = "Música (blancos)", Content = BuildMusicTab() });
        tabs.Items.Add(new TabItem { Header = "Celebraciones (verdes)", Content = BuildCelebrationTab() });
        tabs.Items.Add(new TabItem { Header = "General", Content = BuildGeneralTab() });
        tabs.SelectedIndex = Math.Clamp(startTab, 0, 3);

        var close = new Button
        {
            Content = "Guardar y cerrar", Width = 170, Height = 36,
            Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right
        };
        close.Click += (_, _) => DialogResult = true;

        var root = new DockPanel();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(tabs);
        Content = root;
    }

    // ---------------------------------------------------------------- pestañas

    private UIElement BuildVoiceTab() =>
        BuildListDetail(_p.Voice, v => $"{v.Id}. {v.Name}", (f, v, changed) =>
        {
            AddText(f, "Nombre visible del botón", v.Name, t => { v.Name = t; changed(); });
            AddText(f, "Texto que pronunciará la voz", v.SpokenText, t => v.SpokenText = t, true);
            AddText(f, "Texto del encabezado (junto al nombre del asistente)", v.HeaderText, t => v.HeaderText = t);
            AddText(f, "Texto del panel central", v.PanelText, t => v.PanelText = t);
            AddInt(f, "Número de ronda", v.RoundNumber, n => v.RoundNumber = n);
            AddInt(f, "Tiempo asociado (segundos)", v.TimeSeconds, n => v.TimeSeconds = n);
            AddFile(f, "Archivo de audio opcional (si se asigna, se usa en lugar de la voz)", v.AudioFile,
                    AudioFilter, p => { v.AudioFile = p; changed(); });
        });

    private UIElement BuildMusicTab() =>
        BuildListDetail(_p.Music,
            m => $"{m.Id}. {m.Name}  —  {(string.IsNullOrWhiteSpace(m.FilePath) ? "(sin archivo)" : Path.GetFileName(m.FilePath))}",
            (f, m, changed) =>
            {
                AddText(f, "Nombre visible del botón", m.Name, t => { m.Name = t; changed(); });
                AddText(f, "Título (se muestra en el encabezado al sonar)", m.Title, t => m.Title = t);
                AddText(f, "Texto del panel central", m.PanelText, t => m.PanelText = t);
                AddInt(f, "Número de ronda", m.RoundNumber, n => m.RoundNumber = n);
                AddFile(f, "Archivo de música", m.FilePath, AudioFilter, p => { m.FilePath = p; changed(); });
            });

    private UIElement BuildCelebrationTab() =>
        BuildListDetail(_p.Celebrations,
            c => $"{c.Id}. {c.Name}  —  {(string.IsNullOrWhiteSpace(c.FilePath) ? "(sin archivo)" : Path.GetFileName(c.FilePath))}",
            (f, c, changed) =>
            {
                AddText(f, "Nombre visible del botón", c.Name, t => { c.Name = t; changed(); });
                AddFile(f, "Archivo de audio (MP3, WAV…)", c.FilePath, AudioFilter, p => { c.FilePath = p; changed(); });
            });

    private UIElement BuildGeneralTab()
    {
        var f = new StackPanel { Margin = new Thickness(14) };
        AddText(f, "Nombre del asistente de voz", _s.AssistantName, t => _s.AssistantName = t);
        AddInt(f, "Tiempo de ronda por defecto (segundos)", _s.DefaultRoundSeconds, n => _s.DefaultRoundSeconds = n);
        AddText(f, "Mensaje que corre en la barra blanca del panel central", _s.MarqueeText, t => _s.MarqueeText = t);

        var speak = new CheckBox { Content = "La calificación también dice la frase en voz alta", IsChecked = _s.SpeakRatingPhrase, Margin = new Thickness(0, 12, 0, 0) };
        speak.Click += (_, _) => _s.SpeakRatingPhrase = speak.IsChecked == true;
        f.Children.Add(speak);
        AddText(f, "Frases de calificación (una por línea; agrega todas las que quieras)",
                string.Join(Environment.NewLine, _s.RatingPhrases),
                t =>
                {
                    var lines = t.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (lines.Length > 0) _s.RatingPhrases = lines.ToList();
                }, true);

        var cb = new CheckBox { Content = "Pulsadores habilitados (cambia qué instrucciones se muestran)", IsChecked = _s.BuzzersEnabled, Margin = new Thickness(0, 12, 0, 0) };
        cb.Click += (_, _) => _s.BuzzersEnabled = cb.IsChecked == true;
        f.Children.Add(cb);

        AddText(f, "Instrucciones con pulsadores", _s.InstructionsWithBuzzers, t => _s.InstructionsWithBuzzers = t, true);
        AddText(f, "Instrucciones sin pulsadores", _s.InstructionsWithoutBuzzers, t => _s.InstructionsWithoutBuzzers = t, true);
        AddFile(f, "Logotipo izquierdo", _s.LogoLeftPath, ImageFilter, p => _s.LogoLeftPath = p);
        AddFile(f, "Logotipo derecho", _s.LogoRightPath, ImageFilter, p => _s.LogoRightPath = p);
        return new ScrollViewer { Content = f };
    }

    // ---------------------------------------------------------------- lista + formulario

    private static UIElement BuildListDetail<T>(IList<T> items, Func<T, string> label,
                                                Action<StackPanel, T, Action> buildForm)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var list = new ListBox { Margin = new Thickness(6) };
        foreach (var it in items) list.Items.Add(label(it));

        var form = new StackPanel { Margin = new Thickness(12) };
        var scroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1);

        bool updating = false;
        list.SelectionChanged += (_, _) =>
        {
            if (updating) return;
            form.Children.Clear();
            int i = list.SelectedIndex;
            if (i < 0 || i >= items.Count) return;
            var item = items[i];
            buildForm(form, item, () =>
            {
                // refrescar solo el texto de esa fila, sin reconstruir el formulario (así no se pierde el foco)
                updating = true;
                list.Items[i] = label(item);
                list.SelectedIndex = i;
                updating = false;
            });
        };

        grid.Children.Add(list);
        grid.Children.Add(scroll);
        if (items.Count > 0) list.SelectedIndex = 0;
        return grid;
    }

    // ---------------------------------------------------------------- campos

    private static void AddLabel(StackPanel f, string text) =>
        f.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 10, 0, 2), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });

    private static void AddText(StackPanel f, string label, string value, Action<string> set, bool multiline = false)
    {
        AddLabel(f, label);
        var tb = new TextBox { Text = value ?? "", Padding = new Thickness(4) };
        if (multiline) { tb.AcceptsReturn = true; tb.TextWrapping = TextWrapping.Wrap; tb.MinHeight = 70; }
        tb.TextChanged += (_, _) => set(tb.Text);
        f.Children.Add(tb);
    }

    private static void AddInt(StackPanel f, string label, int value, Action<int> set)
    {
        AddLabel(f, label);
        var tb = new TextBox { Text = value.ToString(), Width = 100, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(4) };
        tb.TextChanged += (_, _) => { if (int.TryParse(tb.Text, out int n) && n >= 0) set(n); };
        f.Children.Add(tb);
    }

    private static void AddFile(StackPanel f, string label, string? value, string filter, Action<string?> set)
    {
        AddLabel(f, label);
        var row = new DockPanel();
        var browse = new Button { Content = "Examinar…", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
        var clear = new Button { Content = "Quitar", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(clear, Dock.Right);
        DockPanel.SetDock(browse, Dock.Right);
        var tb = new TextBox { Text = value ?? "", Padding = new Thickness(4) };
        tb.TextChanged += (_, _) => set(string.IsNullOrWhiteSpace(tb.Text) ? null : tb.Text.Trim());
        browse.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = filter, CheckFileExists = true };
            if (dlg.ShowDialog() == true) tb.Text = dlg.FileName;
        };
        clear.Click += (_, _) => tb.Text = "";
        row.Children.Add(clear);
        row.Children.Add(browse);
        row.Children.Add(tb);
        f.Children.Add(row);
    }
}
