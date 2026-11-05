using System;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WinterTray.Services
{
    public class UpdateInfo
    {
        public Version RemoteVersion { get; set; } = new Version(0, 0);
        public string VersionString { get; set; } = "";
        public string ReleaseTitle { get; set; } = "";
        public string Changelog { get; set; } = "";
        public string HtmlUrl { get; set; } = "";
        public string? DownloadUrl { get; set; }
        public string? FileName { get; set; }
        public long FileSizeBytes { get; set; }
        public bool IsNewer { get; set; }

        public string FormattedSize
        {
            get
            {
                if (FileSizeBytes <= 0) return "";
                double mb = FileSizeBytes / (1024.0 * 1024.0);
                return $"{mb:F1} MB";
            }
        }
    }

    public class DownloadProgressReport
    {
        public long BytesDownloaded { get; set; }
        public long TotalBytes { get; set; }
        public double Percentage => TotalBytes > 0 ? Math.Clamp((double)BytesDownloaded / TotalBytes * 100.0, 0.0, 100.0) : 0.0;
        public string StatusText { get; set; } = "";
    }

    public static class UpdateManager
    {
        /// <summary>
        /// Elsődleges GitHub API végpont (a felhasználó által megadott repo)
        /// </summary>
        public const string PrimaryReleasesApiUrl = "https://api.github.com/repos/baboy0715/wintray_page/releases/latest";

        /// <summary>
        /// Másodlagos (fallback) GitHub API végpont a forráskód repóhoz
        /// </summary>
        public const string FallbackReleasesApiUrl = "https://api.github.com/repos/baboy0715/wintertray/releases/latest";

        /// <summary>
        /// A hivatalos weboldal URL-je
        /// </summary>
        public const string OfficialWebsiteUrl = "https://wintertray.hu";

        /// <summary>
        /// A GitHub releases böngészős oldala
        /// </summary>
        public const string GitHubReleasesPageUrl = "https://github.com/baboy0715/wintray_page/releases/";

        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        static UpdateManager()
        {
            // A GitHub API megköveteli a User-Agent fejlécet
            if (!HttpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                HttpClient.DefaultRequestHeaders.Add("User-Agent", "WinterTray-DesktopApp");
            }
        }

        /// <summary>
        /// Az aktuálisan futó alkalmazás verziója
        /// </summary>
        public static Version CurrentAppVersion
        {
            get
            {
                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                return ver ?? new Version(2, 2, 0, 0);
            }
        }

        public static string CurrentVersionDisplay
        {
            get
            {
                var v = CurrentAppVersion;
                return v.Build > 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}";
            }
        }

        /// <summary>
        /// Megvizsgálja, hogy a gép jelenleg csatlakozik-e hálózathoz
        /// </summary>
        public static bool IsNetworkAvailable()
        {
            try
            {
                return NetworkInterface.GetIsNetworkAvailable();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Verziószám biztonságos parszolása (pl. "v2.2.0", "v2.3", "2.3.0", "v1.1-beta", vagy fájlnévből: "WinterTray-Setup-v2.3.1.exe")
        /// </summary>
        public static bool TryParseVersion(string? raw, out Version version)
        {
            version = new Version(0, 0);
            if (string.IsNullOrWhiteSpace(raw)) return false;

            string clean = raw.Trim().TrimStart('v', 'V').Trim();
            int dashIndex = clean.IndexOf('-');
            if (dashIndex > 0) clean = clean.Substring(0, dashIndex);

            var parts = clean.Split('.');
            if (parts.Length == 1 && int.TryParse(parts[0], out int major))
            {
                version = new Version(major, 0, 0);
                return true;
            }
            if (parts.Length == 2 && int.TryParse(parts[0], out int maj) && int.TryParse(parts[1], out int min))
            {
                version = new Version(maj, min, 0);
                return true;
            }

            if (Version.TryParse(clean, out version!))
            {
                return true;
            }

            // Ha szövegbe / fájlnévbe van ágyazva a verzió (pl. "WinterTray-Setup-v2.3.1.exe" vagy "Release v2.3.1")
            var match = System.Text.RegularExpressions.Regex.Match(raw, @"[vV]?(\d+\.\d+(?:\.\d+)?(?:\.\d+)?)");
            if (match.Success && match.Groups[1].Value != raw)
            {
                return TryParseVersion(match.Groups[1].Value, out version);
            }

            return false;
        }

        /// <summary>
        /// Összehasonlítja a távoli verziót a helyi verzióval
        /// </summary>
        public static bool IsVersionNewer(Version currentVer, Version remoteVer)
        {
            int cMajor = currentVer.Major;
            int cMinor = currentVer.Minor;
            int cBuild = Math.Max(0, currentVer.Build);

            int rMajor = remoteVer.Major;
            int rMinor = remoteVer.Minor;
            int rBuild = Math.Max(0, remoteVer.Build);

            if (rMajor != cMajor) return rMajor > cMajor;
            if (rMinor != cMinor) return rMinor > cMinor;
            return rBuild > cBuild;
        }

        /// <summary>
        /// Frissítés keresése aszinkron módon.
        /// silent = true esetén semmilyen hiba nem dobódik, null-t ad vissza csendben offline állapotban.
        /// </summary>
        public static async Task<UpdateInfo?> CheckForUpdateAsync(bool silent = true)
        {
            if (silent && !IsNetworkAvailable())
            {
                return null;
            }

            try
            {
                // Először a wintray_page repo legfrissebb kiadását próbáljuk lekérni
                var update = await TryFetchReleaseFromUrlAsync(PrimaryReleasesApiUrl);
                if (update == null)
                {
                    // Ha a wintray_page-en nincs vagy nem elérhető, próbáljuk a wintertray repót
                    update = await TryFetchReleaseFromUrlAsync(FallbackReleasesApiUrl);
                }

                return update;
            }
            catch (Exception)
            {
                if (silent) return null;
                throw;
            }
        }

        private static async Task<UpdateInfo?> TryFetchReleaseFromUrlAsync(string apiUrl)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));
            using var response = await HttpClient.GetAsync(apiUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tagElem) ? tagElem.GetString() ?? "" : "";
            string releaseTitle = root.TryGetProperty("name", out var nameElem) ? nameElem.GetString() ?? tagName : tagName;
            string changelog = root.TryGetProperty("body", out var bodyElem) ? bodyElem.GetString() ?? "" : "";
            string htmlUrl = root.TryGetProperty("html_url", out var htmlElem) ? htmlElem.GetString() ?? GitHubReleasesPageUrl : GitHubReleasesPageUrl;

            string? downloadUrl = null;
            string? fileName = null;
            long fileSize = 0;

            // Keresünk .exe telepítőt az assets listában
            if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsElem.EnumerateArray())
                {
                    string aName = asset.TryGetProperty("name", out var aNameElem) ? aNameElem.GetString() ?? "" : "";
                    if (aName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.TryGetProperty("browser_download_url", out var dlElem) ? dlElem.GetString() : null;
                        fileName = aName;
                        fileSize = asset.TryGetProperty("size", out var sizeElem) ? sizeElem.GetInt64() : 0;
                        break;
                    }
                }
            }

            // Verzió kinyerése és összehasonlítása:
            // 1. tag_name (pl. "v2.3.1")
            // 2. feltöltött telepítő fájlnév (pl. "WinterTray-Setup-v2.3.1.exe")
            // 3. release címe (pl. "WinterTray v2.3.1")
            if (!TryParseVersion(tagName, out var remoteVersion))
            {
                if (!TryParseVersion(fileName, out remoteVersion))
                {
                    if (!TryParseVersion(releaseTitle, out remoteVersion))
                    {
                        return null;
                    }
                }
            }

            // Ha a fájlnévben specifikusabb verziószám van (pl. tag: v2.3, de a fájl v2.3.1)
            if (TryParseVersion(fileName, out var fileVersion) && IsVersionNewer(remoteVersion, fileVersion))
            {
                remoteVersion = fileVersion;
            }

            var current = CurrentAppVersion;
            bool isNewer = IsVersionNewer(current, remoteVersion);

            // Ha nincs külön .exe asset csatolva a release-hez, a fallback link a release weboldala
            downloadUrl ??= htmlUrl;
            fileName ??= $"WinterTray-Setup-{tagName}.exe";

            return new UpdateInfo
            {
                RemoteVersion = remoteVersion,
                VersionString = tagName.TrimStart('v', 'V'),
                ReleaseTitle = releaseTitle,
                Changelog = changelog,
                HtmlUrl = htmlUrl,
                DownloadUrl = downloadUrl,
                FileName = fileName,
                FileSizeBytes = fileSize,
                IsNewer = isNewer
            };
        }

        /// <summary>
        /// Letölti a telepítőt a %TEMP% mappába és jelenti a haladást.
        /// Visszatér a letöltött fájl abszolút elérési útjával.
        /// </summary>
        public static async Task<string> DownloadInstallerAsync(
            UpdateInfo update,
            IProgress<DownloadProgressReport>? progress = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(update.DownloadUrl))
            {
                throw new InvalidOperationException("Nincs érvényes letöltési hivatkozás.");
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "WinterTray_Update");
            Directory.CreateDirectory(tempDir);

            string targetFileName = string.IsNullOrWhiteSpace(update.FileName)
                ? $"WinterTray-Setup-{update.VersionString}.exe"
                : update.FileName;

            string destinationPath = Path.Combine(tempDir, targetFileName);

            using var response = await HttpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? update.FileSizeBytes;

            using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
                totalRead += bytesRead;

                progress?.Report(new DownloadProgressReport
                {
                    BytesDownloaded = totalRead,
                    TotalBytes = totalBytes,
                    StatusText = totalBytes > 0
                        ? $"{(totalRead / (1024.0 * 1024.0)):F1} MB / {(totalBytes / (1024.0 * 1024.0)):F1} MB"
                        : $"{(totalRead / (1024.0 * 1024.0)):F1} MB"
                });
            }

            return destinationPath;
        }
    }
}
