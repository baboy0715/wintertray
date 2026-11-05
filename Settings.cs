using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using WinterTray.Engine;

namespace WinterTray
{
    public class Settings
    {
        public const int CurrentVersion = 9;

        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinterTray");
        private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRegistryName = "WinterTray";

        public int ConfigVersion { get; set; } = 0;

        // ---- Nyelv / Language ("en" = English, "hu" = Magyar) ----
        public string Language { get; set; } = "en";

        [JsonIgnore]
        public bool IsHungarian => string.Equals(Language, "hu", StringComparison.OrdinalIgnoreCase);

        // ---- Évszak választás ----
        public AppSeason Season { get; set; } = AppSeason.Winter;

        // ---- Tél (Hóesés) ----
        public int FlakeCount { get; set; } = 250;
        public double FallSpeed { get; set; } = 1.0;
        public double WindStrength { get; set; } = 0.25;
        public double FlakeSizeMin { get; set; } = 2.3;
        public double FlakeSizeMax { get; set; } = 6.0;
        /// <summary>0 = vegyes, 1 = bolyhos, 2 = jégkristályok</summary>
        public int FlakeStyle { get; set; } = 0;

        // ---- Ősz (Eső és Falevelek) ----
        public bool RainEnabled { get; set; } = true;
        public int RainCount { get; set; } = 220;
        public double RainFallSpeed { get; set; } = 1.0;
        public int LeafCount { get; set; } = 7;
        public double LeafFallSpeed { get; set; } = 0.95;
        public double LeafWindStrength { get; set; } = 0.25;
        public double LeafSize { get; set; } = 18.0;
        /// <summary>Megült levelek élettartama (másodperc). 0 = soha nem tűnik el.</summary>
        public double LeafLifetimeSeconds { get; set; } = 120.0;
        public bool AccumulateLeavesOnWindows { get; set; } = true;

        // ---- Egér ----
        public bool MouseRepelEnabled { get; set; } = true;
        public double MouseWindPower { get; set; } = 1.0;
        public double MouseSpeedSensitivity { get; set; } = 1.0;
        public double MouseRepelRadius { get; set; } = 90.0;
        public bool MouseSweepSnow { get; set; } = true;

        // ---- Halmok és ablak-díszítés ----
        public bool AccumulateOnWindows { get; set; } = true;
        public bool AccumulateOnTaskbar { get; set; } = false;
        public double AccumulationSpeed { get; set; } = 0.40;
        public bool LimitSnowHeight { get; set; } = true;
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

        public bool UseSeasonalWallpaper { get; set; } = true;

        [JsonPropertyName("UseWinterWallpaper")]
        public bool UseWinterWallpaper
        {
            get => UseSeasonalWallpaper;
            set => UseSeasonalWallpaper = value;
        }

        // ---- Frissítések / Updates ----
        public bool CheckForUpdatesOnStartup { get; set; } = true;

        public static Settings Load()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    string json = File.ReadAllText(ConfigFile);
                    var settings = JsonSerializer.Deserialize<Settings>(json);
                    if (settings != null && settings.ConfigVersion >= 5)
                    {
                        if (string.IsNullOrWhiteSpace(settings.Language))
                        {
                            settings.Language = "en";
                        }
                        if (settings.ConfigVersion < 9)
                        {
                            if (settings.FlakeCount == 200)
                            {
                                settings.FlakeCount = 250;
                            }
                            if (Math.Abs(settings.AccumulationSpeed - 0.2) < 0.01)
                            {
                                settings.AccumulationSpeed = 0.40;
                            }
                            settings.MeltRateSeconds = 0.0;
                            settings.ConfigVersion = CurrentVersion;
                        }
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
