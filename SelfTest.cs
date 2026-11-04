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
        public static void RunUnitTests()
        {
            var def = new Settings();
            if (def.FlakeCount != 250) throw new Exception($"FlakeCount is {def.FlakeCount}, expected 250");
            if (Math.Abs(def.AccumulationSpeed - 0.40) > 0.001) throw new Exception($"AccumulationSpeed is {def.AccumulationSpeed}, expected 0.40");
            if (def.MeltRateSeconds != 0.0) throw new Exception($"MeltRateSeconds is {def.MeltRateSeconds}, expected 0.0");

            var world = new Engine.SnowWorld(def);
            world.Resize(1920, 1080);

            var screen1 = new ScreenInfo
            {
                Bounds = new Rect(0, 0, 1920, 1080),
                WorkingArea = new Rect(0, 0, 1920, 1040),
                IsCovered = false
            };

            var winLeft = new TrackedWindow
            {
                Title = "Left App",
                Rect = new Rect(0, 0, 960, 1040),
                PxRect = new Win32.RECT { Left = 0, Top = 0, Right = 960, Bottom = 1040 },
                IsTaskbar = false
            };

            world.SetScreensAndWindows(new List<ScreenInfo> { screen1 }, new List<TrackedWindow> { winLeft });

            if (!world.IsOccluded(500, -20, 5)) throw new Exception("Point above snapped window was not occluded!");
            if (!world.IsOccluded(500, 300, 5)) throw new Exception("Point inside snapped window was not occluded!");
            if (!world.IsOccluded(958, 300, 5)) throw new Exception("Point on edge of snapped window was not occluded!");
            if (world.IsOccluded(1400, 300, 5)) throw new Exception("Open desktop point was unexpectedly occluded!");

            var winRight = new TrackedWindow
            {
                Title = "Right App",
                Rect = new Rect(960, 0, 960, 1040),
                PxRect = new Win32.RECT { Left = 960, Top = 0, Right = 1920, Bottom = 1040 },
                IsTaskbar = false
            };

            world.SetScreensAndWindows(new List<ScreenInfo> { screen1 }, new List<TrackedWindow> { winLeft, winRight });

            if (!world.IsOccluded(500, 300, 5)) throw new Exception("Left half not occluded when both open");
            if (!world.IsOccluded(1400, 300, 5)) throw new Exception("Right half not occluded when both open");
            if (!world.IsOccluded(960, 300, 5)) throw new Exception("Seam not occluded");

            // Test 3: IsScreenCovered with side-by-side windows
            var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen;
            if (primaryScreen != null)
            {
                var wa = primaryScreen.WorkingArea;
                int midX = wa.Left + wa.Width / 2;

                var wLeftPx = new TrackedWindow
                {
                    Title = "Left App",
                    PxRect = new Win32.RECT { Left = wa.Left, Top = wa.Top, Right = midX, Bottom = wa.Bottom }
                };
                var wRightPx = new TrackedWindow
                {
                    Title = "Right App",
                    PxRect = new Win32.RECT { Left = midX, Top = wa.Top, Right = wa.Right, Bottom = wa.Bottom }
                };

                // Case A: 1 window only (left half) -> NOT covered
                if (SnowOverlayWindow.IsScreenCovered(primaryScreen, new List<TrackedWindow> { wLeftPx }))
                {
                    throw new Exception("Single half window should NOT be covered!");
                }

                // Case B: 2 windows side by side -> COVERED!
                if (!SnowOverlayWindow.IsScreenCovered(primaryScreen, new List<TrackedWindow> { wLeftPx, wRightPx }))
                {
                    throw new Exception("Two side-by-side windows should BE covered!");
                }

                // Case C: Single fullscreen window -> COVERED!
                var wFullPx = new TrackedWindow
                {
                    Title = "Full App",
                    PxRect = new Win32.RECT { Left = wa.Left, Top = wa.Top, Right = wa.Right, Bottom = wa.Bottom }
                };
                if (!SnowOverlayWindow.IsScreenCovered(primaryScreen, new List<TrackedWindow> { wFullPx }))
                {
                    throw new Exception("Single full window should BE covered!");
                }
            }

            Console.WriteLine("ALL UNIT TESTS PASSED!");
        }

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
