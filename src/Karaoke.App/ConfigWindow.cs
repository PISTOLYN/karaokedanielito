using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

    /// <summary>Lector de pulsadores de la ventana principal; lo asigna MainWindow.InitBuzzers. Sirve para detectar qué botón se presiona al asignarlo.</summary>
    public static BuzzerGamepad? Gamepad { get; set; }

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
        tabs.Items.Add(new TabItem { Header = "Pulsadores (equipos)", Content = BuildBuzzerTab() });
        tabs.SelectedIndex = Math.Clamp(startTab, 0, tabs.Items.Count - 1);

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

        // "Pulsadores habilitados" ahora está en la pestaña "Pulsadores (equipos)".
        AddText(f, "Instrucciones con pulsadores", _s.InstructionsWithBuzzers, t => _s.InstructionsWithBuzzers = t, true);
        AddText(f, "Instrucciones sin pulsadores", _s.InstructionsWithoutBuzzers, t => _s.InstructionsWithoutBuzzers = t, true);
        AddFile(f, "Logotipo izquierdo", _s.LogoLeftPath, ImageFilter, p => _s.LogoLeftPath = p);
        AddFile(f, "Logotipo derecho", _s.LogoRightPath, ImageFilter, p => _s.LogoRightPath = p);
        return new ScrollViewer { Content = f };
    }

    private UIElement BuildBuzzerTab()
    {
        var f = new StackPanel { Margin = new Thickness(14) };

        var enabled = new CheckBox
        {
            Content = "Pulsadores habilitados (si se desactivan, se ignoran y se muestran las instrucciones sin pulsadores)",
            IsChecked = _s.BuzzersEnabled
        };
        enabled.Click += (_, _) => _s.BuzzersEnabled = enabled.IsChecked == true;
        f.Children.Add(enabled);

        f.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Text = "El primer equipo que presione su pulsador gana el turno: suena su sonido, su cuadro parpadea y el pulsador del otro equipo " +
                   "queda inhabilitado (su cuadro se atenúa) hasta que presiones REINICIAR PULSADORES o DETENER TODO."
        });

        var monitor = new TextBlock { Margin = new Thickness(0, 10, 0, 0), Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap };
        var status = new TextBlock { Margin = new Thickness(0, 6, 0, 0), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var warn = new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0), Foreground = Brushes.Firebrick, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed,
            Text = "⚠ Los dos equipos tienen asignado el mismo pulsador: siempre ganaría el rojo. Asigna un botón distinto a cada equipo."
        };
        var redInfo = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var blueInfo = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };

        static string Describe(int device, int button) =>
            device < 0 ? $"cualquier dispositivo, botón {button}" : $"dispositivo {device}, botón {button}";

        void Refresh()
        {
            redInfo.Text = "Asignado: " + Describe(_s.RedBuzzerDevice, _s.RedBuzzerButton);
            blueInfo.Text = "Asignado: " + Describe(_s.BlueBuzzerDevice, _s.BlueBuzzerButton);
            bool sameButton = _s.RedBuzzerButton == _s.BlueBuzzerButton &&
                              (_s.RedBuzzerDevice < 0 || _s.BlueBuzzerDevice < 0 || _s.RedBuzzerDevice == _s.BlueBuzzerDevice);
            warn.Visibility = sameButton ? Visibility.Visible : Visibility.Collapsed;
        }

        Team? learning = null;

        void StartLearn(Team team)
        {
            if (Gamepad == null)
            {
                status.Text = "No se pudo iniciar el lector de pulsadores USB (revisa el registro).";
                return;
            }
            learning = team;
            status.Text = $"Presiona ahora el pulsador del equipo {(team == Team.Red ? "ROJO" : "AZUL")}…";
        }

        Button MakeLearnButton(string text, Team team)
        {
            var b = new Button { Content = text, Padding = new Thickness(12, 5, 12, 5), MinWidth = 220 };
            b.Click += (_, _) => StartLearn(team);
            return b;
        }

        Button MakeResetButton(Team team)
        {
            var b = new Button { Content = "Valores por defecto", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(8, 0, 0, 0) };
            b.Click += (_, _) =>
            {
                if (team == Team.Red) { _s.RedBuzzerDevice = -1; _s.RedBuzzerButton = 0; }
                else { _s.BlueBuzzerDevice = -1; _s.BlueBuzzerButton = 1; }
                learning = null;
                status.Text = "";
                Refresh();
            };
            return b;
        }

        // ---- asignación de los 2 pulsadores
        AddLabel(f, "Pulsador del EQUIPO ROJO");
        var redRow = new StackPanel { Orientation = Orientation.Horizontal };
        redRow.Children.Add(MakeLearnButton("Asignar (presiona el pulsador rojo)", Team.Red));
        redRow.Children.Add(MakeResetButton(Team.Red));
        redRow.Children.Add(redInfo);
        f.Children.Add(redRow);

        AddLabel(f, "Pulsador del EQUIPO AZUL");
        var blueRow = new StackPanel { Orientation = Orientation.Horizontal };
        blueRow.Children.Add(MakeLearnButton("Asignar (presiona el pulsador azul)", Team.Blue));
        blueRow.Children.Add(MakeResetButton(Team.Blue));
        blueRow.Children.Add(blueInfo);
        f.Children.Add(blueRow);

        f.Children.Add(status);
        f.Children.Add(warn);
        f.Children.Add(monitor);
        Refresh();

        // Detección en vivo: el evento llega en un hilo de fondo, se pasa al hilo de la interfaz
        var gp = Gamepad;
        Action<int, int>? onPress = null;
        if (gp != null)
        {
            monitor.Text = gp.ConnectedCount > 0
                ? $"Pulsadores USB detectados: {gp.ConnectedCount}. Último botón presionado: (ninguno todavía)"
                : "No se detecta ningún pulsador USB. Conéctalo y presiona un botón para comprobarlo.";
            onPress = (device, button) => Dispatcher.BeginInvoke(new Action(() =>
            {
                monitor.Text = $"Último botón presionado: dispositivo {device}, botón {button}";
                if (learning is not Team team) return;
                if (team == Team.Red) { _s.RedBuzzerDevice = device; _s.RedBuzzerButton = button; }
                else { _s.BlueBuzzerDevice = device; _s.BlueBuzzerButton = button; }
                learning = null;
                status.Text = $"Listo: pulsador del equipo {(team == Team.Red ? "rojo" : "azul")} asignado ({Describe(device, button)}).";
                Refresh();
            }));
            gp.ButtonPressed += onPress;
            Closed += (_, _) => gp.ButtonPressed -= onPress;
        }
        else
        {
            monitor.Text = "No se pudo iniciar el lector de pulsadores USB, por eso no se pueden asignar presionando el botón.";
        }

        // ---- sonidos y comportamiento
        AddFile(f, "Sonido cuando gana el turno el EQUIPO ROJO (opcional)", _s.RedBuzzerSound, AudioFilter, p => _s.RedBuzzerSound = p);
        AddFile(f, "Sonido cuando gana el turno el EQUIPO AZUL (opcional)", _s.BlueBuzzerSound, AudioFilter, p => _s.BlueBuzzerSound = p);

        var speak = new CheckBox
        {
            Content = "Además, la voz dice «¡Turno para el equipo rojo/azul!» (si no hay sonido asignado, siempre se usa la voz)",
            IsChecked = _s.BuzzerSpeakTurn, Margin = new Thickness(0, 12, 0, 0)
        };
        speak.Click += (_, _) => _s.BuzzerSpeakTurn = speak.IsChecked == true;
        f.Children.Add(speak);

        var point = new CheckBox
        {
            Content = "El pulsador suma 1 punto al equipo que gana el turno",
            IsChecked = _s.BuzzerAwardsPoint, Margin = new Thickness(0, 8, 0, 0)
        };
        point.Click += (_, _) => _s.BuzzerAwardsPoint = point.IsChecked == true;
        f.Children.Add(point);

        AddInt(f, "Bloqueo automático en segundos (0 = el turno queda bloqueado hasta presionar REINICIAR PULSADORES)",
               _s.BuzzerLockSeconds, n => _s.BuzzerLockSeconds = n);

        f.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 14, 0, 0), Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap,
            Text = "Consejo: en la pantalla principal, F9 activa el modo prueba para ver qué botón manda cada pulsador."
        });

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
