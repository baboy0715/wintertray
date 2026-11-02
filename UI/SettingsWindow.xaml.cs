using System;
using System.Windows;

namespace WinterTray.UI
{
    public partial class SettingsWindow : Window
    {
        private readonly Settings _settings;
        private readonly SnowOverlayWindow _overlay;
        private bool _isInitializing = true;

        public SettingsWindow(Settings settings, SnowOverlayWindow overlay)
        {
            _settings = settings;
            _overlay = overlay;
            InitializeComponent();

            LoadValues();
            _isInitializing = false;
        }

        private static string MeltText(double seconds) => seconds <= 0 ? "soha" : $"{seconds:F0} mp";

        private void LoadValues()
        {
            CmbStyle.SelectedIndex = Math.Clamp(_settings.FlakeStyle, 0, 2);

            SliderFlakeCount.Value = _settings.FlakeCount;
            TxtFlakeCount.Text = $"{_settings.FlakeCount} db";

            SliderFallSpeed.Value = _settings.FallSpeed;
            TxtFallSpeed.Text = $"{_settings.FallSpeed:F1}x";

            SliderWind.Value = _settings.WindStrength;
            TxtWind.Text = $"{_settings.WindStrength:F1}";

            SliderSize.Value = _settings.FlakeSizeMax;
            TxtSize.Text = $"{_settings.FlakeSizeMin:F1} - {_settings.FlakeSizeMax:F1} px";

            ChkMouseRepel.IsChecked = _settings.MouseRepelEnabled;
            SliderMousePower.Value = _settings.MouseWindPower;
            TxtMousePower.Text = $"{_settings.MouseWindPower:F1}x";
            SliderMouseSens.Value = _settings.MouseSpeedSensitivity;
            TxtMouseSens.Text = $"{_settings.MouseSpeedSensitivity:F1}x";
            SliderMouseRadius.Value = _settings.MouseRepelRadius;
            TxtMouseRadius.Text = $"{_settings.MouseRepelRadius:F0} px";
            ChkSweep.IsChecked = _settings.MouseSweepSnow;

            ChkAccumulate.IsChecked = _settings.AccumulateOnWindows;
            ChkSnowOverhang.IsChecked = _settings.SnowOverhang;
            ChkShowIcicles.IsChecked = _settings.ShowIcicles;
            SliderIcicleDensity.Value = _settings.IcicleDensity;
            TxtIcicleDensity.Text = $"{_settings.IcicleDensity:F1}x";
            ChkTaskbar.IsChecked = _settings.AccumulateOnTaskbar;
            SliderAccum.Value = _settings.AccumulationSpeed;
            TxtAccum.Text = $"{_settings.AccumulationSpeed:F2}x";
            SliderMaxHeight.Value = _settings.MaxSnowHeight;
            TxtMaxHeight.Text = $"{_settings.MaxSnowHeight:F0} px";
            SliderMeltRate.Value = _settings.MeltRateSeconds;
            TxtMeltRate.Text = MeltText(_settings.MeltRateSeconds);
            ChkFallOff.IsChecked = _settings.FallOffOnWindowMove;

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
            ChkUseWallpaper.IsChecked = _settings.UseWinterWallpaper;
        }

        private void OnSettingChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

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

            _settings.MouseRepelEnabled = ChkMouseRepel.IsChecked ?? true;
            _settings.MouseWindPower = SliderMousePower.Value;
            TxtMousePower.Text = $"{_settings.MouseWindPower:F1}x";
            _settings.MouseSpeedSensitivity = SliderMouseSens.Value;
            TxtMouseSens.Text = $"{_settings.MouseSpeedSensitivity:F1}x";
            _settings.MouseRepelRadius = SliderMouseRadius.Value;
            TxtMouseRadius.Text = $"{_settings.MouseRepelRadius:F0} px";
            _settings.MouseSweepSnow = ChkSweep.IsChecked ?? true;

            _settings.AccumulateOnWindows = ChkAccumulate.IsChecked ?? true;
            _settings.SnowOverhang = ChkSnowOverhang.IsChecked ?? true;
            _settings.ShowIcicles = ChkShowIcicles.IsChecked ?? true;
            _settings.IcicleDensity = SliderIcicleDensity.Value;
            TxtIcicleDensity.Text = $"{_settings.IcicleDensity:F1}x";
            _settings.AccumulateOnTaskbar = ChkTaskbar.IsChecked ?? false;
            _settings.AccumulationSpeed = SliderAccum.Value;
            TxtAccum.Text = $"{_settings.AccumulationSpeed:F2}x";
            _settings.MaxSnowHeight = SliderMaxHeight.Value;
            TxtMaxHeight.Text = $"{_settings.MaxSnowHeight:F0} px";
            _settings.MeltRateSeconds = SliderMeltRate.Value;
            TxtMeltRate.Text = MeltText(_settings.MeltRateSeconds);
            _settings.FallOffOnWindowMove = ChkFallOff.IsChecked ?? true;

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
            if (wantWallpaper != _settings.UseWinterWallpaper)
            {
                _settings.UseWinterWallpaper = wantWallpaper;
                if (wantWallpaper)
                {
                    Native.WallpaperManager.ApplyWinterWallpaper();
                }
                else
                {
                    Native.WallpaperManager.RestoreOriginalWallpaper();
                }
            }

            _overlay.Reconfigure();
        }

        private void OnSaveClicked(object sender, RoutedEventArgs e)
        {
            _settings.Save();
            Close();
        }

        private void OnClearSnowClicked(object sender, RoutedEventArgs e)
        {
            _overlay.ClearSnow();
        }

        private void OnResetDefaultsClicked(object sender, RoutedEventArgs e)
        {
            var def = new Settings();
            _settings.FlakeStyle = def.FlakeStyle;
            _settings.FlakeCount = def.FlakeCount;
            _settings.FallSpeed = def.FallSpeed;
            _settings.WindStrength = def.WindStrength;
            _settings.FlakeSizeMin = def.FlakeSizeMin;
            _settings.FlakeSizeMax = def.FlakeSizeMax;

            _settings.MouseRepelEnabled = def.MouseRepelEnabled;
            _settings.MouseWindPower = def.MouseWindPower;
            _settings.MouseSpeedSensitivity = def.MouseSpeedSensitivity;
            _settings.MouseRepelRadius = def.MouseRepelRadius;
            _settings.MouseSweepSnow = def.MouseSweepSnow;

            _settings.AccumulateOnWindows = def.AccumulateOnWindows;
            _settings.AccumulateOnTaskbar = def.AccumulateOnTaskbar;
            _settings.AccumulationSpeed = def.AccumulationSpeed;
            _settings.MaxSnowHeight = def.MaxSnowHeight;
            _settings.MeltRateSeconds = def.MeltRateSeconds;
            _settings.FallOffOnWindowMove = def.FallOffOnWindowMove;

            _settings.TargetFps = def.TargetFps;
            _settings.AnimationQuality = def.AnimationQuality;
            _settings.PauseWhenCovered = def.PauseWhenCovered;
            _settings.UseWinterWallpaper = def.UseWinterWallpaper;

            _isInitializing = true;
            LoadValues();
            _isInitializing = false;

            _overlay.Reconfigure();
        }
    }
}
