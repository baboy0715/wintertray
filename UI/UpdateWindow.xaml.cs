using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WinterTray.Services;

namespace WinterTray.UI
{
    public partial class UpdateWindow : Window
    {
        private readonly UpdateInfo _update;
        private readonly bool _isHungarian;
        private CancellationTokenSource? _cts;
        private bool _isDownloading = false;

        public UpdateWindow(UpdateInfo update, bool isHungarian)
        {
            _update = update;
            _isHungarian = isHungarian;
            InitializeComponent();

            ApplyLocalization();
        }

        private void ApplyLocalization()
        {
            Title = _isHungarian ? "WinterTray - Frissítés" : "WinterTray - Update";

            TxtTitle.Text = _isHungarian ? "Új frissítés érhető el! 🚀" : "New update available! 🚀";

            string sizeInfo = !string.IsNullOrEmpty(_update.FormattedSize) ? $" • {_update.FormattedSize}" : "";
            TxtVersionCompare.Text = _isHungarian
                ? $"WinterTray v{_update.VersionString} (Jelenlegi: v{UpdateManager.CurrentVersionDisplay}){sizeInfo}"
                : $"WinterTray v{_update.VersionString} (Current: v{UpdateManager.CurrentVersionDisplay}){sizeInfo}";

            TxtChangelogTitle.Text = _isHungarian ? "Változások és újdonságok:" : "What's new in this release:";

            if (!string.IsNullOrWhiteSpace(_update.Changelog))
            {
                TxtChangelog.Text = _update.Changelog.Trim();
            }
            else
            {
                TxtChangelog.Text = _isHungarian
                    ? "A legfrissebb verzió tartalmazza az őszi és téli mód finomhangolásait, hibajavításokat és teljesítmény-optimalizációt."
                    : "The latest version contains performance improvements, seasonal enhancements, and bug fixes.";
            }

            BtnCancel.Content = _isHungarian ? "Később" : "Later";
            BtnBrowser.Content = _isHungarian ? "🌐 Megnyitás böngészőben" : "🌐 Open in browser";
            BtnDownload.Content = _isHungarian ? "⚡ Letöltés és Telepítés" : "⚡ Download & Install";
        }

        private void OnBrowserClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                string targetUrl = !string.IsNullOrWhiteSpace(_update.HtmlUrl)
                    ? _update.HtmlUrl
                    : UpdateManager.GitHubReleasesPageUrl;

                Process.Start(new ProcessStartInfo
                {
                    FileName = targetUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    _isHungarian ? $"Nem sikerült megnyitni a böngészőt: {ex.Message}" : $"Could not open browser: {ex.Message}",
                    "WinterTray", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void OnDownloadClicked(object sender, RoutedEventArgs e)
        {
            if (_isDownloading) return;

            // Ha nincs közvetlen .exe link, megnyitjuk a böngészőt
            if (string.IsNullOrWhiteSpace(_update.DownloadUrl) || !_update.DownloadUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                OnBrowserClicked(sender, e);
                return;
            }

            _isDownloading = true;
            _cts = new CancellationTokenSource();

            BtnDownload.IsEnabled = false;
            BtnBrowser.IsEnabled = false;
            BtnCancel.Content = _isHungarian ? "Mégse" : "Cancel";
            PanelProgress.Visibility = Visibility.Visible;
            TxtProgressStatus.Text = _isHungarian ? "Letöltés indítása..." : "Starting download...";
            PbDownload.Value = 0;

            var progress = new Progress<DownloadProgressReport>(report =>
            {
                PbDownload.Value = report.Percentage;
                TxtProgressPercent.Text = $"{report.Percentage:F0}%";
                TxtProgressStatus.Text = _isHungarian
                    ? $"Letöltve: {report.StatusText}"
                    : $"Downloaded: {report.StatusText}";
            });

            try
            {
                string downloadedPath = await UpdateManager.DownloadInstallerAsync(_update, progress, _cts.Token);

                TxtProgressStatus.Text = _isHungarian ? "Telepítő indítása..." : "Starting installer...";
                TxtProgressPercent.Text = "100%";
                PbDownload.Value = 100;

                await Task.Delay(400);

                if (File.Exists(downloadedPath))
                {
                    // Telepítő elindítása
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = downloadedPath,
                        UseShellExecute = true
                    });

                    // Alkalmazás leállítása, hogy a frissítés zavartalanul végbemenjen
                    Application.Current.Shutdown();
                }
            }
            catch (OperationCanceledException)
            {
                TxtProgressStatus.Text = _isHungarian ? "Letöltés megszakítva." : "Download cancelled.";
                ResetDownloadUI();
            }
            catch (Exception ex)
            {
                string msg = _isHungarian
                    ? $"A letöltés nem sikerült ({ex.Message}). Szeretnéd letölteni a böngészőből?"
                    : $"Download failed ({ex.Message}). Would you like to download via browser?";

                var res = MessageBox.Show(msg, "WinterTray", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                {
                    OnBrowserClicked(sender, e);
                }
                ResetDownloadUI();
            }
        }

        private void ResetDownloadUI()
        {
            _isDownloading = false;
            BtnDownload.IsEnabled = true;
            BtnBrowser.IsEnabled = true;
            BtnCancel.Content = _isHungarian ? "Később" : "Later";
        }

        private void OnCancelClicked(object sender, RoutedEventArgs e)
        {
            if (_isDownloading)
            {
                _cts?.Cancel();
                ResetDownloadUI();
            }
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_isDownloading)
            {
                _cts?.Cancel();
            }
            base.OnClosed(e);
        }
    }
}
