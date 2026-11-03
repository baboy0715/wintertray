using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using WinterTray.Engine;

namespace WinterTray.Native
{
    [ComImport]
    [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID);
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetMonitorDevicePathAt(uint monitorIndex);
        uint GetMonitorDevicePathCount();
        [return: MarshalAs(UnmanagedType.Struct)]
        Win32.RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(int position);
        int GetPosition();
        void SetSlideshow(IntPtr items);
        IntPtr GetSlideshow();
        void SetStatus(int state);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, int direction);
        int GetStatus();
        bool Enable();
    }

    [ComImport]
    [Guid("C2CF033D-074B-49D1-BE1B-41DE2D574A42")]
    [ClassInterface(ClassInterfaceType.None)]
    public class DesktopWallpaper { }

    public sealed class WallpaperState
    {
        public string? BackupPath { get; set; }
        public string? OriginalPath { get; set; }
        public int OriginalPosition { get; set; } = 4;
        public string? WallpaperStyle { get; set; }
        public string? TileWallpaper { get; set; }
        public bool Applied { get; set; }
    }

    public static class WallpaperManager
    {
        private const string DesktopRegKey = @"Control Panel\Desktop";
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinterTray");
        private static readonly string StateFile = Path.Combine(ConfigDir, "wallpaper_state.json");
        private static readonly string BackupBmpFile = Path.Combine(ConfigDir, "original_wallpaper_backup.bmp");
        private static readonly string LegacyBackupDat = Path.Combine(ConfigDir, "original_wallpaper_backup.dat");
        private static readonly string ActiveBmpFile = Path.Combine(ConfigDir, "season_wallpaper_active.bmp");
        private static readonly string LegacyActiveBmp = Path.Combine(ConfigDir, "winter_wallpaper_active.bmp");

        public static bool IsApplied { get; private set; }
        private static AppSeason? _lastAppliedSeason;

        public static string? FindWallpaperPath(AppSeason season)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string currentDir = Directory.GetCurrentDirectory();

            string fileName = season == AppSeason.Autumn ? "wallpaper-fall" : "wallpaper";

            string[] candidates =
            {
                Path.Combine(baseDir, $"{fileName}.jpeg"),
                Path.Combine(baseDir, $"{fileName}.jpg"),
                Path.Combine(baseDir, "Resources", $"{fileName}.jpeg"),
                Path.Combine(currentDir, $"{fileName}.jpeg"),
                Path.Combine(currentDir, $"{fileName}.jpg"),
                Path.Combine(baseDir, "..", $"{fileName}.jpeg"),
                Path.Combine(baseDir, "..", "..", $"{fileName}.jpeg"),
                Path.Combine(baseDir, "..", "..", "..", $"{fileName}.jpeg"),
                $@"C:\projektekű\WinterTray\{fileName}.jpeg",
                Path.Combine(ConfigDir, $"{fileName}.jpeg")
            };

            foreach (var p in candidates)
            {
                try
                {
                    string full = Path.GetFullPath(p);
                    if (File.Exists(full)) return full;
                }
                catch { }
            }

            // Ha őszi képet nem talált, de téli van (vagy fordítva)
            if (season == AppSeason.Autumn)
            {
                return FindWallpaperPath(AppSeason.Winter);
            }
            return null;
        }

        public static string? FindWinterWallpaperPath() => FindWallpaperPath(AppSeason.Winter);

        public static bool ApplyWinterWallpaper() => ApplyWallpaper(AppSeason.Winter);

        public static bool ApplyWallpaper(AppSeason season)
        {
            if (IsApplied && _lastAppliedSeason == season) return true;

            string? wallpaperSource = FindWallpaperPath(season);
            if (wallpaperSource == null) return false;

            try
            {
                Directory.CreateDirectory(ConfigDir);

                // Ha van korábbi .dat mentés, átalakítjuk valódi .bmp kiterjesztésűre
                if (File.Exists(LegacyBackupDat) && !File.Exists(BackupBmpFile))
                {
                    try { File.Copy(LegacyBackupDat, BackupBmpFile, true); } catch { }
                }

                // 1. Az eredeti háttérkép lekérése és biztonsági mentése, ha még nem tettük meg
                if (!IsApplied && !File.Exists(BackupBmpFile))
                {
                    string originalPath = "";
                    int originalPosition = 4; // DWPO_FILL

                    try
                    {
                        var dw = (IDesktopWallpaper)new DesktopWallpaper();
                        originalPath = dw.GetWallpaper(null) ?? "";
                        originalPosition = dw.GetPosition();
                    }
                    catch { }

                    if (string.IsNullOrEmpty(originalPath))
                    {
                        var sb = new StringBuilder(512);
                        if (Win32.SystemParametersInfo(Win32.SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0) != 0)
                        {
                            originalPath = sb.ToString();
                        }
                    }

                    string style = "10";
                    string tile = "0";
                    try
                    {
                        using var key = Registry.CurrentUser.OpenSubKey(DesktopRegKey, false);
                        if (key != null)
                        {
                            if (string.IsNullOrEmpty(originalPath))
                            {
                                originalPath = key.GetValue("Wallpaper")?.ToString() ?? "";
                            }
                            style = key.GetValue("WallpaperStyle")?.ToString() ?? "10";
                            tile = key.GetValue("TileWallpaper")?.ToString() ?? "0";
                        }
                    }
                    catch { }

                    // Biztonsági mentés a jelenlegi háttérképről (.bmp formátumban)
                    string transcoded = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        @"Microsoft\Windows\Themes\TranscodedWallpaper");

                    string sourceToBackup = File.Exists(originalPath) ? originalPath
                        : (File.Exists(transcoded) ? transcoded : "");

                    if (!string.IsNullOrEmpty(sourceToBackup) && File.Exists(sourceToBackup))
                    {
                        try
                        {
                            string normSource = Path.GetFullPath(sourceToBackup);
                            if (!normSource.Contains("wallpaper.jpeg", StringComparison.OrdinalIgnoreCase) &&
                                !normSource.Contains("wallpaper-fall.jpeg", StringComparison.OrdinalIgnoreCase) &&
                                !normSource.Contains("season_wallpaper_active", StringComparison.OrdinalIgnoreCase) &&
                                !normSource.Contains("winter_wallpaper_active", StringComparison.OrdinalIgnoreCase))
                            {
                                File.Copy(sourceToBackup, BackupBmpFile, true);
                            }
                        }
                        catch { }
                    }

                    var state = new WallpaperState
                    {
                        BackupPath = File.Exists(BackupBmpFile) ? BackupBmpFile : null,
                        OriginalPath = originalPath,
                        OriginalPosition = originalPosition,
                        WallpaperStyle = style,
                        TileWallpaper = tile,
                        Applied = true
                    };

                    try
                    {
                        File.WriteAllText(StateFile, JsonSerializer.Serialize(state));
                    }
                    catch { }
                }

                // 2. Beállítjuk a szezonális háttérképet (BMP konverzió Windows SPI és Shell számára)
                try
                {
                    using (var image = Image.FromFile(wallpaperSource))
                    {
                        image.Save(ActiveBmpFile, ImageFormat.Bmp);
                    }
                }
                catch { }

                string appliedFile = File.Exists(ActiveBmpFile) ? ActiveBmpFile : wallpaperSource;

                // MÓDSZER A: IDesktopWallpaper COM interfész
                try
                {
                    var dw = (IDesktopWallpaper)new DesktopWallpaper();
                    dw.SetPosition(4); // 4 = Fill
                    dw.SetWallpaper(null, appliedFile);
                }
                catch { }

                // MÓDSZER B: Win32 Registry + SPI_SETDESKWALLPAPER
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(DesktopRegKey, true))
                    {
                        if (key != null)
                        {
                            key.SetValue("WallpaperStyle", "10");
                            key.SetValue("TileWallpaper", "0");
                        }
                    }

                    Win32.SystemParametersInfo(Win32.SPI_SETDESKWALLPAPER, 0, appliedFile,
                        Win32.SPIF_UPDATEINIFILE | Win32.SPIF_SENDCHANGE);
                }
                catch { }

                IsApplied = true;
                _lastAppliedSeason = season;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void RestoreOriginalWallpaper()
        {
            try
            {
                WallpaperState? state = null;
                if (File.Exists(StateFile))
                {
                    try
                    {
                        string json = File.ReadAllText(StateFile);
                        state = JsonSerializer.Deserialize<WallpaperState>(json);
                    }
                    catch { }
                }

                string? restoreFile = null;

                // 1. Mentett BMP biztonsági mentés
                if (File.Exists(BackupBmpFile))
                {
                    restoreFile = BackupBmpFile;
                }
                else if (File.Exists(LegacyBackupDat))
                {
                    try
                    {
                        File.Copy(LegacyBackupDat, BackupBmpFile, true);
                        restoreFile = BackupBmpFile;
                    }
                    catch { }
                }

                // 2. Eredeti fájl útvonal, ha van és nem a szezonális háttérkép
                if (restoreFile == null && state != null && !string.IsNullOrEmpty(state.OriginalPath) && File.Exists(state.OriginalPath))
                {
                    string p = Path.GetFullPath(state.OriginalPath);
                    if (!p.Contains("season_wallpaper_active", StringComparison.OrdinalIgnoreCase) &&
                        !p.Contains("winter_wallpaper_active", StringComparison.OrdinalIgnoreCase) &&
                        !p.Contains("wallpaper.jpeg", StringComparison.OrdinalIgnoreCase) &&
                        !p.Contains("wallpaper-fall.jpeg", StringComparison.OrdinalIgnoreCase))
                    {
                        restoreFile = state.OriginalPath;
                    }
                }

                // 3. TranscodedWallpaperCache
                if (restoreFile == null)
                {
                    string transcoded = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        @"Microsoft\Windows\Themes\TranscodedWallpaper");
                    if (File.Exists(transcoded))
                    {
                        try
                        {
                            File.Copy(transcoded, BackupBmpFile, true);
                            restoreFile = BackupBmpFile;
                        }
                        catch { }
                    }
                }

                if (!string.IsNullOrEmpty(restoreFile) && File.Exists(restoreFile))
                {
                    try
                    {
                        var dw = (IDesktopWallpaper)new DesktopWallpaper();
                        dw.SetPosition(state?.OriginalPosition ?? 4);
                        dw.SetWallpaper(null, restoreFile);
                    }
                    catch { }

                    try
                    {
                        using var key = Registry.CurrentUser.OpenSubKey(DesktopRegKey, true);
                        if (key != null)
                        {
                            key.SetValue("WallpaperStyle", state?.WallpaperStyle ?? "10");
                            key.SetValue("TileWallpaper", state?.TileWallpaper ?? "0");
                        }

                        Win32.SystemParametersInfo(Win32.SPI_SETDESKWALLPAPER, 0, restoreFile,
                            Win32.SPIF_UPDATEINIFILE | Win32.SPIF_SENDCHANGE);
                    }
                    catch { }
                }

                try
                {
                    if (File.Exists(StateFile)) File.Delete(StateFile);
                    if (File.Exists(ActiveBmpFile)) File.Delete(ActiveBmpFile);
                    if (File.Exists(LegacyActiveBmp)) File.Delete(LegacyActiveBmp);
                }
                catch { }

                IsApplied = false;
                _lastAppliedSeason = null;
            }
            catch
            {
                // hiba esetén nem omlik össze
            }
        }
    }
}
