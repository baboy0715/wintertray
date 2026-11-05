using System;
using System.Windows;
using WinterTray.Engine;
using WinterTray.Native;
using WinterTray.Services;

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

        private string CountText(int count) => _settings.IsHungarian ? $"{count} db" : $"{count}";

        private string LeafLifeText(double seconds)
        {
            if (_settings.IsHungarian)
            {
                if (seconds <= 0) return "soha (nem tűnnek el)";
                if (Math.Abs(seconds - 120.0) < 0.5) return "120 mp (2 perc)";
                if (Math.Abs(seconds - 60.0) < 0.5) return "60 mp (1 perc)";
                if (Math.Abs(seconds - 180.0) < 0.5) return "180 mp (3 perc)";
                if (Math.Abs(seconds - 240.0) < 0.5) return "240 mp (4 perc)";
                if (Math.Abs(seconds - 300.0) < 0.5) return "300 mp (5 perc)";
                return $"{seconds:F0} mp";
            }
            else
            {
                if (seconds <= 0) return "never (stay forever)";
                if (Math.Abs(seconds - 120.0) < 0.5) return "120 s (2 min)";
                if (Math.Abs(seconds - 60.0) < 0.5) return "60 s (1 min)";
                if (Math.Abs(seconds - 180.0) < 0.5) return "180 s (3 min)";
                if (Math.Abs(seconds - 240.0) < 0.5) return "240 s (4 min)";
                if (Math.Abs(seconds - 300.0) < 0.5) return "300 s (5 min)";
                return $"{seconds:F0} s";
            }
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
                TxtMaxHeight.Text = _settings.IsHungarian ? "korlátlan (nincs limit)" : "unlimited (no limit)";
                SliderMaxHeight.IsEnabled = false;
            }
        }

        private void ApplyLocalization()
        {
            bool hu = _settings.IsHungarian;
            bool isAutumn = _settings.Season == AppSeason.Autumn;

            Title = hu ? "WinterTray - Beállítások" : "WinterTray - Settings";

            BtnFeedback.Content = hu ? "💬 Értékelés & Hibák" : "💬 Feedback & Bugs";
            BtnFeedback.ToolTip = hu ? "Kérdőív és hibabejelentő megnyitása" : "Open feedback and bug report form";

            TxtSecSeason.Text = hu ? "ÉVSZAK, TÉMA ÉS NYELV" : "SEASON, THEME & LANGUAGE";
            TxtSeasonTitle.Text = hu ? "Aktuális évszak" : "Current season";
            TxtSeasonSub.Text = hu
                ? "Válts azonnal a téli hóesés és az őszi falevelek között"
                : "Switch instantly between winter snowfall and autumn leaves";
            RbSeasonWinter.Content = hu ? "❄ Tél" : "❄ Winter";
            RbSeasonAutumn.Content = hu ? "🍂 Ősz" : "🍂 Autumn";

            TxtLangTitle.Text = hu ? "Nyelv" : "Language";
            TxtLangSub.Text = hu
                ? "Válaszd ki az alkalmazás felületének nyelvét"
                : "Select the application interface language";

            // Téli szekció
            TxtSecWinterVisual.Text = hu ? "❄ TÉLI HÓESÉS ÉS MEGJELENÉS" : "❄ WINTER SNOWFALL & APPEARANCE";
            TxtFlakeStyleLabel.Text = hu ? "Hópelyhek stílusa" : "Snowflake style";
            CmbStyleMixed.Content = hu ? "Vegyes (kristály + bolyhos)" : "Mixed (crystals + fluffy)";
            CmbStyleFluffy.Content = hu ? "Lágy, bolyhos pelyhek" : "Soft, fluffy flakes";
            CmbStyleCrystal.Content = hu ? "Részletes jégkristályok" : "Detailed ice crystals";

            TxtFlakeCountLabel.Text = hu ? "Hópelyhek száma" : "Snowflake count";
            TxtFallSpeedLabel.Text = hu ? "Esési sebesség" : "Fall speed";
            TxtWindLabel.Text = hu ? "Természetes szél (negatív = balra)" : "Natural wind (negative = left)";
            TxtSizeLabel.Text = hu ? "Hópelyhek mérete" : "Snowflake size";

            TxtSecWinterWindows.Text = hu ? "❄ TÉLI HÓFELHALMOZÓDÁS ÉS ABLAKOK" : "❄ SNOW ACCUMULATION & WINDOWS";
            ChkAccumulate.Content = hu
                ? "A hó megül a szabadon álló ablakok tetején"
                : "Snow accumulates on top of floating windows";
            ChkSnowOverhang.Content = hu
                ? "A hóréteg finoman ráhajoljon az ablak fejlécére (2-4 px puha perem)"
                : "Snow gently overhangs the window title bar (2-4 px soft edge)";
            ChkShowIcicles.Content = hu
                ? "Jégcsapok az ablakok alján és tetején (kattintással széttörhetőek!)"
                : "Icicles on window tops and bottoms (click to shatter!)";
            TxtIcicleDensityLabel.Text = hu ? "Jégcsapok sűrűsége" : "Icicle density";
            ChkTaskbar.Content = hu
                ? "A hó megül a tálca tetején is (alapból a tálca alatt eltűnik)"
                : "Snow also accumulates on the taskbar (otherwise disappears behind it)";
            TxtAccumLabel.Text = hu ? "Felhalmozódás gyorsasága" : "Accumulation speed";
            TxtMaxHeightLabel.Text = hu ? "Hóréteg maximális vastagsága" : "Maximum snow thickness";
            ChkLimitMaxHeight.Content = hu
                ? "Vastagság korlátozása (kikapcsolva a hóréteg korlátlanul nőhet)"
                : "Limit thickness (when unchecked, snow can grow infinitely)";
            ChkFallOff.Content = hu
                ? "A hó lezúdul, ha az ablakot elmozgatod, bezárod vagy kicsinyíted"
                : "Snow falls off when a window is moved, closed, or minimized";

            // Őszi szekció
            TxtSecAutumnRain.Text = hu ? "🍂 ŐSZI ESŐZÉS ÉS CSOBBANÁSOK" : "🍂 AUTUMN RAIN & SPLASHES";
            ChkRain.Content = hu
                ? "Őszi esőzés bekapcsolása (valósághű esőcsíkok és csobbanások az ablakokon)"
                : "Enable autumn rain (realistic rain streaks and splashes on windows)";
            TxtRainCountLabel.Text = hu ? "Esőcseppek száma" : "Raindrop count";
            TxtRainSpeedLabel.Text = hu ? "Eső esési sebessége" : "Rain fall speed";

            TxtSecAutumnLeaves.Text = hu ? "🍂 ŐSZI FALEVELEK ÉS ABLAKOK" : "🍂 AUTUMN LEAVES & WINDOWS";
            TxtLeafCountLabel.Text = hu
                ? "Szálló falevelek száma (nagyon ritka, finom hullás)"
                : "Falling leaves count (sparse, gentle fall)";
            TxtLeafSpeedLabel.Text = hu ? "Falevelek esési sebessége" : "Leaf fall speed";
            TxtLeafWindLabel.Text = hu ? "Természetes szél (negatív = balra)" : "Natural wind (negative = left)";
            TxtLeafSizeLabel.Text = hu ? "Falevelek mérete" : "Leaf size";
            ChkLeafAccumulate.Content = hu
                ? "A levelek ráfekszenek a szabad ablakok tetejére"
                : "Leaves rest on top of floating windows";
            ChkLeafTaskbar.Content = hu
                ? "A levelek és az eső a tálca tetején is megülnek/csobbannak (kikapcsolva a tálca mögé esnek)"
                : "Leaves and rain also land/splash on the taskbar (otherwise fall behind it)";
            TxtLeafLifeLabel.Text = hu
                ? "Megült levelek élettartama (0 = soha nem tűnnek el)"
                : "Rested leaf lifetime (0 = never disappears)";

            // Egér szekció
            TxtSecMouse.Text = hu ? "EGÉRMUTATÓ ÉS INTERAKCIÓ" : "MOUSE CURSOR & INTERACTION";
            ChkMouseRepel.Content = hu
                ? "Az egér elfújja a hulló részecskéket (szélörvény)"
                : "Mouse cursor blows away falling particles (wind vortex)";
            TxtMousePowerLabel.Text = hu ? "Szél erőssége" : "Wind power";
            TxtMouseSensLabel.Text = hu
                ? "Sebesség-érzékenység (gyorsabb egér = nagyobb hatás)"
                : "Speed sensitivity (faster mouse = stronger effect)";
            TxtMouseRadiusLabel.Text = hu ? "Alap hatótávolság" : "Base effect radius";
            ChkSweep.Content = hu
                ? "Egérrel lesöpörhető hó és lerepíthető falevelek az ablakokról"
                : "Sweep snow and blow leaves off windows with the mouse cursor";

            // Rendszer szekció
            TxtSecSystem.Text = hu ? "TELJESÍTMÉNY ÉS RENDSZER" : "PERFORMANCE & SYSTEM";
            TxtFpsLabel.Text = hu ? "Képfrissítés (FPS)" : "Frame rate (FPS)";
            CmbFpsNative.Content = hu ? "Monitor Natív (VSync / 120+ FPS)" : "Monitor Native (VSync / 120+ FPS)";
            CmbFps120.Content = hu ? "120 FPS (Szuper sima)" : "120 FPS (Super smooth)";
            CmbFps60.Content = hu ? "60 FPS (Alapértelmezett)" : "60 FPS (Default)";
            CmbFps30.Content = hu ? "30 FPS (Kímélő mód)" : "30 FPS (Power saving)";

            TxtAnimQualityLabel.Text = hu ? "Effektek és animációk minősége" : "Effects & animation quality";
            CmbQualityUltra.Content = hu ? "Teljes / Ultra (120 FPS sima részecskék)" : "Full / Ultra (120 FPS smooth particles)";
            CmbQualityMed.Content = hu ? "Közepes (Kiegyensúlyozott)" : "Medium (Balanced)";
            CmbQualityLow.Content = hu ? "Kikapcsolva (Gyenge gépekre)" : "Off (Low-end PCs)";

            ChkPauseCovered.Content = hu
                ? "Szünet, ha ablakok teljesen eltakarják az asztalt (nulla terhelés)"
                : "Pause when windows completely cover the desktop (zero load)";
            ChkAutoStart.Content = hu
                ? "Automatikus indulás a Windows indításakor"
                : "Start automatically with Windows";
            ChkUseWallpaper.Content = hu
                ? "Szezonális háttérkép beállítása (Tél: wallpaper.jpeg, Ősz: wallpaper-fall.jpeg)"
                : "Set seasonal desktop wallpaper (Winter: wallpaper.jpeg, Autumn: wallpaper-fall.jpeg)";

            // Frissítések szekció
            TxtSecUpdates.Text = hu ? "FRISSÍTÉSEK" : "UPDATES";
            ChkAutoUpdate.Content = hu
                ? "Frissítések automatikus keresése indításkor"
                : "Automatically check for updates on startup";
            TxtUpdateCheckTitle.Text = hu ? "WinterTray verzió és frissítés" : "WinterTray version and updates";
            BtnCheckUpdates.Content = hu ? "Keresés most" : "Check now";
            if (string.IsNullOrWhiteSpace(TxtUpdateStatus.Text) ||
                TxtUpdateStatus.Text.StartsWith("Current version") ||
                TxtUpdateStatus.Text.StartsWith("Aktuális verzió"))
            {
                TxtUpdateStatus.Text = hu
                    ? $"Aktuális verzió: v{UpdateManager.CurrentVersionDisplay}"
                    : $"Current version: v{UpdateManager.CurrentVersionDisplay}";
            }

            BtnReset.Content = hu ? "Alaphelyzet" : "Reset defaults";
            BtnSave.Content = hu ? "Mentés és bezárás" : "Save & Close";

            // Dinamikus érték-szövegek frissítése
            TxtFlakeCount.Text = CountText(_settings.FlakeCount);
            UpdateSnowHeightText();
            TxtRainCount.Text = CountText(_settings.RainCount);
            TxtLeafCount.Text = CountText(_settings.LeafCount);
            TxtLeafLife.Text = LeafLifeText(_settings.LeafLifetimeSeconds);

            UpdateSeasonUI(isAutumn);
        }

        private void LoadValues()
        {
            // Nyelv
            CmbLanguage.SelectedIndex = _settings.IsHungarian ? 1 : 0;
            ApplyLocalization();

            // Szezon
            bool isAutumn = _settings.Season == AppSeason.Autumn;
            RbSeasonWinter.IsChecked = !isAutumn;
            RbSeasonAutumn.IsChecked = isAutumn;
            UpdateSeasonUI(isAutumn);

            // Téli beállítások
            CmbStyle.SelectedIndex = Math.Clamp(_settings.FlakeStyle, 0, 2);

            SliderFlakeCount.Value = _settings.FlakeCount;
            TxtFlakeCount.Text = CountText(_settings.FlakeCount);

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
            ChkFallOff.IsChecked = _settings.FallOffOnWindowMove;

            // Őszi beállítások
            ChkRain.IsChecked = _settings.RainEnabled;
            SliderRainCount.Value = _settings.RainCount;
            TxtRainCount.Text = CountText(_settings.RainCount);
            SliderRainSpeed.Value = _settings.RainFallSpeed;
            TxtRainSpeed.Text = $"{_settings.RainFallSpeed:F1}x";

            SliderLeafCount.Value = _settings.LeafCount;
            TxtLeafCount.Text = CountText(_settings.LeafCount);

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
            ChkAutoUpdate.IsChecked = _settings.CheckForUpdatesOnStartup;
        }

        private void UpdateSeasonUI(bool isAutumn)
        {
            bool hu = _settings.IsHungarian;
            PanelWinter.Visibility = isAutumn ? Visibility.Collapsed : Visibility.Visible;
            PanelAutumn.Visibility = isAutumn ? Visibility.Visible : Visibility.Collapsed;

            TxtHeaderIcon.Text = isAutumn ? "🍂" : "❄";
            TxtHeaderIcon.Foreground = isAutumn
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 167, 38))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(96, 205, 255));

            TxtHeaderDesc.Text = hu
                ? (isAutumn
                    ? "Őszi falevelek hullása és megülése az ablakokon"
                    : "Téli hóesés és hófelhalmozódás az ablakokon")
                : (isAutumn
                    ? "Autumn falling leaves and rain on your windows"
                    : "Winter snowfall and snow accumulation on your windows");

            BtnClear.Content = hu
                ? (isAutumn ? "Levelek lesöprése" : "Hó letakarítása")
                : (isAutumn ? "Sweep leaves" : "Clear snow");
        }

        private void OnLanguageChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            _settings.Language = CmbLanguage.SelectedIndex == 1 ? "hu" : "en";
            ApplyLocalization();
            _settings.Save();
            _onSeasonChangedCallback?.Invoke();
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
            TxtFlakeCount.Text = CountText(_settings.FlakeCount);

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
            _settings.FallOffOnWindowMove = ChkFallOff.IsChecked ?? true;

            // Őszi beállítások
            _settings.RainEnabled = ChkRain.IsChecked ?? true;
            _settings.RainCount = (int)SliderRainCount.Value;
            TxtRainCount.Text = CountText(_settings.RainCount);
            _settings.RainFallSpeed = SliderRainSpeed.Value;
            TxtRainSpeed.Text = $"{_settings.RainFallSpeed:F1}x";

            _settings.LeafCount = (int)SliderLeafCount.Value;
            TxtLeafCount.Text = CountText(_settings.LeafCount);

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
            _settings.CheckForUpdatesOnStartup = ChkAutoUpdate.IsChecked ?? true;

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

        private void OnFeedbackClicked(object sender, RoutedEventArgs e)
        {
            App.OpenFeedbackUrl();
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
            _settings.SnowOverhang = def.SnowOverhang;
            _settings.ShowIcicles = def.ShowIcicles;
            _settings.IcicleDensity = def.IcicleDensity;
            _settings.AccumulateOnTaskbar = def.AccumulateOnTaskbar;
            _settings.AccumulationSpeed = def.AccumulationSpeed;
            _settings.LimitSnowHeight = def.LimitSnowHeight;
            _settings.MaxSnowHeight = def.MaxSnowHeight;
            _settings.FallOffOnWindowMove = def.FallOffOnWindowMove;

            _settings.TargetFps = def.TargetFps;
            _settings.AnimationQuality = def.AnimationQuality;
            _settings.PauseWhenCovered = def.PauseWhenCovered;
            _settings.UseSeasonalWallpaper = def.UseSeasonalWallpaper;
            _settings.CheckForUpdatesOnStartup = def.CheckForUpdatesOnStartup;

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

        private async void OnCheckUpdatesClicked(object sender, RoutedEventArgs e)
        {
            bool hu = _settings.IsHungarian;
            BtnCheckUpdates.IsEnabled = false;
            TxtUpdateStatus.Text = hu ? "Frissítések keresése folyamatban..." : "Checking for updates...";

            try
            {
                var update = await UpdateManager.CheckForUpdateAsync(silent: false);
                if (update != null && update.IsNewer)
                {
                    TxtUpdateStatus.Text = hu
                        ? $"Új verzió érhető el: v{update.VersionString}!"
                        : $"New version available: v{update.VersionString}!";

                    var updateWin = new UpdateWindow(update, hu) { Owner = this };
                    updateWin.ShowDialog();
                }
                else if (update != null)
                {
                    TxtUpdateStatus.Text = hu
                        ? $"A legfrissebb verziót használod (v{UpdateManager.CurrentVersionDisplay}) ✔"
                        : $"You are using the latest version (v{UpdateManager.CurrentVersionDisplay}) ✔";
                }
                else
                {
                    TxtUpdateStatus.Text = hu
                        ? "Nem sikerült ellenőrizni (nincs kapcsolat vagy nincs újabb kiadás)."
                        : "Could not check for updates (no connection or no newer release).";
                }
            }
            catch
            {
                TxtUpdateStatus.Text = hu
                    ? "Nem sikerült ellenőrizni (nincs internetkapcsolat)."
                    : "Could not check for updates (no internet connection).";
            }
            finally
            {
                BtnCheckUpdates.IsEnabled = true;
            }
        }
    }
}
