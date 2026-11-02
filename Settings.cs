using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace WinterTray
{
    public class Settings
    {
        public const int CurrentVersion = 6;

        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinterTray");
        private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRegistryName = "WinterTray";

        /// <summary>Régi konfig fájlokat (alacsonyabb verzió) alaphelyzetre állítunk, mert a fizika/grafika megváltozott.</summary>
        public int ConfigVersion { get; set; } = 0;

        // ---- Hóesés ----
        public int FlakeCount { get; set; } = 200;
        public double FallSpeed { get; set; } = 1.0;
        public double WindStrength { get; set; } = 0.25;
        public double FlakeSizeMin { get; set; } = 2.3;
        public double FlakeSizeMax { get; set; } = 6.0;
        /// <summary>0 = vegyes, 1 = bolyhos, 2 = jégkristályok</summary>
        public int FlakeStyle { get; set; } = 0;

        // ---- Egér ----
        public bool MouseRepelEnabled { get; set; } = true;
        public double MouseWindPower { get; set; } = 1.0;
        public double MouseSpeedSensitivity { get; set; } = 1.0;
        public double MouseRepelRadius { get; set; } = 90.0;
        public bool MouseSweepSnow { get; set; } = true;

        // ---- Halmok és ablak-díszítés ----
        public bool AccumulateOnWindows { get; set; } = true;
        public bool AccumulateOnTaskbar { get; set; } = false;
        public double AccumulationSpeed { get; set; } = 0.2;
        public double MaxSnowHeight { get; set; } = 22.0;
        /// <summary>Mennyi idő alatt olvad el egy teljes hóréteg (mp). 0 = soha.</summary>
        public double MeltRateSeconds { get; set; } = 0.0;
        public bool FallOffOnWindowMove { get; set; } = true;

        /// <summary>Jégcsapok az ablakok alján és tetején.</summary>
        public bool ShowIcicles { get; set; } = true;
        /// <summary>Jégcsapok sűrűsége (0.4x ritka .. 2.0x nagyon sűrű).</summary>
        public double IcicleDensity { get; set; } = 1.0;
        /// <summary>A hóréteg enyhe ráhajlása az ablak fejlécére (2-4px puha perem).</summary>
        public bool SnowOverhang { get; set; } = true;

        // ---- Teljesítmény / rendszer ----
        /// <summary>0 = Monitor Natív (VSync / 120+ FPS), 30, 60, 120, 144</summary>
        public int TargetFps { get; set; } = 0;
        /// <summary>0 = Kikapcsolva (gyenge gép), 1 = Közepes, 2 = Teljes / Ultra (120 FPS sima részecskék)</summary>
        public int AnimationQuality { get; set; } = 2;
        public bool PauseWhenCovered { get; set; } = true;
        public bool StartWithWindows { get; set; } = false;
        public bool UseWinterWallpaper { get; set; } = true;

        public static Settings Load()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    string json = File.ReadAllText(ConfigFile);
                    var settings = JsonSerializer.Deserialize<Settings>(json);
                    if (settings != null && settings.ConfigVersion >= CurrentVersion)
                    {
                        settings.StartWithWindows = IsAutoStartEnabled();
                        return settings;
                    }
                }
            }
            catch
            {
                // alapértékek
            }

            var def = new Settings { ConfigVersion = CurrentVersion };
            def.StartWithWindows = IsAutoStartEnabled();
            return def;
        }

        public void Save()
        {
            try
            {
                ConfigVersion = CurrentVersion;
                Directory.CreateDirectory(ConfigDir);
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(ConfigFile, JsonSerializer.Serialize(this, options));
                SetAutoStart(StartWithWindows);
            }
            catch
            {
                // mentési hiba nem kritikus
            }
        }

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
                return key?.GetValue(AppRegistryName) != null;
            }
            catch
            {
                return false;
            }
        }

        public static void SetAutoStart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                if (key == null) return;

                if (enable)
                {
                    string exe = Environment.ProcessPath ?? "";
                    if (!string.IsNullOrEmpty(exe))
                    {
                        key.SetValue(AppRegistryName, $"\"{exe}\"");
                    }
                }
                else
                {
                    key.DeleteValue(AppRegistryName, false);
                }
            }
            catch
            {
                // jogosultsági hiba esetén nem omlik össze
            }
        }
    }
}
