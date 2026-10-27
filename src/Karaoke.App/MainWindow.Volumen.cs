using System;
using System.Globalization;
using System.IO;
using System.Windows;

namespace Karaoke.App
{
    // Control de volumen general (master): silenciar, icono dinamico y recordar el nivel.
    // Trabaja junto a tu SliderVolumen_ValueChanged existente; no hace falta modificarlo.
    public partial class MainWindow
    {
        private double _volAntesMute = 0.8;

        private static readonly string _volArchivo = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KaraokeDesafio", "volumen.txt");

        private void VolumenWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Se agregan DESPUES de tu handler del XAML, asi que tu codigo corre primero.
            SliderVolumen.ValueChanged += (s, a) => ActualizarIconoVolumen();
            Closing += (s, a) => GuardarVolumen();

            CargarVolumen();
            ActualizarIconoVolumen();
        }

        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            if (SliderVolumen.Value > 0)
            {
                _volAntesMute = SliderVolumen.Value;
                SliderVolumen.Value = 0;
            }
            else
            {
                SliderVolumen.Value = _volAntesMute > 0 ? _volAntesMute : 0.8;
            }
        }

        private void ActualizarIconoVolumen()
        {
            if (BtnMute == null || SliderVolumen == null) return;
            double v = SliderVolumen.Value;
            BtnMute.Content = v <= 0 ? "🔇" : v < 0.34 ? "🔈" : v < 0.67 ? "🔉" : "🔊";
        }

        private void CargarVolumen()
        {
            try
            {
                if (!File.Exists(_volArchivo)) return;
                string txt = File.ReadAllText(_volArchivo).Trim();
                if (double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                {
                    v = Math.Max(0, Math.Min(1, v));
                    _volAntesMute = v > 0 ? v : 0.8;
                    SliderVolumen.Value = v;
                }
            }
            catch { /* si falla, se queda en el valor por defecto */ }
        }

        private void GuardarVolumen()
        {
            try
            {
                // Si esta silenciado, se guarda el nivel anterior para no arrancar mudo.
                double v = SliderVolumen.Value > 0 ? SliderVolumen.Value : _volAntesMute;
                Directory.CreateDirectory(Path.GetDirectoryName(_volArchivo));
                File.WriteAllText(_volArchivo, v.ToString("0.###", CultureInfo.InvariantCulture));
            }
            catch { /* no es critico */ }
        }
    }
}
