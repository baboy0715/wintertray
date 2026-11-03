using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using WinterTray.Native;
using WinterTray.UI;

namespace WinterTray
{
    public partial class App : Application
    {
        private static Mutex? _mutex;
        private NotifyIcon? _trayIcon;
        private Settings? _settings;
        private SnowOverlayWindow? _overlay;
        private SettingsWindow? _settingsWindow;
        private ToolStripMenuItem? _titleItem;
        private ToolStripMenuItem? _seasonMenuItem;
        private ToolStripMenuItem? _clearMenuItem;
        private ToolStripMenuItem? _pauseMenuItem;
        private ToolStripMenuItem? _autoStartMenuItem;
        private bool _selfTest;

        protected override void OnStartup(StartupEventArgs e)
        {
            string? selfTestDir = null;
            for (int i = 0; i < e.Args.Length; i++)
            {
                if (e.Args[i] == "--selftest")
                {
                    _selfTest = true;
                    selfTestDir = i + 1 < e.Args.Length ? e.Args[i + 1] : Path.Combine(Path.GetTempPath(), "WinterTraySelfTest");
                }
            }

            if (!_selfTest)
            {
                _mutex = new Mutex(true, "WinterTray_SingleInstance_Mutex", out bool isNewInstance);
                if (!isNewInstance)
                {
                    MessageBox.Show(
                        "A WinterTray már fut a háttérben (nézd meg a tálca értesítési területét).",
                        "WinterTray", MessageBoxButton.OK, MessageBoxImage.Information);
                    Shutdown();
                    return;
                }
            }

            base.OnStartup(e);

            if (_selfTest)
            {
                // gyorsított, szándékosan feltűnő teszt-beállítások (nem mentjük el)
                _settings = new Settings
                {
                    ConfigVersion = Settings.CurrentVersion,
                    FlakeCount = 380,
                    AccumulationSpeed = 5.0
                };
                _overlay = new SnowOverlayWindow(_settings);
                _overlay.Show();
                _ = SelfTest.RunAsync(selfTestDir!, _overlay);
                return;
            }

            _settings = Settings.Load();
            if (_settings.UseSeasonalWallpaper)
            {
                WallpaperManager.ApplyWallpaper(_settings.Season);
            }
            AppDomain.CurrentDomain.ProcessExit += (s, ev) => WallpaperManager.RestoreOriginalWallpaper();

            _overlay = new SnowOverlayWindow(_settings);
            _overlay.Show();

            SetupTrayIcon();
        }

        private void SetupTrayIcon()
        {
            var menu = new ContextMenuStrip();

            _titleItem = new ToolStripMenuItem("❄ WinterTray") { Enabled = false };
            _seasonMenuItem = new ToolStripMenuItem("Váltás őszi módra 🍂", null, (s, e) => ToggleSeason());
            var settingsItem = new ToolStripMenuItem("Beállítások...", null, (s, e) => OpenSettings());
            settingsItem.Font = new Font(settingsItem.Font, System.Drawing.FontStyle.Bold);

            _pauseMenuItem = new ToolStripMenuItem("Szüneteltetés", null, (s, e) => TogglePause());
            _clearMenuItem = new ToolStripMenuItem("Hó letakarítása", null, (s, e) => _overlay?.ClearSnow());

            _autoStartMenuItem = new ToolStripMenuItem("Indulás a Windowszal", null, (s, e) => ToggleAutoStart())
            {
                Checked = _settings?.StartWithWindows ?? false
            };

            var exitItem = new ToolStripMenuItem("Kilépés", null, (s, e) => ExitApp());

            menu.Items.Add(_titleItem);
            menu.Items.Add(_seasonMenuItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(settingsItem);
            menu.Items.Add(_pauseMenuItem);
            menu.Items.Add(_clearMenuItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_autoStartMenuItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            Icon appIcon = SystemIcons.Application;
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");
                if (File.Exists(iconPath)) appIcon = new Icon(iconPath);
            }
            catch
            {
                // rendszer ikon
            }

            _trayIcon = new NotifyIcon
            {
                Icon = appIcon,
                ContextMenuStrip = menu,
                Text = "WinterTray",
                Visible = true
            };
            _trayIcon.DoubleClick += (s, e) => OpenSettings();

            UpdateTrayState();
        }

        public void UpdateTrayState()
        {
            if (_settings == null || _trayIcon == null) return;

            bool isAutumn = _settings.Season == Engine.AppSeason.Autumn;
            if (_titleItem != null)
                _titleItem.Text = isAutumn ? "🍂 WinterTray (Ősz)" : "❄ WinterTray (Tél)";
            if (_seasonMenuItem != null)
                _seasonMenuItem.Text = isAutumn ? "Váltás téli módra ❄" : "Váltás őszi módra 🍂";
            if (_clearMenuItem != null)
                _clearMenuItem.Text = isAutumn ? "Levelek lesöprése" : "Hó letakarítása";

            _trayIcon.Text = isAutumn ? "WinterTray - Őszi falevelek" : "WinterTray - Téli hóesés";
        }

        private void ToggleSeason()
        {
            if (_settings == null || _overlay == null) return;

            _settings.Season = _settings.Season == Engine.AppSeason.Winter
                ? Engine.AppSeason.Autumn
                : Engine.AppSeason.Winter;

            if (_settings.UseSeasonalWallpaper)
            {
                WallpaperManager.ApplyWallpaper(_settings.Season);
            }

            _settings.Save();
            _overlay.Reconfigure();
            UpdateTrayState();
        }

        private void OpenSettings()
        {
            if (_settings == null || _overlay == null) return;

            if (_settingsWindow == null || !_settingsWindow.IsLoaded)
            {
                _settingsWindow = new SettingsWindow(_settings, _overlay, () => UpdateTrayState());
                _settingsWindow.Closed += (s, e) =>
                {
                    _settingsWindow = null;
                    if (_autoStartMenuItem != null) _autoStartMenuItem.Checked = _settings.StartWithWindows;
                    UpdateTrayState();
                };
                _settingsWindow.Show();
            }
            else
            {
                _settingsWindow.Activate();
            }
        }

        private void TogglePause()
        {
            if (_overlay == null || _pauseMenuItem == null) return;
            bool paused = !_overlay.IsPaused;
            _overlay.SetPaused(paused);
            _pauseMenuItem.Text = paused ? "Folytatás" : "Szüneteltetés";
        }

        private void ToggleAutoStart()
        {
            if (_settings == null || _autoStartMenuItem == null) return;
            _settings.StartWithWindows = !_settings.StartWithWindows;
            _settings.Save();
            _autoStartMenuItem.Checked = _settings.StartWithWindows;
        }

        private void ExitApp()
        {
            Native.WallpaperManager.RestoreOriginalWallpaper();
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            _settings?.Save();
            _overlay?.Close();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Native.WallpaperManager.RestoreOriginalWallpaper();
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            if (!_selfTest)
            {
                try { _mutex?.ReleaseMutex(); } catch { }
            }
            base.OnExit(e);
        }
    }
}
