using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Karaoke.Core.Audio;
using Karaoke.Core.Logging;
using Karaoke.Core.Models;
using Karaoke.Core.Persistence;
using Karaoke.Core.Scoring;
using Karaoke.Core.Timing;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Karaoke.App;

public partial class MainWindow : Window
{
    /// <summary>Etapa 2: audio real. Con false, un botón sin archivo válido no suena y avisa en el panel.</summary>
    private const bool DemoMode = false;
    private readonly NAudioPlaybackFactory _playback;
    private double _dispL, _dispR;

    private readonly AppPaths _paths = new();
    private readonly IAppLogger _log;
    private readonly AppSettings _settings;
    private Palette _palette;
    private readonly Scoreboard _score = new();
    private readonly AudioCoordinator _audio;
    private readonly RoundTimer _timer = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private readonly Dictionary<string, Button> _buttons = new();
    private readonly List<Rectangle> _eqBars = new();
    private readonly Random _rng = new();
    private readonly CultureInfo _es = CultureInfo.GetCultureInfo("es-MX");
    private bool _exitConfirmed, _fullscreen, _updatingList;
    private WindowState _prevState;
    private WindowStyle _prevStyle;
    private ResizeMode _prevResize;

    public MainWindow()
    {
        InitializeComponent();
        _paths.EnsureCreated();
        _log = new FileLogger(_paths.LogsDir);
        _settings = JsonStore.LoadOrDefault(_paths.SettingsFile, AppSettings.CreateDefault, _log);
        _settings.Normalize();
        _palette = _settings.ActivePalette;

        _playback = new NAudioPlaybackFactory(_log);
        _audio = new AudioCoordinator(_playback, _log);
        _audio.Changed += () => Dispatcher.BeginInvoke(new Action(RefreshAudioState));
        _score.Changed += RefreshScore;
        _timer.Expired += () => SetPanel("¡TIEMPO!");

        RedPanel.ContextMenu = BuildScoreMenu(Team.Red);
        BluePanel.ContextMenu = BuildScoreMenu(Team.Blue);

        BuildEqualizer();
        BuildButtons();
        RefreshPaletteList();
        RefreshScore();
        LoadLogos();
        HeaderName.Text = _settings.AssistantName + "...";
        _tick.Tick += OnTick;
        _tick.Start();
        InitBuzzers();
        _log.Info("app", "Iniciado (etapa 2, audio real)");
    }

    // ------------------------------------------------------------ construcción de la botonera

    private Style Btn => (Style)FindResource("NeonBtn");

    private Button MakeButton(string key, string text, Brush bg, Brush fg, Brush border, RoutedEventHandler click)
    {
        var b = new Button { Style = Btn, Content = text, Background = bg, Foreground = fg, BorderBrush = border, Tag = border };
        b.Click += click;
        _buttons[key] = b;
        return b;
    }

    private static Brush Gradient(string top, string bottom) =>
        new LinearGradientBrush((Color)ColorConverter.ConvertFromString(top),
                                (Color)ColorConverter.ConvertFromString(bottom), 90);

    private void BuildButtons()
    {
        // Quitar los botones de la paleta anterior (conservando los de celebración, que viven en el XAML)
        foreach (var k in _buttons.Keys.Where(k => k[0] != 'C').ToList()) _buttons.Remove(k);
        ButtonGrid.Children.Clear();

        var cyan = Gradient("#35E0FF", "#0A9FC4");
        var cyanBorder = (Brush)new SolidColorBrush(Color.FromRgb(0x7F, 0xE8, 0xFF));
        var whiteBorder = (Brush)new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));
        var red = (Brush)new SolidColorBrush(Color.FromRgb(0xE0, 0x10, 0x20));

        // Orden de la imagen: fila 1 voces 1-9, fila 2 canciones 1-9, fila 3 voces 10-18, fila 4 canciones 10-18
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 9; col++)
            {
                int id = (row / 2) * 9 + col + 1;
                if (row % 2 == 0)
                {
                    var cfg = _palette.Voice.FirstOrDefault(v => v.Id == id);
                    if (cfg == null) { ButtonGrid.Children.Add(new Border()); continue; }
                    ButtonGrid.Children.Add(MakeButton("V" + id, cfg.Name, cyan, Brushes.White, cyanBorder,
                        (_, _) => OnVoice(cfg)));
                }
                else
                {
                    var cfg = _palette.Music.FirstOrDefault(m => m.Id == id);
                    if (cfg == null) { ButtonGrid.Children.Add(new Border()); continue; }
                    ButtonGrid.Children.Add(MakeButton("M" + id, cfg.Name, Brushes.White, red, whiteBorder,
                        (_, _) => OnMusic(cfg)));
                }
            }
        }

        // Celebraciones (botones definidos en el XAML)
        var celBg = Gradient("#D2F54A", "#7CC81C");
        var celBorder = (Brush)new SolidColorBrush(Color.FromRgb(0xEA, 0xFF, 0x9A));
        foreach (var (btn, id) in new[] { (BtnCel1, 1), (BtnCel2, 2), (BtnCel3, 3), (BtnCel4, 4) })
        {
            var cfg = _palette.Celebrations.FirstOrDefault(c => c.Id == id);
            btn.Content = cfg?.Name ?? "CELEBRACION";
            btn.Background = celBg;
            btn.BorderBrush = celBorder;
            btn.Tag = celBorder;
            _buttons["C" + id] = btn;
        }
        DisplayPalette.Text = _palette.Name;
    }

    private void BuildEqualizer()
    {
        for (int i = 0; i < 11; i++)
        {
            var bar = new Rectangle { Width = 7, Height = 8, Margin = new Thickness(3, 0, 3, 0), RadiusX = 3, RadiusY = 3,
                                      Fill = new SolidColorBrush(Color.FromRgb(0x3F, 0xD0, 0xFF)), VerticalAlignment = VerticalAlignment.Center };
            _eqBars.Add(bar);
            EqPanel.Children.Add(bar);
        }
    }

    private ContextMenu BuildScoreMenu(Team team)
    {
        var undo = new MenuItem { Header = "Deshacer último punto" };
        undo.Click += (_, _) => _score.UndoLast();
        var reset = new MenuItem { Header = "Reiniciar marcador…" };
        reset.Click += (_, _) =>
        {
            if (Confirm("¿Reiniciar el marcador de ambos equipos a 0?")) _score.Reset();
        };
        var menu = new ContextMenu();
        menu.Items.Add(undo);
        menu.Items.Add(reset);
        return menu;
    }

    private void LoadLogos()
    {
        TrySetLogo(LogoLeftImg, LogoLeftText, _settings.LogoLeftPath);
        TrySetLogo(LogoRightImg, LogoRightText, _settings.LogoRightPath);
    }

    private void TrySetLogo(System.Windows.Controls.Image img, TextBlock placeholder, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            img.Source = bmp;
            placeholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) { _log.Error("archivos", $"No se pudo cargar el logotipo {path}", ex); }
    }

    // ------------------------------------------------------------ acciones del conductor

    private void OnVoice(VoiceButtonConfig cfg)
    {
        string source = "tts:" + cfg.SpokenText;
        if (!string.IsNullOrWhiteSpace(cfg.AudioFile))
        {
            if (File.Exists(cfg.AudioFile)) source = cfg.AudioFile!;
            else _log.Warn("archivos", $"Archivo de voz faltante en {cfg.Name}: {cfg.AudioFile}; se usa la voz de Windows");
        }
        var r = _audio.Toggle(AudioChannel.Voice, "V" + cfg.Id, source);
        if (r == ToggleResult.Blocked) { SetPanel("No se pudo reproducir la voz (ver registro)"); return; }
        if (r != ToggleResult.Started) return; // Stopped: solo detiene la voz; no toca el temporizador

        RoundNumber.Text = cfg.RoundNumber.ToString();
        HeaderName.Text = _settings.AssistantName + "...";
        HeaderSub.Text = cfg.HeaderText;
        SetPanel(cfg.PanelText);
        // Misma ronda mientras corre → no reinicia el contador (lo garantiza RoundTimer)
        _timer.Start("R" + cfg.RoundNumber, cfg.TimeSeconds > 0 ? cfg.TimeSeconds : _settings.DefaultRoundSeconds);
    }

    private void OnMusic(MusicButtonConfig cfg)
    {
        if (!DemoMode)
        {
            if (string.IsNullOrWhiteSpace(cfg.FilePath)) { SetPanel("Sin archivo asignado: " + cfg.Name); return; }
            if (!File.Exists(cfg.FilePath))
            {
                _log.Warn("archivos", $"Archivo faltante para {cfg.Name}: {cfg.FilePath}");
                SetPanel("Archivo no encontrado: " + cfg.Name);
                return;
            }
        }
        var r = _audio.Toggle(AudioChannel.Music, "M" + cfg.Id, cfg.FilePath ?? "(sin archivo)");
        if (r == ToggleResult.Blocked) { SetPanel("No se pudo reproducir: " + cfg.Name + " (ver registro)"); return; }
        if (r != ToggleResult.Started) return;

        RoundNumber.Text = cfg.RoundNumber.ToString();
        HeaderName.Text = cfg.Title;
        HeaderSub.Text = "♪ en reproducción";
        SetPanel(cfg.PanelText);
    }

    private void Celebration_Click(object sender, RoutedEventArgs e)
    {
        // El Tag de estos botones guarda el color de borde, así que el id se obtiene por la clave registrada ("C1".."C4")
        string key = _buttons.First(kv => ReferenceEquals(kv.Value, sender)).Key;
        int id = int.Parse(key.Substring(1));
        var cfg = _palette.Celebrations.FirstOrDefault(c => c.Id == id);
        if (!DemoMode && (cfg == null || string.IsNullOrWhiteSpace(cfg.FilePath) || !File.Exists(cfg.FilePath)))
        {
            _log.Warn("archivos", $"Celebración {id} sin archivo válido");
            SetPanel("Celebración sin archivo asignado");
            return;
        }
        if (_audio.Toggle(AudioChannel.Effect, "C" + id, cfg?.FilePath ?? "(sin archivo)") == ToggleResult.Blocked)
            SetPanel("No se pudo reproducir la celebración (ver registro)");
    }

    private void PointRed_Click(object sender, RoutedEventArgs e) => _score.AddPoint(Team.Red);
    private void PointBlue_Click(object sender, RoutedEventArgs e) => _score.AddPoint(Team.Blue);

    private void Pause_Click(object sender, RoutedEventArgs e) => _audio.TogglePause();

    private void StopAll_Click(object sender, RoutedEventArgs e)
    {
        _audio.StopAll();
        _timer.Cancel();
        EndTurn();
        // Pendiente en etapas siguientes: cancelar animación de calificación y turno activo de pulsadores.
        SetPanel("TODO DETENIDO");
        _log.Info("app", "DETENER TODO");
    }

    private void Instructions_Click(object sender, RoutedEventArgs e)
    {
        string text = _settings.BuzzersEnabled ? _settings.InstructionsWithBuzzers : _settings.InstructionsWithoutBuzzers;
        SetPanel("INSTRUCCIONES", text);
        _audio.Toggle(AudioChannel.Voice, "INSTR", "tts:" + text); // segunda pulsación: detiene la lectura
    }

    private void Rate_Click(object sender, RoutedEventArgs e) => ShowPending("Calificación", 4);

    private void Pending_Click(object sender, RoutedEventArgs e)
    {
        var parts = ((string)((Button)sender).Tag).Split('|');
        ShowPending(parts[0], int.Parse(parts[1]));
    }

    private void ShowPending(string feature, int stage) =>
        SetPanel($"«{feature}»: función pendiente (etapa {stage})");

    private void Config_Click(object sender, RoutedEventArgs e)
    {
        int tab = sender is Button b && b.Tag is string t && int.TryParse(t, out int n) ? n : 0;
        var w = new ConfigWindow(_settings, _palette, tab) { Owner = this };
        w.ShowDialog();
        SaveSettings(showMessage: false);
        BuildButtons();
        RefreshAudioState();
        HeaderName.Text = _settings.AssistantName + "...";
        LoadLogos();
        SetPanel("Configuración guardada");
    }

    private void ClearPalette_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("¿Detener todo y quitar TODOS los archivos asignados de la paleta '" + _palette.Name + "'?")) return;
        _audio.StopAll();
        _timer.Cancel();
        foreach (var m in _palette.Music) m.FilePath = null;
        foreach (var v in _palette.Voice) v.AudioFile = null;
        foreach (var c in _palette.Celebrations) c.FilePath = null;
        SaveSettings(showMessage: false);
        SetPanel("Paleta vaciada: " + _palette.Name);
    }

    private void SavePalette_Click(object sender, RoutedEventArgs e) => SaveSettings(showMessage: true);

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_exitConfirmed)
        {
            if (!Confirm("¿Cerrar el programa?")) { e.Cancel = true; return; }
            _exitConfirmed = true;
        }
        DisposeBuzzers();
        _audio.StopAll();
        _timer.Cancel();
        SaveSettings(showMessage: false);
    }

    private void SaveSettings(bool showMessage)
    {
        try
        {
            _settings.ActivePaletteName = _palette.Name;
            JsonStore.Save(_paths.SettingsFile, _settings);
            if (showMessage) SetPanel("Paleta guardada: " + _palette.Name);
        }
        catch (Exception ex)
        {
            _log.Error("persistencia", "No se pudo guardar la configuración", ex);
            if (showMessage) SetPanel("ERROR al guardar (ver registro)");
        }
    }

    // ------------------------------------------------------------ paletas

    private void RefreshPaletteList()
    {
        _updatingList = true;
        PaletteList.ItemsSource = _settings.Palettes.Select(p => p.Name).ToList();
        PaletteList.SelectedItem = _palette.Name;
        _updatingList = false;
    }

    private void PaletteList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingList || PaletteList.SelectedItem is not string name) return;
        var p = _settings.Palettes.FirstOrDefault(x => x.Name == name);
        if (p == null || ReferenceEquals(p, _palette)) return;
        _audio.StopAll(); // cambiar de paleta en pleno show corta el audio de la anterior
        _timer.Cancel();
        _palette = p;
        BuildButtons();
        SetPanel("Paleta: " + p.Name);
    }

    private void MovePalette(int delta)
    {
        int i = _settings.Palettes.IndexOf(_palette) + delta;
        if (i < 0 || i >= _settings.Palettes.Count) return;
        PaletteList.SelectedIndex = i;
    }
    private void PrevPalette_Click(object sender, RoutedEventArgs e) => MovePalette(-1);
    private void NextPalette_Click(object sender, RoutedEventArgs e) => MovePalette(+1);

    // ------------------------------------------------------------ pantalla completa

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _prevState = WindowState; _prevStyle = WindowStyle; _prevResize = ResizeMode;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = _prevStyle;
            ResizeMode = _prevResize;
            WindowState = _prevState;
        }
        _fullscreen = !_fullscreen;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; }
        else if (e.Key == Key.Escape && _fullscreen) { ToggleFullscreen(); e.Handled = true; }
    }

    // ------------------------------------------------------------ refresco de la interfaz

    private void SetPanel(string title, string? body = null)
    {
        PanelTitle.Text = title;
        PanelBody.Text = body ?? "";
        PanelBodyBorder.Visibility = string.IsNullOrEmpty(body) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshScore()
    {
        RedScore.Text = _score.Red.ToString();
        BlueScore.Text = _score.Blue.ToString();
    }

    private static AudioChannel ChannelFor(string key) =>
        key[0] == 'V' ? AudioChannel.Voice : key[0] == 'M' ? AudioChannel.Music : AudioChannel.Effect;

    private void RefreshAudioState()
    {
        foreach (var (key, btn) in _buttons)
        {
            bool active = _audio.ActiveKey(ChannelFor(key)) == key;
            btn.BorderBrush = active ? Brushes.Gold : (Brush)btn.Tag;
            btn.BorderThickness = new Thickness(active ? 5 : 2);
        }
        BtnPause.Content = _audio.IsPaused ? "▶" : "⏸";

        var musicKey = _audio.ActiveKey(AudioChannel.Music);
        if (musicKey != null && int.TryParse(musicKey.AsSpan(1), out int id))
            DisplayNow.Text = _palette.Music.FirstOrDefault(m => m.Id == id)?.Title ?? "(Ninguno)";
        else
            DisplayNow.Text = "(Ninguno)";
    }

    private void OnTick(object? sender, EventArgs e)
    {
        double dt = _sw.Elapsed.TotalSeconds;
        _sw.Restart();
        _timer.Tick(dt);

        int secs = (int)Math.Ceiling(_timer.Remaining);
        TimerText.Text = $"{secs / 60}:{secs % 60:00}";
        TimerBar.Value = _timer.Progress;

        var now = DateTime.Now;
        DisplayClock.Text = now.ToString("HH:mm:ss");
        var date = now.ToString("dddd\nd 'de' MMMM 'de' yyyy", _es);
        DisplayDate.Text = char.ToUpper(date[0], _es) + date[1..];

        // Ecualizador junto al asistente: solo se mueve mientras habla la voz
        bool speaking = _audio.IsActive(AudioChannel.Voice);
        double max = Math.Max(16, EqPanel.ActualHeight * 0.9);
        foreach (var bar in _eqBars)
            bar.Height = speaking ? 8 + _rng.NextDouble() * (max - 8) : 8;

        // Medidores L/R: nivel pico real de los archivos que suenan (la voz de Windows no se mide)
        var (l, r) = _playback.GetLevels();
        _dispL = Math.Max(l, _dispL * 0.85);
        _dispR = Math.Max(r, _dispR * 0.85);
        MeterL.Height = Math.Clamp(_dispL, 0, 1) * MeterHostL.ActualHeight;
        MeterR.Height = Math.Clamp(_dispR, 0, 1) * MeterHostR.ActualHeight;
    }

    private bool Confirm(string message) =>
        MessageBox.Show(this, message, "Karaoke Interactivo", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
