using System;
using System.Drawing;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using WinterTray.Native;
using WinterTray.Services;
using WinterTray.UI;

namespace WinterTray
{
    public partial class App : Application
    {
        public const string FeedbackUrl = "https://qform.hu/form/wintertray-visszajelzes-funkcioertekeles-es-hibabejelentes-2";

        private static Mutex? _mutex;
        private NotifyIcon? _trayIcon;
        private Settings? _settings;
        private SnowOverlayWindow? _overlay;
        private SettingsWindow? _settingsWindow;
        private UpdateWindow? _updateWindow;
        private ToolStripMenuItem? _titleItem;
        private ToolStripMenuItem? _updateMenuItem;
        private ToolStripMenuItem? _seasonMenuItem;
        private ToolStripMenuItem? _settingsMenuItem;
        private ToolStripMenuItem? _feedbackMenuItem;
        private ToolStripMenuItem? _clearMenuItem;
        private ToolStripMenuItem? _pauseMenuItem;
        private ToolStripMenuItem? _autoStartMenuItem;
        private ToolStripMenuItem? _exitMenuItem;
        private bool _selfTest;
        private UpdateInfo? _latestAvailableUpdate;
        private bool _hasCheckedForUpdatesThisSession;
        private bool _networkMonitoringStarted;

        protected override void OnStartup(StartupEventArgs e)
        {
            string? selfTestDir = null;
            for (int i = 0; i < e.Args.Length; i++)
            {
                if (e.Args[i] == "--unit-tests")
                {
                    SelfTest.RunUnitTests();
                    Shutdown();
                    return;
                }
                if (e.Args[i] == "--selftest")
                {
                    _selfTest = true;
                    selfTestDir = i + 1 < e.Args.Length ? e.Args[i + 1] : Path.Combine(Path.GetTempPath(), "WinterTraySelfTest");
                }
            }

            if (!_selfTest)
            {
                _settings = Settings.Load();
                _mutex = new Mutex(true, "WinterTray_SingleInstance_Mutex", out bool isNewInstance);
                if (!isNewInstance)
                {
                    bool hu = _settings.IsHungarian;
                    MessageBox.Show(
                        hu
                            ? "A WinterTray már fut a háttérben (nézd meg a tálca értesítési területét)."
                            : "WinterTray is already running in the background (check the system tray notification area).",
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

            _settings ??= Settings.Load();
            if (_settings.UseSeasonalWallpaper)
            {
                WallpaperManager.ApplyWallpaper(_settings.Season);
            }
            AppDomain.CurrentDomain.ProcessExit += (s, ev) => WallpaperManager.RestoreOriginalWallpaper();

            _overlay = new SnowOverlayWindow(_settings);
            _overlay.Show();

            SetupTrayIcon();

            if (!_selfTest)
            {
                InitUpdateChecking();
            }
        }

        private void SetupTrayIcon()
        {
            var menu = new ContextMenuStrip();

            _titleItem = new ToolStripMenuItem("❄ WinterTray") { Enabled = false };
            _updateMenuItem = new ToolStripMenuItem("Új frissítés elérhető! ⚡", null, (s, e) => OpenUpdateWindow())
            {
                Visible = false,
                Font = new Font(menu.Font, System.Drawing.FontStyle.Bold)
            };
            _seasonMenuItem = new ToolStripMenuItem("Switch to Autumn mode 🍂", null, (s, e) => ToggleSeason());
            _settingsMenuItem = new ToolStripMenuItem("Settings...", null, (s, e) => OpenSettings());
            _settingsMenuItem.Font = new Font(_settingsMenuItem.Font, System.Drawing.FontStyle.Bold);

            _feedbackMenuItem = new ToolStripMenuItem("Feedback & Bug Report... 💬", null, (s, e) => OpenFeedbackUrl());

            _pauseMenuItem = new ToolStripMenuItem("Pause", null, (s, e) => TogglePause());
            _clearMenuItem = new ToolStripMenuItem("Clear snow", null, (s, e) => _overlay?.ClearSnow());

            _autoStartMenuItem = new ToolStripMenuItem("Start with Windows", null, (s, e) => ToggleAutoStart())
            {
                Checked = _settings?.StartWithWindows ?? false
            };

            _exitMenuItem = new ToolStripMenuItem("Exit", null, (s, e) => ExitApp());

            menu.Items.Add(_titleItem);
            menu.Items.Add(_updateMenuItem);
            menu.Items.Add(_seasonMenuItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_settingsMenuItem);
            menu.Items.Add(_feedbackMenuItem);
            menu.Items.Add(_pauseMenuItem);
            menu.Items.Add(_clearMenuItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_autoStartMenuItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_exitMenuItem);

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
            _trayIcon.BalloonTipClicked += (s, e) =>
            {
                if (_latestAvailableUpdate != null)
                {
                    OpenUpdateWindow(_latestAvailableUpdate);
                }
                else
                {
                    OpenSettings();
                }
            };

            UpdateTrayState();
        }

        public void UpdateTrayState()
        {
            if (_settings == null || _trayIcon == null) return;

            bool hu = _settings.IsHungarian;
            bool isAutumn = _settings.Season == Engine.AppSeason.Autumn;
            bool paused = _overlay?.IsPaused ?? false;

            if (_titleItem != null)
                _titleItem.Text = hu
                    ? (isAutumn ? "🍂 WinterTray (Ősz)" : "❄ WinterTray (Tél)")
                    : (isAutumn ? "🍂 WinterTray (Autumn)" : "❄ WinterTray (Winter)");

            if (_updateMenuItem != null)
            {
                if (_latestAvailableUpdate != null && _latestAvailableUpdate.IsNewer)
                {
                    _updateMenuItem.Visible = true;
                    _updateMenuItem.Text = hu
                        ? $"❄ Új verzió: v{_latestAvailableUpdate.VersionString} ⚡"
                        : $"❄ New version: v{_latestAvailableUpdate.VersionString} ⚡";
                }
                else
                {
                    _updateMenuItem.Visible = false;
                }
            }

            if (_seasonMenuItem != null)
                _seasonMenuItem.Text = hu
                    ? (isAutumn ? "Váltás téli módra ❄" : "Váltás őszi módra 🍂")
                    : (isAutumn ? "Switch to Winter mode ❄" : "Switch to Autumn mode 🍂");

            if (_settingsMenuItem != null)
                _settingsMenuItem.Text = hu ? "Beállítások..." : "Settings...";

            if (_feedbackMenuItem != null)
                _feedbackMenuItem.Text = hu ? "Értékelés és hibabejelentés... 💬" : "Feedback & Bug Report... 💬";

            if (_pauseMenuItem != null)
                _pauseMenuItem.Text = hu
                    ? (paused ? "Folytatás" : "Szüneteltetés")
                    : (paused ? "Resume" : "Pause");

            if (_clearMenuItem != null)
                _clearMenuItem.Text = hu
                    ? (isAutumn ? "Levelek lesöprése" : "Hó letakarítása")
                    : (isAutumn ? "Sweep leaves" : "Clear snow");

            if (_autoStartMenuItem != null)
                _autoStartMenuItem.Text = hu ? "Indulás a Windowszal" : "Start with Windows";

            if (_exitMenuItem != null)
                _exitMenuItem.Text = hu ? "Kilépés" : "Exit";

            _trayIcon.Text = hu
                ? (isAutumn ? "WinterTray - Őszi falevelek" : "WinterTray - Téli hóesés")
                : (isAutumn ? "WinterTray - Autumn Falling Leaves" : "WinterTray - Winter Snowfall");
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

        public static void OpenFeedbackUrl()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = FeedbackUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void TogglePause()
        {
            if (_overlay == null || _pauseMenuItem == null) return;
            bool paused = !_overlay.IsPaused;
            _overlay.SetPaused(paused);
            UpdateTrayState();
        }

        private void ToggleAutoStart()
        {
            if (_settings == null || _autoStartMenuItem == null) return;
            _settings.StartWithWindows = !_settings.StartWithWindows;
            _settings.Save();
            _autoStartMenuItem.Checked = _settings.StartWithWindows;
        }

        private void InitUpdateChecking()
        {
            if (!_networkMonitoringStarted)
            {
                _networkMonitoringStarted = true;
                try
                {
                    NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
                }
                catch { }
            }

            if (_settings?.CheckForUpdatesOnStartup ?? true)
            {
                // Indításkor 3.5 másodperc múlva a háttérben csendesen ellenőrzi a frissítéseket
                _ = Task.Run(async () =>
                {
                    await Task.Delay(3500);
                    await CheckForUpdatesSilentlyAsync();
                });
            }
        }

        private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        {
            // Amikor a számítógép csatlakozik az internethez, és ebben a munkamenetben még nem találtunk frissítést
            if (e.IsAvailable && !_hasCheckedForUpdatesThisSession && (_settings?.CheckForUpdatesOnStartup ?? true))
            {
                _ = Task.Run(async () =>
                {
                    // 4 másodperc várakozás, hogy a DNS és hálózati kapcsolat felálljon
                    await Task.Delay(4000);
                    await CheckForUpdatesSilentlyAsync();
                });
            }
        }

        private async Task CheckForUpdatesSilentlyAsync()
        {
            try
            {
                var update = await UpdateManager.CheckForUpdateAsync(silent: true);
                if (update != null)
                {
                    _hasCheckedForUpdatesThisSession = true;
                    if (update.IsNewer)
                    {
                        _latestAvailableUpdate = update;
                        Dispatcher.Invoke(() =>
                        {
                            ShowUpdateNotification(update);
                        });
                    }
                }
            }
            catch
            {
                // Offline alkalmazásként szándékosan semmilyen hibaüzenetet nem jelenítünk meg
            }
        }

        private void ShowUpdateNotification(UpdateInfo update)
        {
            if (_trayIcon == null) return;

            bool hu = _settings?.IsHungarian ?? false;
            string title = hu ? "❄ Új WinterTray frissítés!" : "❄ WinterTray Update Available!";
            string text = hu
                ? $"Elérhető a legújabb v{update.VersionString} verzió! Kattints ide a frissítés letöltéséhez."
                : $"Version v{update.VersionString} is now available! Click here to download.";

            _trayIcon.BalloonTipTitle = title;
            _trayIcon.BalloonTipText = text;
            _trayIcon.BalloonTipIcon = ToolTipIcon.Info;
            _trayIcon.ShowBalloonTip(10000);

            UpdateTrayState();
        }

        public void OpenUpdateWindow(UpdateInfo? update = null)
        {
            update ??= _latestAvailableUpdate;
            if (update == null)
            {
                OpenSettings();
                return;
            }

            if (_updateWindow == null || !_updateWindow.IsLoaded)
            {
                _updateWindow = new UpdateWindow(update, _settings?.IsHungarian ?? false);
                _updateWindow.Closed += (s, e) => _updateWindow = null;
                _updateWindow.Show();
            }
            else
            {
                _updateWindow.Activate();
            }
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
