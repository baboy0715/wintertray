using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using WinterTray.Engine;
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

            // Test 3: Floating window (e.g. File Explorer) with open desktop above it
            var winExplorer = new TrackedWindow
            {
                HWnd = new IntPtr(12345),
                Title = "File Explorer",
                Rect = new Rect(200, 300, 800, 500),
                PxRect = new Win32.RECT { Left = 200, Top = 300, Right = 1000, Bottom = 800 },
                IsTaskbar = false
            };
            var winBackground = new TrackedWindow
            {
                HWnd = new IntPtr(67890),
                Title = "Background App",
                Rect = new Rect(100, 350, 1200, 550),
                PxRect = new Win32.RECT { Left = 100, Top = 350, Right = 1300, Bottom = 900 },
                IsTaskbar = false
            };

            // Explorer is in front of winBackground:
            world.SetScreensAndWindows(new List<ScreenInfo> { screen1 }, new List<TrackedWindow> { winExplorer, winBackground });

            // Flake approaching the ledge of Explorer (e.g. Y = 295, radius 4.4 -> Y + radius = 299.4) must NOT be occluded:
            if (world.IsOccluded(500, 295, 4.4)) throw new Exception("Point just above floating window ledge must NOT be occluded!");
            if (world.IsOccluded(500, 300, 4.4)) throw new Exception("Point at floating window ledge must NOT be occluded!");

            // Inside Explorer (e.g. Y = 350) MUST be occluded:
            if (!world.IsOccluded(500, 350, 4.4)) throw new Exception("Point inside floating window body MUST be occluded!");

            // Test snow accumulation on Explorer ledge:
            for (int tick = 0; tick < 300; tick++)
            {
                world.Update(0.016, new CursorState { Valid = false });
            }

            var explorerBank = world.GetBankForTesting(winExplorer.HWnd);
            if (explorerBank == null) throw new Exception("Bank was not created for floating window!");
            if (explorerBank.Peak <= 0.0) throw new Exception($"Snow did not accumulate on floating window! Peak = {explorerBank.Peak}");

            // Test 4: IsScreenCovered with side-by-side windows
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

            // Test 5: Uniform snowflake density proportional to open sky width
            world.SetScreensAndWindows(new List<ScreenInfo> { screen1 }, new List<TrackedWindow>());
            int fullSkyCount = world.GetActiveFlakeCountForTesting();
            if (fullSkyCount != 250) throw new Exception($"Expected 250 active flakes for full sky, got {fullSkyCount}");

            world.SetScreensAndWindows(new List<ScreenInfo> { screen1 }, new List<TrackedWindow> { winLeft });
            int halfSkyCount = world.GetActiveFlakeCountForTesting();
            if (halfSkyCount < 120 || halfSkyCount > 130) throw new Exception($"Expected ~125 active flakes for half sky, got {halfSkyCount}");

            world.SetScreensAndWindows(new List<ScreenInfo> { screen1 }, new List<TrackedWindow> { winLeft, winRight });
            int zeroSkyCount = world.GetActiveFlakeCountForTesting();
            if (zeroSkyCount != 0) throw new Exception($"Expected 0 active flakes for fully covered sky, got {zeroSkyCount}");

            // Test 6: Pausing hides overlay so no frozen flakes appear on newly opened windows
            var testOverlay = new SnowOverlayWindow(def);
            testOverlay.SetPaused(true);
            if (testOverlay.Visibility != Visibility.Hidden) throw new Exception("Paused overlay must be Hidden!");
            testOverlay.SetPaused(false);
            if (testOverlay.Visibility != Visibility.Visible) throw new Exception("Resumed overlay must be Visible!");
            testOverlay.Close();

            Console.WriteLine("ALL UNIT TESTS PASSED!");
            File.WriteAllText("wintertray_test.txt", "PASSED");
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
