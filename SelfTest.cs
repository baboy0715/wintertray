using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using WinterTray.Native;
using WinterTray.UI;

namespace WinterTray
{
    /// <summary>
    /// Fejlesztői önellenőrzés (WinterTray.exe --selftest &lt;mappa&gt;): megnyit egy teszt-ablakot,
    /// megvárja a hófelhalmozódást, lesöpri az egérrel, bezárja az ablakot, és minden lépésről képernyőképet ment.
    /// </summary>
    internal static class SelfTest
    {
        public static async Task RunAsync(string dir, SnowOverlayWindow overlay)
        {
            Directory.CreateDirectory(dir);
            string log = Path.Combine(dir, "selftest.log");
            File.WriteAllText(log, $"WinterTray selftest {DateTime.Now}\n");

            dynamic? shell = null;
            try
            {
                var shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType != null) shell = Activator.CreateInstance(shellType);
            }
            catch { /* nem kritikus */ }

            try
            {
                // a háttér-ablakok ideiglenes lekicsinyítése, hogy az asztal látszódjon (a végén visszaállítjuk)
                shell?.MinimizeAll();
                await Task.Delay(1500);
                ZOrder.DumpZOrder(overlay.Hwnd, log, "minimizeAll után");
                Shot(Path.Combine(dir, "1_desktop_empty.png"));

                var win = CreateTestWindow();
                win.Show();
                await Task.Delay(1500);
                ZOrder.DumpZOrder(overlay.Hwnd, log, "teszt-ablak megnyitva");

                await Task.Delay(8500);
                Shot(Path.Combine(dir, "2_pile_grown.png"));

                // söprés az egérrel az ablak teteje fölött
                var handle = new WindowInteropHelper(win).Handle;
                Win32.GetAccurateWindowRect(handle, out var r);
                int y = r.Top - 8;
                for (int i = 0; i <= 40; i++)
                {
                    Win32.SetCursorPos(r.Left + 40 + (r.Width - 80) * i / 40, y);
                    await Task.Delay(12);
                }
                await Task.Delay(150);
                Shot(Path.Combine(dir, "3_after_sweep.png"));

                // újra felgyűlik, majd bezárjuk az ablakot -> lavina
                await Task.Delay(7000);
                Shot(Path.Combine(dir, "4_pile_regrown.png"));
                win.Close();
                await Task.Delay(280);
                Shot(Path.Combine(dir, "5_avalanche_a.png"));
                await Task.Delay(260);
                Shot(Path.Combine(dir, "6_avalanche_b.png"));
                await Task.Delay(1500);
                Shot(Path.Combine(dir, "7_after.png"));
            }
            catch (Exception ex)
            {
                File.AppendAllText(log, "HIBA: " + ex + "\n");
            }
            finally
            {
                try { shell?.UndoMinimizeALL(); } catch { }
                File.AppendAllText(log, "kész\n");
                Application.Current.Shutdown();
            }
        }

        private static Window CreateTestWindow()
        {
            return new Window
            {
                Title = "WinterTray teszt-ablak",
                Left = 380,
                Top = 240,
                Width = 620,
                Height = 340,
                ShowActivated = false,
                Background = Brushes.WhiteSmoke,
                Content = new TextBlock
                {
                    Text = "Teszt-ablak\nide ül a hó",
                    FontSize = 30,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            };
        }

        private static void Shot(string path)
        {
            var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
            using var bmp = new Bitmap(vs.Width, vs.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                // 0x40000000 = CAPTUREBLT: a réteges (átlátszó) ablakokat is lefényképezi
                g.CopyFromScreen(vs.Left, vs.Top, 0, 0, bmp.Size,
                    CopyPixelOperation.SourceCopy | (CopyPixelOperation)0x40000000);
            }
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
