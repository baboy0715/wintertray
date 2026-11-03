using System;
using System.Windows;
using WinterTray.Engine;
using WinterTray.Native;

namespace WinterTray.UI
{
    public partial class SettingsWindow : Window
    {
        private readonly Settings _settings;
        private readonly SnowOverlayWindow _overlay;
        private readonly Action? _onSeasonChangedCallback;
        private bool _isInitializing = true;

        public SettingsWindow(Settings settings, SnowOverlayWindow overlay, Action? onSeasonChangedCallback = null)
        {
            _settings = settings;
            _overlay = overlay;
            _onSeasonChangedCallback = onSeasonChangedCallback;
            InitializeComponent();

            LoadValues();
            _isInitializing = false;
        }

        private static string MeltText(double seconds) => seconds <= 0 ? "soha" : $"{seconds:F0} mp";

        private static string LeafLifeText(double seconds)
        {
            if (seconds <= 0) return "soha (nem tűnnek el)";
            if (Math.Abs(seconds - 120.0) < 0.5) return "120 mp (2 perc)";
            if (Math.Abs(seconds - 60.0) < 0.5) return "60 mp (1 perc)";
            if (Math.Abs(seconds - 180.0) < 0.5) return "180 mp (3 perc)";
            if (Math.Abs(seconds - 240.0) < 0.5) return "240 mp (4 perc)";
            if (Math.Abs(seconds - 300.0) < 0.5) return "300 mp (5 perc)";
            return $"{seconds:F0} mp";
        }

        private void UpdateSnowHeightText()
        {
            if (_settings.LimitSnowHeight)
            {
                TxtMaxHeight.Text = $"{_settings.MaxSnowHeight:F0} px";
                SliderMaxHeight.IsEnabled = true;
            }
            else
            {
                TxtMaxHeight.Text = "korlátlan (nincs limit)";
                SliderMaxHeight.IsEnabled = false;
            }
        }

        private void LoadValues()
        {
            // Szezon
            bool isAutumn = _settings.Season == AppSeason.Autumn;
            RbSeasonWinter.IsChecked = !isAutumn;
            RbSeasonAutumn.IsChecked = isAutumn;
            UpdateSeasonUI(isAutumn);

            // Téli beállítások
            CmbStyle.SelectedIndex = Math.Clamp(_settings.FlakeStyle, 0, 2);

            SliderFlakeCount.Value = _settings.FlakeCount;
            TxtFlakeCount.Text = $"{_settings.FlakeCount} db";

            SliderFallSpeed.Value = _settings.FallSpeed;
            TxtFallSpeed.Text = $"{_settings.FallSpeed:F1}x";

            SliderWind.Value = _settings.WindStrength;
            TxtWind.Text = $"{_settings.WindStrength:F1}";

            SliderSize.Value = _settings.FlakeSizeMax;
            TxtSize.Text = $"{_settings.FlakeSizeMin:F1} - {_settings.FlakeSizeMax:F1} px";

            ChkAccumulate.IsChecked = _settings.AccumulateOnWindows;
            ChkSnowOverhang.IsChecked = _settings.SnowOverhang;
            ChkShowIcicles.IsChecked = _settings.ShowIcicles;
            SliderIcicleDensity.Value = _settings.IcicleDensity;
            TxtIcicleDensity.Text = $"{_settings.IcicleDensity:F1}x";
            ChkTaskbar.IsChecked = _settings.AccumulateOnTaskbar;
            SliderAccum.Value = _settings.AccumulationSpeed;
            TxtAccum.Text = $"{_settings.AccumulationSpeed:F2}x";
            ChkLimitMaxHeight.IsChecked = _settings.LimitSnowHeight;
            SliderMaxHeight.Value = _settings.MaxSnowHeight;
            UpdateSnowHeightText();
            SliderMeltRate.Value = _settings.MeltRateSeconds;
            TxtMeltRate.Text = MeltText(_settings.MeltRateSeconds);
            ChkFallOff.IsChecked = _settings.FallOffOnWindowMove;

            // Őszi beállítások
            ChkRain.IsChecked = _settings.RainEnabled;
            SliderRainCount.Value = _settings.RainCount;
            TxtRainCount.Text = $"{_settings.RainCount} db";
            SliderRainSpeed.Value = _settings.RainFallSpeed;
            TxtRainSpeed.Text = $"{_settings.RainFallSpeed:F1}x";

            SliderLeafCount.Value = _settings.LeafCount;
            TxtLeafCount.Text = $"{_settings.LeafCount} db";

            SliderLeafSpeed.Value = _settings.LeafFallSpeed;
            TxtLeafSpeed.Text = $"{_settings.LeafFallSpeed:F1}x";

            SliderLeafWind.Value = _settings.LeafWindStrength;
            TxtLeafWind.Text = $"{_settings.LeafWindStrength:F1}";

            SliderLeafSize.Value = _settings.LeafSize;
            TxtLeafSize.Text = $"{_settings.LeafSize:F0} px";

            ChkLeafAccumulate.IsChecked = _settings.AccumulateLeavesOnWindows;
            ChkLeafTaskbar.IsChecked = _settings.AccumulateOnTaskbar;
            SliderLeafLife.Value = _settings.LeafLifetimeSeconds;
            TxtLeafLife.Text = LeafLifeText(_settings.LeafLifetimeSeconds);

            // Egér beállítások
            ChkMouseRepel.IsChecked = _settings.MouseRepelEnabled;
            SliderMousePower.Value = _settings.MouseWindPower;
            TxtMousePower.Text = $"{_settings.MouseWindPower:F1}x";
            SliderMouseSens.Value = _settings.MouseSpeedSensitivity;
            TxtMouseSens.Text = $"{_settings.MouseSpeedSensitivity:F1}x";
            SliderMouseRadius.Value = _settings.MouseRepelRadius;
            TxtMouseRadius.Text = $"{_settings.MouseRepelRadius:F0} px";
            ChkSweep.IsChecked = _settings.MouseSweepSnow;

            // Rendszer beállítások
            CmbFps.SelectedIndex = _settings.TargetFps switch
            {
                120 => 1,
                60 => 2,
                30 => 3,
                _ => 0
            };
            CmbAnimQuality.SelectedIndex = _settings.AnimationQuality switch
            {
                1 => 1,
                0 => 2,
                _ => 0
            };
            ChkPauseCovered.IsChecked = _settings.PauseWhenCovered;
            ChkAutoStart.IsChecked = _settings.StartWithWindows;
            ChkUseWallpaper.IsChecked = _settings.UseSeasonalWallpaper;
        }

        private void UpdateSeasonUI(bool isAutumn)
        {
            PanelWinter.Visibility = isAutumn ? Visibility.Collapsed : Visibility.Visible;
            PanelAutumn.Visibility = isAutumn ? Visibility.Visible : Visibility.Collapsed;

            TxtHeaderIcon.Text = isAutumn ? "🍂" : "❄";
            TxtHeaderIcon.Foreground = isAutumn
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 167, 38))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(96, 205, 255));

            TxtHeaderDesc.Text = isAutumn
                ? "Őszi falevelek hullása és megülése az ablakokon"
                : "Téli hóesés és hófelhalmozódás az ablakokon";

            BtnClear.Content = isAutumn ? "Levelek lesöprése" : "Hó letakarítása";
        }

        private void OnSeasonChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            bool isAutumn = RbSeasonAutumn.IsChecked == true;
            _settings.Season = isAutumn ? AppSeason.Autumn : AppSeason.Winter;

            UpdateSeasonUI(isAutumn);

            if (_settings.UseSeasonalWallpaper)
            {
                WallpaperManager.ApplyWallpaper(_settings.Season);
            }

            _overlay.Reconfigure();
            _onSeasonChangedCallback?.Invoke();
        }

        private void OnSettingChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            if (sender == ChkLeafTaskbar)
            {
                _settings.AccumulateOnTaskbar = ChkLeafTaskbar.IsChecked ?? false;
                ChkTaskbar.IsChecked = _settings.AccumulateOnTaskbar;
            }
            else if (sender == ChkTaskbar)
            {
                _settings.AccumulateOnTaskbar = ChkTaskbar.IsChecked ?? false;
                ChkLeafTaskbar.IsChecked = _settings.AccumulateOnTaskbar;
            }

            // Téli beállítások mentése a modellbe
            _settings.FlakeStyle = CmbStyle.SelectedIndex;
            _settings.FlakeCount = (int)SliderFlakeCount.Value;
            TxtFlakeCount.Text = $"{_settings.FlakeCount} db";

            _settings.FallSpeed = SliderFallSpeed.Value;
            TxtFallSpeed.Text = $"{_settings.FallSpeed:F1}x";

            _settings.WindStrength = SliderWind.Value;
            TxtWind.Text = $"{_settings.WindStrength:F1}";

            _settings.FlakeSizeMax = SliderSize.Value;
            _settings.FlakeSizeMin = Math.Max(1.6, _settings.FlakeSizeMax * 0.38);
            TxtSize.Text = $"{_settings.FlakeSizeMin:F1} - {_settings.FlakeSizeMax:F1} px";

            _settings.AccumulateOnWindows = ChkAccumulate.IsChecked ?? true;
            _settings.SnowOverhang = ChkSnowOverhang.IsChecked ?? true;
            _settings.ShowIcicles = ChkShowIcicles.IsChecked ?? true;
            _settings.IcicleDensity = SliderIcicleDensity.Value;
            TxtIcicleDensity.Text = $"{_settings.IcicleDensity:F1}x";
            _settings.AccumulateOnTaskbar = ChkTaskbar.IsChecked ?? false;
            ChkLeafTaskbar.IsChecked = _settings.AccumulateOnTaskbar;
            _settings.AccumulationSpeed = SliderAccum.Value;
            TxtAccum.Text = $"{_settings.AccumulationSpeed:F2}x";
            _settings.LimitSnowHeight = ChkLimitMaxHeight.IsChecked ?? true;
            _settings.MaxSnowHeight = SliderMaxHeight.Value;
            UpdateSnowHeightText();
            _settings.MeltRateSeconds = SliderMeltRate.Value;
            TxtMeltRate.Text = MeltText(_settings.MeltRateSeconds);
            _settings.FallOffOnWindowMove = ChkFallOff.IsChecked ?? true;

            // Őszi beállítások
            _settings.RainEnabled = ChkRain.IsChecked ?? true;
            _settings.RainCount = (int)SliderRainCount.Value;
            TxtRainCount.Text = $"{_settings.RainCount} db";
            _settings.RainFallSpeed = SliderRainSpeed.Value;
            TxtRainSpeed.Text = $"{_settings.RainFallSpeed:F1}x";

            _settings.LeafCount = (int)SliderLeafCount.Value;
            TxtLeafCount.Text = $"{_settings.LeafCount} db";

            _settings.LeafFallSpeed = SliderLeafSpeed.Value;
            TxtLeafSpeed.Text = $"{_settings.LeafFallSpeed:F1}x";

            _settings.LeafWindStrength = SliderLeafWind.Value;
            TxtLeafWind.Text = $"{_settings.LeafWindStrength:F1}";

            _settings.LeafSize = SliderLeafSize.Value;
            TxtLeafSize.Text = $"{_settings.LeafSize:F0} px";

            _settings.AccumulateLeavesOnWindows = ChkLeafAccumulate.IsChecked ?? true;
            _settings.LeafLifetimeSeconds = SliderLeafLife.Value;
            TxtLeafLife.Text = LeafLifeText(_settings.LeafLifetimeSeconds);

            // Egér
            _settings.MouseRepelEnabled = ChkMouseRepel.IsChecked ?? true;
            _settings.MouseWindPower = SliderMousePower.Value;
            TxtMousePower.Text = $"{_settings.MouseWindPower:F1}x";
            _settings.MouseSpeedSensitivity = SliderMouseSens.Value;
            TxtMouseSens.Text = $"{_settings.MouseSpeedSensitivity:F1}x";
            _settings.MouseRepelRadius = SliderMouseRadius.Value;
            TxtMouseRadius.Text = $"{_settings.MouseRepelRadius:F0} px";
            _settings.MouseSweepSnow = ChkSweep.IsChecked ?? true;

            // Rendszer
            _settings.TargetFps = CmbFps.SelectedIndex switch
            {
                1 => 120,
                2 => 60,
                3 => 30,
                _ => 0
            };
            _settings.AnimationQuality = CmbAnimQuality.SelectedIndex switch
            {
                1 => 1,
                2 => 0,
                _ => 2
            };
            _settings.PauseWhenCovered = ChkPauseCovered.IsChecked ?? true;
            _settings.StartWithWindows = ChkAutoStart.IsChecked ?? false;

            bool wantWallpaper = ChkUseWallpaper.IsChecked ?? true;
            if (wantWallpaper != _settings.UseSeasonalWallpaper)
            {
                _settings.UseSeasonalWallpaper = wantWallpaper;
                if (wantWallpaper)
                {
                    WallpaperManager.ApplyWallpaper(_settings.Season);
                }
                else
                {
                    WallpaperManager.RestoreOriginalWallpaper();
                }
            }

            _overlay.Reconfigure();
        }

        private void OnSaveClicked(object sender, RoutedEventArgs e)
        {
            _settings.Save();
            Close();
        }

        private void OnClearClicked(object sender, RoutedEventArgs e)
        {
            _overlay.ClearSnow();
        }

        private void OnResetDefaultsClicked(object sender, RoutedEventArgs e)
        {
            var def = new Settings();
            _settings.Season = def.Season;

            _settings.FlakeStyle = def.FlakeStyle;
            _settings.FlakeCount = def.FlakeCount;
            _settings.FallSpeed = def.FallSpeed;
            _settings.WindStrength = def.WindStrength;
            _settings.FlakeSizeMin = def.FlakeSizeMin;
            _settings.FlakeSizeMax = def.FlakeSizeMax;

            _settings.RainEnabled = def.RainEnabled;
            _settings.RainCount = def.RainCount;
            _settings.RainFallSpeed = def.RainFallSpeed;
            _settings.LeafCount = def.LeafCount;
            _settings.LeafFallSpeed = def.LeafFallSpeed;
            _settings.LeafWindStrength = def.LeafWindStrength;
            _settings.LeafSize = def.LeafSize;
            _settings.LeafLifetimeSeconds = def.LeafLifetimeSeconds;
            _settings.AccumulateLeavesOnWindows = def.AccumulateLeavesOnWindows;

            _settings.MouseRepelEnabled = def.MouseRepelEnabled;
            _settings.MouseWindPower = def.MouseWindPower;
            _settings.MouseSpeedSensitivity = def.MouseSpeedSensitivity;
            _settings.MouseRepelRadius = def.MouseRepelRadius;
            _settings.MouseSweepSnow = def.MouseSweepSnow;

            _settings.AccumulateOnWindows = def.AccumulateOnWindows;
            _settings.AccumulateOnTaskbar = def.AccumulateOnTaskbar;
            _settings.AccumulationSpeed = def.AccumulationSpeed;
            _settings.LimitSnowHeight = def.LimitSnowHeight;
            _settings.MaxSnowHeight = def.MaxSnowHeight;
            _settings.MeltRateSeconds = def.MeltRateSeconds;
            _settings.FallOffOnWindowMove = def.FallOffOnWindowMove;

            _settings.TargetFps = def.TargetFps;
            _settings.AnimationQuality = def.AnimationQuality;
            _settings.PauseWhenCovered = def.PauseWhenCovered;
            _settings.UseSeasonalWallpaper = def.UseSeasonalWallpaper;

            _isInitializing = true;
            LoadValues();
            _isInitializing = false;

            if (_settings.UseSeasonalWallpaper)
            {
                WallpaperManager.ApplyWallpaper(_settings.Season);
            }
            else
            {
                WallpaperManager.RestoreOriginalWallpaper();
            }

            _overlay.Reconfigure();
            _onSeasonChangedCallback?.Invoke();
        }
    }
}
