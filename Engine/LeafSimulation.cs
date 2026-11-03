using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using WinterTray.Native;

namespace WinterTray.Engine
{
    /// <summary>
    /// Az őszi szimuláció motorja:
    /// - Őszi esőzés finom esőcsíkokkal, felületi csobbanással az ablakok tetején és az asztalon.
    /// - Vízréteg / pocsolya képződés az ablakok felső peremén csillogó fényvonallal és dinamikus hullámzással.
    /// - Valósághű vízcseppek lecsorgása az ablakok bal és jobb függőleges szegélyén felületi feszültséggel és torpanással.
    /// - Dinamikus vízhullám fizika: ablakmozgatáskor (tehetetlenség), kicsinyítéskor és kéz/kurzor sepréskor a víz hullámzik,
    ///   és a hullám lemossa / elsöpri a faleveleket az ablakról!
    /// - Helyes renderelési sorrend: a csobbanások és a vízréteg a levelek ALATT helyezkednek el, nem vágják át a faleveleket.
    /// - Ritka, gyönyörűen hulló őszi falevelek szinuszos lebegéssel és forgással.
    /// - Szigorúan zárt levélpopuláció, 0..300s élettartam, 120 FPS vajpuha fade out.
    /// </summary>
    public sealed class LeafSimulation
    {
        private const double LedgeMinTop = 28.0;
        private const int MaxSplashes = 70;

        private readonly Settings _s;
        private readonly Random _rnd = new();

        // Falevelek
        private readonly List<LeafFlake> _leaves = new();
        private readonly List<LandedLeaf> _landedLeaves = new();

        // Vízréteg ablakonként és lefolyó cseppek
        private readonly Dictionary<IntPtr, WindowWaterFilm> _waterFilms = new();
        private readonly List<RunningDrop> _runningDrops = new();

        // Eső és csobbanások
        private readonly List<RainDrop> _rainDrops = new();
        private readonly List<RainSplash> _splashes = new();

        // Fagyasztott WPF tollak és ecsetek (0 GC, maximális teljesítmény)
        private readonly Pen _rainPen;
        private readonly Brush _splashDropBrush;
        private readonly Pen _waterHighlightPen;
        private readonly Brush _waterFilmBrush;
        private readonly Brush _runningDropBrush;
        private readonly Pen _runningDropPen;

        private double _time;
        private double _lastWidth = 1920;
        private double _lastHeight = 1080;

        public double Width => _lastWidth;
        public double Height => _lastHeight;

        public LeafSimulation(Settings settings)
        {
            _s = settings;
            LeafSprites.Init();

            // Esőcsíkok áttetsző ezüstkék tolla
            var rainBrush = new SolidColorBrush(Color.FromArgb(145, 190, 215, 245));
            rainBrush.Freeze();
            var rainPen = new Pen(rainBrush, 1.25)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            rainPen.Freeze();
            _rainPen = rainPen;

            // Csobbanás mikrocseppek
            var dropBrush = new SolidColorBrush(Color.FromArgb(190, 225, 245, 255));
            dropBrush.Freeze();
            _splashDropBrush = dropBrush;

            // Vízréteg felületi csillogása (meniszkusz fényvonal)
            var waterHighBrush = new SolidColorBrush(Color.FromArgb(160, 225, 248, 255));
            waterHighBrush.Freeze();
            var waterHighPen = new Pen(waterHighBrush, 1.1)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            waterHighPen.Freeze();
            _waterHighlightPen = waterHighPen;

            // Vízréteg teste az ablak tetején
            var waterBody = new SolidColorBrush(Color.FromArgb(60, 160, 210, 245));
            waterBody.Freeze();
            _waterFilmBrush = waterBody;

            // Ablak szélén lecsorgó cseppek feje
            var runningHeadBrush = new SolidColorBrush(Color.FromArgb(200, 220, 245, 255));
            runningHeadBrush.Freeze();
            _runningDropBrush = runningHeadBrush;

            // Ablak szélén lecsorgó cseppek uszálya
            var runningTrailBrush = new SolidColorBrush(Color.FromArgb(140, 180, 220, 250));
            runningTrailBrush.Freeze();
            var runningTrailPen = new Pen(runningTrailBrush, 1.35)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            runningTrailPen.Freeze();
            _runningDropPen = runningTrailPen;
        }

        public void Resize(double width, double height)
        {
            _lastWidth = width;
            _lastHeight = height;
            ResetSimulation();
        }

        public void Reconfigure()
        {
            int targetLeafCount = Math.Clamp(_s.LeafCount, 1, 40);
            while (_leaves.Count + _landedLeaves.Count < targetLeafCount)
            {
                _leaves.Add(NewLeaf(true));
            }
            while (_leaves.Count + _landedLeaves.Count > targetLeafCount && _leaves.Count > 0)
            {
                _leaves.RemoveAt(_leaves.Count - 1);
            }

            int targetRain = _s.RainEnabled ? Math.Clamp(_s.RainCount, 30, 500) : 0;
            while (_rainDrops.Count < targetRain) _rainDrops.Add(NewRainDrop(true));
            if (_rainDrops.Count > targetRain) _rainDrops.RemoveRange(targetRain, _rainDrops.Count - targetRain);
        }

        public void ResetLeaves()
        {
            ResetSimulation();
        }

        public void ResetSimulation()
        {
            _leaves.Clear();
            _landedLeaves.Clear();
            _rainDrops.Clear();
            _splashes.Clear();
            _runningDrops.Clear();
            _waterFilms.Clear();

            int targetLeafCount = Math.Clamp(_s.LeafCount, 1, 40);
            for (int i = 0; i < targetLeafCount; i++)
            {
                _leaves.Add(NewLeaf(true));
            }

            int targetRain = _s.RainEnabled ? Math.Clamp(_s.RainCount, 30, 500) : 0;
            for (int i = 0; i < targetRain; i++)
            {
                _rainDrops.Add(NewRainDrop(true));
            }
        }

        public void ClearAll()
        {
            for (int i = 0; i < _landedLeaves.Count; i++)
            {
                var l = _landedLeaves[i];
                SpawnFallingLeafFromLanded(l, l.RelX, l.RelY, (_rnd.NextDouble() - 0.5) * 40.0, -15.0);
            }
            _landedLeaves.Clear();
            _splashes.Clear();
            _runningDrops.Clear();
            _waterFilms.Clear();
        }

        public void OnWindowClosed(IntPtr hwnd, Rect oldRect)
        {
            _waterFilms.Remove(hwnd);
            _runningDrops.RemoveAll(d => d.HWnd == hwnd);

            for (int i = _landedLeaves.Count - 1; i >= 0; i--)
            {
                var l = _landedLeaves[i];
                if (l.HWnd == hwnd)
                {
                    double worldX = oldRect.Left + l.RelX;
                    double worldY = oldRect.Top + l.RelY;
                    SpawnFallingLeafFromLanded(l, worldX, worldY, (_rnd.NextDouble() - 0.5) * 40.0, 15.0 + _rnd.NextDouble() * 20.0);
                    _landedLeaves.RemoveAt(i);
                }
            }
        }

        public void OnWindowMoved(IntPtr hwnd, Rect oldRect, Rect newRect, double vx, double vy)
        {
            if (!_waterFilms.TryGetValue(hwnd, out var water))
            {
                water = new WindowWaterFilm { HWnd = hwnd, Level = 1.0 };
                _waterFilms[hwnd] = water;
            }

            double speed = Math.Sqrt(vx * vx + vy * vy);
            double dw = newRect.Width - oldRect.Width;

            // 1. Tehetetlenségi vízhullám: az ablak mozgásával ellentétes irányban torlódik a víz
            water.SloshVx = Math.Clamp(water.SloshVx - vx * 0.45, -700.0, 700.0);
            water.WaveOffset = Math.Clamp(water.WaveOffset - vx * 0.05, -35.0, 35.0);

            // 2. Ablak átméretezése / kicsinyítése: a víz felgyűrődik és a széleken lefolyik
            if (Math.Abs(dw) > 1.5)
            {
                water.SloshVx = Math.Clamp(water.SloshVx + (oldRect.Width - newRect.Width) * 4.5, -600.0, 600.0);
                water.SideDripAcc += 0.35;
            }

            // 3. Falevelek lemosása átméretezéskor és gyors rántáskor
            for (int i = _landedLeaves.Count - 1; i >= 0; i--)
            {
                var l = _landedLeaves[i];
                if (l.HWnd != hwnd) continue;

                double worldX = newRect.Left + l.RelX;
                double worldY = newRect.Top + l.RelY;

                // Kicsinyítés: ha a szélt behúzták a falevél alól
                if (l.RelX > newRect.Width - 12.0 || l.RelX < 12.0)
                {
                    double pushDir = l.RelX < 12.0 ? -1.0 : 1.0;
                    double pushVx = pushDir * (35.0 + _rnd.NextDouble() * 45.0) + vx * 0.25;
                    SpawnFallingLeafFromLanded(l, worldX, worldY, pushVx, 15.0 + _rnd.NextDouble() * 20.0);
                    _landedLeaves.RemoveAt(i);
                    continue;
                }

                // Gyors rántás / mozgás miatti lemosódás
                if (speed > 220.0 && _rnd.NextDouble() < 0.35)
                {
                    double pushVx = water.SloshVx * 0.35 + vx * 0.25 + (_rnd.NextDouble() - 0.5) * 35.0;
                    SpawnFallingLeafFromLanded(l, worldX, worldY, pushVx, vy * 0.20 - 10.0);
                    _landedLeaves.RemoveAt(i);
                }
            }
        }

        private void SpawnFallingLeafFromLanded(LandedLeaf l, double worldX, double worldY, double pushVx, double pushVy)
        {
            _leaves.Add(new LeafFlake
            {
                X = worldX,
                Y = worldY,
                PrevY = worldY,
                Size = l.Size,
                Opacity = 1.0,
                Depth = 0.7,
                Rot = l.Rot,
                RotSpeed = (_rnd.NextDouble() - 0.5) * 5.0,
                SwayPhase = _rnd.NextDouble() * Math.PI * 2.0,
                SwaySpeed = 1.4 + _rnd.NextDouble() * 1.5,
                SwayAmp = 18.0 + _rnd.NextDouble() * 22.0,
                FallVy = (40.0 + _rnd.NextDouble() * 35.0) * _s.LeafFallSpeed,
                Vx = pushVx,
                Vy = pushVy,
                Variant = l.Variant,
                ColorVariant = l.ColorVariant
            });
        }

        public void Update(
            double dt,
            CursorState cursor,
            List<TrackedWindow> windows,
            Func<double, double, IntPtr, bool> isCoveredByHigher,
            Func<double, double, bool> isInsideAnyWindow)
        {
            _time += dt;

            // 1. Kurzorral seprés az ablakok tetején (hullámkeltés a vízben)
            UpdateCursorWaterInteraction(cursor, windows);

            // 2. Vízréteg és szegélyeken lefolyó vízcseppek szimulációja
            UpdateWaterAndRunningDrops(dt, windows);

            // 3. Esőcseppek és csobbanások
            if (_s.RainEnabled)
            {
                UpdateRain(dt, windows, isCoveredByHigher);
            }

            // 4. Megült levelek (vízhullám által mosás + kurzor seprés + 120 FPS fade out)
            UpdateLandedLeaves(dt, cursor, windows);

            // 5. Hulló falevelek
            UpdateFallingLeaves(dt, cursor, windows, isCoveredByHigher, isInsideAnyWindow);
        }

        // ------------------------------------------------------------------ VÍZRÉTEG ÉS HULLÁMOK

        private void UpdateCursorWaterInteraction(CursorState cursor, List<TrackedWindow> windows)
        {
            if (!cursor.Valid || cursor.Speed < 30.0) return;

            for (int i = 0; i < windows.Count; i++)
            {
                var win = windows[i];
                if (win.IsTaskbar && !_s.AccumulateOnTaskbar) continue;
                if (win.Rect.Top < LedgeMinTop) continue;

                if (cursor.Y >= win.Rect.Top - 18.0 && cursor.Y <= win.Rect.Top + 28.0 &&
                    cursor.X >= win.Rect.Left - 10.0 && cursor.X <= win.Rect.Right + 10.0)
                {
                    if (!_waterFilms.TryGetValue(win.HWnd, out var water))
                    {
                        water = new WindowWaterFilm { HWnd = win.HWnd, Level = 1.0 };
                        _waterFilms[win.HWnd] = water;
                    }

                    // A kéz / kurzor hullámot indít a vízben
                    water.SloshVx = Math.Clamp(water.SloshVx + cursor.Vx * 0.40, -600.0, 600.0);
                    water.WaveOffset = Math.Clamp(water.WaveOffset + Math.Sign(cursor.Vx) * 12.0, -35.0, 35.0);
                    water.SideDripAcc += 0.20;
                }
            }
        }

        private void UpdateWaterAndRunningDrops(double dt, List<TrackedWindow> windows)
        {
            // 1. Vízréteg párolgása és lecsengése ablakonként
            for (int wIdx = 0; wIdx < windows.Count; wIdx++)
            {
                var win = windows[wIdx];
                if (!_waterFilms.TryGetValue(win.HWnd, out var water))
                {
                    water = new WindowWaterFilm { HWnd = win.HWnd, Level = _s.RainEnabled ? 1.0 : 0.0 };
                    _waterFilms[win.HWnd] = water;
                }

                // Hullámsebesség és kitérés természetes csillapítása
                water.SloshVx *= Math.Exp(-3.2 * dt);
                water.WaveOffset *= Math.Exp(-4.0 * dt);

                // Vízréteg szintje
                if (!_s.RainEnabled)
                {
                    water.Level = Math.Max(0.0, water.Level - 0.25 * dt);
                }
                else
                {
                    water.Level = Math.Max(0.5, water.Level - 0.04 * dt);
                }

                // Ha van víz a felszínen, a szélén lecsorog
                if (water.Level > 0.4 && win.Rect.Height > 60)
                {
                    water.SideDripAcc += dt * (0.5 + water.Level * 0.7 + Math.Abs(water.SloshVx) * 0.015);
                    if (water.SideDripAcc >= 1.0 && _runningDrops.Count < 45)
                    {
                        water.SideDripAcc = 0;

                        bool isLeft;
                        if (water.SloshVx < -15.0) isLeft = true;
                        else if (water.SloshVx > 15.0) isLeft = false;
                        else isLeft = _rnd.NextDouble() < 0.5;

                        _runningDrops.Add(new RunningDrop
                        {
                            HWnd = win.HWnd,
                            IsLeftEdge = isLeft,
                            OffsetX = (_rnd.NextDouble() - 0.5) * 1.5,
                            RelY = 0,
                            Vy = 70.0 + _rnd.NextDouble() * 110.0,
                            Length = 8.0 + _rnd.NextDouble() * 12.0,
                            Thickness = 1.4 + _rnd.NextDouble() * 0.8,
                            Alpha = 0.50 + _rnd.NextDouble() * 0.35,
                            HesitateTimer = _rnd.NextDouble() * 0.25
                        });
                    }
                }
            }

            // 2. Ablak oldalán lecsorgó cseppek (Running Drops)
            for (int i = _runningDrops.Count - 1; i >= 0; i--)
            {
                var drop = _runningDrops[i];
                int wIdx = windows.FindIndex(w => w.HWnd == drop.HWnd);
                if (wIdx < 0)
                {
                    _runningDrops.RemoveAt(i);
                    continue;
                }

                var win = windows[wIdx];

                // Felületi feszültség: a csepp néha megtorpan, majd megcsúszik
                drop.HesitateTimer -= dt;
                if (drop.HesitateTimer > 0)
                {
                    drop.RelY += drop.Vy * 0.20 * dt;
                }
                else
                {
                    drop.Vy += 85.0 * dt;
                    drop.RelY += drop.Vy * dt;

                    if (_rnd.NextDouble() < 0.04)
                    {
                        drop.HesitateTimer = 0.08 + _rnd.NextDouble() * 0.16;
                        drop.Vy = Math.Max(40.0, drop.Vy * 0.45);
                    }
                }

                if (drop.RelY >= win.Rect.Height - 4.0)
                {
                    _runningDrops.RemoveAt(i);
                }
                else
                {
                    _runningDrops[i] = drop;
                }
            }
        }

        // ------------------------------------------------------------------ ESŐ ÉS CSOBBANÁSOK

        private void UpdateRain(
            double dt,
            List<TrackedWindow> windows,
            Func<double, double, IntPtr, bool> isCoveredByHigher)
        {
            double speedMult = Math.Clamp(_s.RainFallSpeed, 0.4, 2.5);
            double wind = _s.LeafWindStrength * 80.0;

            // Csobbanások öregítése
            for (int i = _splashes.Count - 1; i >= 0; i--)
            {
                var sp = _splashes[i];
                sp.Age += dt;
                if (sp.Age >= sp.MaxLife)
                {
                    _splashes.RemoveAt(i);
                }
                else
                {
                    _splashes[i] = sp;
                }
            }

            // Hulló esőcseppek
            for (int i = 0; i < _rainDrops.Count; i++)
            {
                var drop = _rainDrops[i];
                double prevY = drop.Y;
                double dy = drop.Vy * speedMult * dt;
                drop.Y += dy;
                drop.X += wind * dt;

                bool hit = false;
                double hitSurfaceY = 0;
                IntPtr hitHwnd = IntPtr.Zero;

                // Ütközés vizsgálata az ablakok tetejével
                for (int wIdx = 0; wIdx < windows.Count; wIdx++)
                {
                    var win = windows[wIdx];
                    if (win.IsTaskbar && !_s.AccumulateOnTaskbar) continue;
                    if (win.Rect.Top < LedgeMinTop) continue;
                    if (drop.X < win.Rect.Left || drop.X > win.Rect.Right) continue;

                    double surfaceY = win.IsTaskbar
                        ? win.Rect.Top
                        : win.Rect.Top + SnowBank.GetYBase(drop.X - win.Rect.Left, win.Rect.Width, true);

                    if (drop.Y >= surfaceY && prevY <= surfaceY + 16)
                    {
                        if (!isCoveredByHigher(drop.X, surfaceY - 2, win.HWnd))
                        {
                            hit = true;
                            hitSurfaceY = surfaceY;
                            hitHwnd = win.HWnd;
                            break;
                        }
                    }
                }

                // Tálca teteje vagy asztal alja
                if (!hit)
                {
                    if (_s.AccumulateOnTaskbar && drop.Y >= Height - 40)
                    {
                        hit = true;
                        hitSurfaceY = Height - 40;
                    }
                    else if (!_s.AccumulateOnTaskbar && drop.Y >= Height + 10)
                    {
                        hit = true;
                        hitSurfaceY = Height;
                    }
                }

                if (hit)
                {
                    // Víz gyűlik az ablak tetején
                    if (hitHwnd != IntPtr.Zero)
                    {
                        if (!_waterFilms.TryGetValue(hitHwnd, out var water))
                        {
                            water = new WindowWaterFilm { HWnd = hitHwnd, Level = 1.0 };
                            _waterFilms[hitHwnd] = water;
                        }
                        water.Level = Math.Min(3.8, water.Level + 0.04);
                    }

                    // FONTOS: Megvizsgáljuk, hogy az esőcsepp épp egy megült falevélre esik-e
                    // Ha a levélre esik, NEM rajzolunk zavaró csobbanókarikát a levél testére!
                    bool hitsLeaf = false;
                    if (hitHwnd != IntPtr.Zero)
                    {
                        for (int k = 0; k < _landedLeaves.Count; k++)
                        {
                            var ll = _landedLeaves[k];
                            if (ll.HWnd == hitHwnd)
                            {
                                int winIdx = windows.FindIndex(w => w.HWnd == hitHwnd);
                                if (winIdx >= 0)
                                {
                                    double leafWorldX = windows[winIdx].Rect.Left + ll.RelX;
                                    if (Math.Abs(drop.X - leafWorldX) < ll.Size * 0.55)
                                    {
                                        hitsLeaf = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (!hitsLeaf && _splashes.Count < MaxSplashes)
                    {
                        _splashes.Add(new RainSplash
                        {
                            X = drop.X,
                            Y = hitSurfaceY,
                            Age = 0,
                            MaxLife = 0.14 + _rnd.NextDouble() * 0.06,
                            Radius = 3.5 + _rnd.NextDouble() * 4.5
                        });
                    }

                    _rainDrops[i] = NewRainDrop(false);
                }
                else if (drop.Y > Height + 30 || drop.X < -50 || drop.X > Width + 50)
                {
                    _rainDrops[i] = NewRainDrop(false);
                }
                else
                {
                    _rainDrops[i] = drop;
                }
            }
        }

        private RainDrop NewRainDrop(bool initial)
        {
            double x = _rnd.NextDouble() * (Width + 120) - 60;
            double y = initial ? _rnd.NextDouble() * Height : -20 - _rnd.NextDouble() * 40;
            return new RainDrop
            {
                X = x,
                Y = y,
                Vy = 720.0 + _rnd.NextDouble() * 320.0,
                Length = 12.0 + _rnd.NextDouble() * 12.0,
                Alpha = 0.55 + _rnd.NextDouble() * 0.40
            };
        }

        // ------------------------------------------------------------------ MEGÜLT ÉS HULLÓ FALEVELEK

        private void UpdateLandedLeaves(double dt, CursorState cur, List<TrackedWindow> windows)
        {
            double maxLife = _s.LeafLifetimeSeconds;
            const double fadeDuration = 5.0; // 5 másodperces sima fade out

            bool checkMouse = cur.Valid;
            double cursorSpeed = cur.Speed;
            double triggerDist = 38.0 + Math.Min(cursorSpeed * 0.06, 50.0);
            double triggerDistSq = triggerDist * triggerDist;

            for (int i = _landedLeaves.Count - 1; i >= 0; i--)
            {
                var leaf = _landedLeaves[i];

                int wIdx = windows.FindIndex(w => w.HWnd == leaf.HWnd);
                if (wIdx < 0)
                {
                    _landedLeaves.RemoveAt(i);
                    continue;
                }

                var win = windows[wIdx];
                _waterFilms.TryGetValue(win.HWnd, out var water);

                // 1. VÍZHULLÁM ÁRAMLÁSA: a víz lökete (SloshVx) csúsztatja a falevelet a lécen
                if (water != null && Math.Abs(water.SloshVx) > 6.0)
                {
                    leaf.RelX += water.SloshVx * 0.16 * dt;
                }

                double worldX = win.Rect.Left + leaf.RelX;
                double worldY = win.Rect.Top + leaf.RelY;

                // 2. VÍZHULLÁM LEMOSSA A FALEVELET A SZÉLEKEN
                if (leaf.RelX < 10.0)
                {
                    double pushVx = -35.0 - _rnd.NextDouble() * 30.0 + (water?.SloshVx ?? 0) * 0.25;
                    SpawnFallingLeafFromLanded(leaf, worldX, worldY, pushVx, 15.0 + _rnd.NextDouble() * 20.0);
                    _landedLeaves.RemoveAt(i);
                    continue;
                }
                else if (leaf.RelX > win.Rect.Width - 10.0)
                {
                    double pushVx = 35.0 + _rnd.NextDouble() * 30.0 + (water?.SloshVx ?? 0) * 0.25;
                    SpawnFallingLeafFromLanded(leaf, worldX, worldY, pushVx, 15.0 + _rnd.NextDouble() * 20.0);
                    _landedLeaves.RemoveAt(i);
                    continue;
                }

                // 3. ERŐTELJES VÍZHULLÁM ÁLTAL TELJES LEMOSÁS
                if (water != null && Math.Abs(water.SloshVx) > 190.0 && _rnd.NextDouble() < 0.40)
                {
                    double pushVx = water.SloshVx * 0.40 + (_rnd.NextDouble() - 0.5) * 30.0;
                    SpawnFallingLeafFromLanded(leaf, worldX, worldY, pushVx, 10.0 + _rnd.NextDouble() * 25.0);
                    _landedLeaves.RemoveAt(i);
                    continue;
                }

                // 4. INTERAKCIÓ: egérrel és vízhullámmal való lesöprés
                if (checkMouse)
                {
                    double dx = worldX - cur.X;
                    double dy = worldY - cur.Y;
                    double distSq = dx * dx + dy * dy;

                    if (distSq <= triggerDistSq)
                    {
                        double dirX = dx != 0 ? Math.Sign(dx) : (_rnd.NextDouble() > 0.5 ? 1 : -1);
                        double pushVx = cur.Vx * 0.45 + (water?.SloshVx ?? 0) * 0.3 + dirX * (45.0 + _rnd.NextDouble() * 65.0);
                        double pushVy = -20.0 - _rnd.NextDouble() * 45.0 + cur.Vy * 0.2;

                        SpawnFallingLeafFromLanded(leaf, worldX, worldY, pushVx, pushVy);
                        _landedLeaves.RemoveAt(i);
                        continue;
                    }
                }

                // 5. ÉLETTARTAM: 0 = soha nem tűnik el! > 0 esetén a beállított ideig marad, utána finoman elhalványul
                if (maxLife > 0)
                {
                    leaf.Age += dt;
                    if (leaf.Age >= maxLife)
                    {
                        _landedLeaves.RemoveAt(i);
                        continue;
                    }

                    if (leaf.Age >= maxLife - fadeDuration)
                    {
                        double remaining = maxLife - leaf.Age;
                        leaf.Opacity = Math.Clamp(remaining / fadeDuration, 0.0, 1.0);
                    }
                    else
                    {
                        leaf.Opacity = 1.0;
                    }
                }
                else
                {
                    leaf.Opacity = 1.0;
                }
            }
        }

        private void UpdateFallingLeaves(
            double dt,
            CursorState cur,
            List<TrackedWindow> windows,
            Func<double, double, IntPtr, bool> isCoveredByHigher,
            Func<double, double, bool> isInsideAnyWindow)
        {
            double windDrift = _s.LeafWindStrength * 40.0;
            double damp = Math.Exp(-2.2 * dt);

            bool mouse = _s.MouseRepelEnabled && cur.Valid;
            double mouseRadius = _s.MouseRepelRadius + Math.Min(cur.Speed * 0.05 * _s.MouseSpeedSensitivity, 100);
            double mouseRadiusSq = mouseRadius * mouseRadius;
            double radialAccel = 400.0 * _s.MouseWindPower;
            double dragAccel = 1.3 * _s.MouseSpeedSensitivity;

            int targetLeafCount = Math.Clamp(_s.LeafCount, 1, 40);

            for (int i = 0; i < _leaves.Count; i++)
            {
                var f = _leaves[i];

                if (mouse)
                {
                    double dx = f.X - cur.X;
                    double dy = f.Y - cur.Y;
                    double d2 = dx * dx + dy * dy;
                    if (d2 < mouseRadiusSq)
                    {
                        double d = Math.Sqrt(d2) + 0.001;
                        double fall = 1.0 - d / mouseRadius;
                        fall *= fall;
                        double weight = 1.1 - 0.4 * f.Depth;

                        double ax = (dx / d) * radialAccel * fall + cur.Vx * dragAccel * fall;
                        double ay = (dy / d) * radialAccel * fall * 0.7 + cur.Vy * dragAccel * fall;
                        f.Vx = Math.Clamp(f.Vx + ax * weight * dt, -700, 700);
                        f.Vy = Math.Clamp(f.Vy + ay * weight * dt, -700, 700);
                    }
                }

                f.PrevY = f.Y;

                // Szinuszos ringatózás és forgás
                f.SwayPhase += f.SwaySpeed * dt;
                double sway = Math.Sin(f.SwayPhase) * f.SwayAmp;
                double swayTilt = Math.Cos(f.SwayPhase) * 0.30;

                f.X += (sway + windDrift * (0.6 + f.Depth * 0.4) + f.Vx) * dt;

                double lift = Math.Abs(Math.Cos(f.SwayPhase)) * 9.0;
                f.Y += (f.FallVy - lift + f.Vy) * dt;

                f.Vx *= damp;
                f.Vy *= damp;
                f.Rot += (f.RotSpeed + swayTilt * 1.8) * dt;

                // ABLAK-ÜTKÖZÉS: PONTOSAN úgy marad ott, ahogy ráesik, és megül
                if (_s.AccumulateLeavesOnWindows && TryLandLeaf(f, windows, isCoveredByHigher))
                {
                    _leaves.RemoveAt(i);
                    i--;

                    if (_leaves.Count + _landedLeaves.Count < targetLeafCount)
                    {
                        _leaves.Add(NewLeaf(false));
                    }
                    continue;
                }

                if (f.Y > 0 && isInsideAnyWindow(f.X, f.Y - 16))
                {
                    _leaves[i] = NewLeaf(false);
                    continue;
                }

                double bottomLimit = _s.AccumulateOnTaskbar ? Height - 30 : Height + 30;
                if (f.Y > bottomLimit || f.X < -100 || f.X > Width + 100)
                {
                    _leaves[i] = NewLeaf(false);
                }
            }

            while (_leaves.Count + _landedLeaves.Count < targetLeafCount)
            {
                _leaves.Add(NewLeaf(false));
            }
        }

        private bool TryLandLeaf(
            LeafFlake f,
            List<TrackedWindow> windows,
            Func<double, double, IntPtr, bool> isCoveredByHigher)
        {
            if (f.Vx * f.Vx + f.Vy * f.Vy > 400.0) return false;

            double foot = f.Y + f.Size * 0.20;
            double prevFoot = f.PrevY + f.Size * 0.20;

            for (int b = 0; b < windows.Count; b++)
            {
                var w = windows[b];
                if (w.IsTaskbar && !_s.AccumulateOnTaskbar) continue;
                if (w.Rect.Top < LedgeMinTop) continue;
                if (w.Rect.Width < 120) continue;

                if (f.X < w.Rect.Left + 14 || f.X > w.Rect.Right - 14) continue;

                double relX = f.X - w.Rect.Left;
                double surfaceY = w.IsTaskbar
                    ? w.Rect.Top
                    : w.Rect.Top + SnowBank.GetYBase(relX, w.Rect.Width, true);

                bool hitLedge = (prevFoot <= surfaceY + 4 && foot >= surfaceY - 4) ||
                                (foot >= surfaceY - 2 && f.Y <= surfaceY + 22);

                if (hitLedge)
                {
                    if (isCoveredByHigher(f.X, surfaceY - 2, w.HWnd)) continue;

                    // PONTOSAN ABBAN A HELYZETBEN ÉS SZÖGBEN FEKSZIK RÁ, AHOGY RÁESETT
                    _landedLeaves.Add(new LandedLeaf
                    {
                        HWnd = w.HWnd,
                        RelX = relX,
                        RelY = (surfaceY - w.Rect.Top),
                        Size = f.Size,
                        Rot = f.Rot,
                        Variant = f.Variant,
                        ColorVariant = f.ColorVariant,
                        Age = 0,
                        MaxLife = _s.LeafLifetimeSeconds,
                        Opacity = 1.0
                    });

                    return true;
                }
            }
            return false;
        }

        private LeafFlake NewLeaf(bool initial)
        {
            double baseSize = Math.Clamp(_s.LeafSize, 12.0, 32.0);
            double size = baseSize * (0.85 + _rnd.NextDouble() * 0.35);

            double x = _rnd.NextDouble() * (Width + 140) - 70;
            double y = initial ? _rnd.NextDouble() * Height : -30 - _rnd.NextDouble() * 60;

            return new LeafFlake
            {
                X = x,
                Y = y,
                PrevY = y,
                Size = size,
                Opacity = 1.0,
                Depth = 0.5 + _rnd.NextDouble() * 0.5,
                Rot = _rnd.NextDouble() * Math.PI * 2.0,
                RotSpeed = (_rnd.NextDouble() - 0.5) * 2.0,
                SwayPhase = _rnd.NextDouble() * Math.PI * 2.0,
                SwaySpeed = 1.2 + _rnd.NextDouble() * 1.4,
                SwayAmp = 16.0 + _rnd.NextDouble() * 22.0,
                FallVy = (36.0 + _rnd.NextDouble() * 30.0) * _s.LeafFallSpeed,
                Vx = 0,
                Vy = 0,
                Variant = _rnd.Next(LeafSprites.Variants),
                ColorVariant = _rnd.Next(LeafSprites.Colors)
            };
        }

        // ------------------------------------------------------------------ RENDERELÉS

        public void Render(DrawingContext dc, List<TrackedWindow> windows, Func<double, double, bool> isInsideAnyWindow)
        {
            // 1. Ablakok tetején gyűlő vízréteg (vízfilm és pocsolya csillogás)
            RenderWaterFilms(dc, windows);

            // 2. Ablakok bal és jobb szélén lefolyó vízcseppek (Running Drops)
            RenderRunningDrops(dc, windows);

            // 3. Vízfelszíni csobbanások az ablakok tetején és az asztalon
            //    FONTOS: A falevelek ALATT helyezkednek el, így nem vágják át a megült faleveleket!
            if (_s.RainEnabled)
            {
                RenderSplashes(dc);
            }

            // 4. Megült falevelek az ablakok tetején (PushOpacity a vajpuha fade out-hoz + PushClip Z-kitakaráshoz)
            RenderLandedLeaves(dc, windows);

            // 5. Hulló falevelek
            double bottomLimit = _s.AccumulateOnTaskbar ? Height - 42 : Height + 25;
            for (int i = 0; i < _leaves.Count; i++)
            {
                var f = _leaves[i];
                if (f.Y >= bottomLimit || isInsideAnyWindow(f.X, f.Y - 16)) continue;

                var img = LeafSprites.GetLeaf(f.Variant, f.ColorVariant, f.Rot);
                double dia = f.Size * 1.5;
                dc.DrawImage(img, new Rect(f.X - dia * 0.5, f.Y - dia * 0.5, dia, dia));
            }

            // 6. Őszi esőcsíkok a levegőben
            if (_s.RainEnabled)
            {
                RenderRainDrops(dc, isInsideAnyWindow);
            }
        }

        private void RenderWaterFilms(DrawingContext dc, List<TrackedWindow> windows)
        {
            for (int wIdx = windows.Count - 1; wIdx >= 0; wIdx--)
            {
                var win = windows[wIdx];
                if (win.IsTaskbar && !_s.AccumulateOnTaskbar) continue;
                if (!_waterFilms.TryGetValue(win.HWnd, out var water)) continue;
                if (water.Level < 0.2) continue;

                double waterHeight = Math.Min(3.2, water.Level);
                double left = win.Rect.Left;
                double width = win.Rect.Width;
                if (width < 30) continue;

                // Z-kitakarás magasabb ablakok által
                Geometry? clipGeom = null;
                Rect waterArea = new Rect(left - 2, win.Rect.Top - 4, width + 4, waterHeight + 8);
                for (int h = 0; h < wIdx; h++)
                {
                    var higher = windows[h];
                    if (higher.Rect.IntersectsWith(waterArea))
                    {
                        clipGeom ??= new RectangleGeometry(waterArea);
                        clipGeom = Geometry.Combine(clipGeom, new RectangleGeometry(higher.Rect), GeometryCombineMode.Exclude, null);
                    }
                }
                if (clipGeom != null)
                {
                    if (clipGeom.IsEmpty()) continue;
                    dc.PushClip(clipGeom);
                }

                // Vízvonal / tükröződés az ablak felső ívén
                var streamGeom = new StreamGeometry();
                using (var ctx = streamGeom.Open())
                {
                    double step = Math.Max(4.0, width / 64.0);
                    int count = (int)Math.Ceiling(width / step) + 1;

                    double startX = left;
                    double startY = win.Rect.Top + SnowBank.GetYBase(0, width, !win.IsTaskbar);
                    ctx.BeginFigure(new Point(startX, startY), false, false);

                    for (int p = 1; p <= count; p++)
                    {
                        double relX = Math.Min(width, p * step);
                        double yBase = win.Rect.Top + SnowBank.GetYBase(relX, width, !win.IsTaskbar);
                        double wave = Math.Sin((relX + water.WaveOffset) * 0.05) * 0.35 * (water.Level / 3.0);
                        ctx.LineTo(new Point(left + relX, yBase + wave), true, true);
                    }
                }
                streamGeom.Freeze();

                // Vízréteg test
                var waterPen = new Pen(_waterFilmBrush, waterHeight)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                waterPen.Freeze();
                dc.DrawGeometry(null, waterPen, streamGeom);

                // Meniszkusz fényvonal
                dc.DrawGeometry(null, _waterHighlightPen, streamGeom);

                if (clipGeom != null)
                {
                    dc.Pop();
                }
            }
        }

        private void RenderRunningDrops(DrawingContext dc, List<TrackedWindow> windows)
        {
            for (int i = 0; i < _runningDrops.Count; i++)
            {
                var drop = _runningDrops[i];
                int wIdx = windows.FindIndex(w => w.HWnd == drop.HWnd);
                if (wIdx < 0) continue;

                var win = windows[wIdx];
                double edgeX = drop.IsLeftEdge ? (win.Rect.Left + drop.OffsetX) : (win.Rect.Right + drop.OffsetX);
                double topY = win.Rect.Top + Math.Max(0.0, drop.RelY - drop.Length);
                double botY = win.Rect.Top + drop.RelY;

                // Uszály vonala
                dc.DrawLine(_runningDropPen, new Point(edgeX, topY), new Point(edgeX, botY));

                // Csepp feje
                dc.DrawEllipse(_runningDropBrush, null, new Point(edgeX, botY), drop.Thickness * 0.75, drop.Thickness);
            }
        }

        private void RenderSplashes(DrawingContext dc)
        {
            for (int i = 0; i < _splashes.Count; i++)
            {
                var sp = _splashes[i];
                double progress = sp.Age / sp.MaxLife;
                if (progress >= 1.0) continue;

                double rx = sp.Radius * (0.3 + 0.7 * progress);
                double ry = rx * 0.35;
                double alpha = (1.0 - progress) * 0.8;

                var splashPen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(alpha * 200), 200, 230, 255)), 0.85);
                dc.DrawEllipse(null, splashPen, new Point(sp.X, sp.Y), rx, ry);

                if (progress < 0.6)
                {
                    double dropScale = 1.0 - progress / 0.6;
                    dc.DrawEllipse(_splashDropBrush, null, new Point(sp.X - rx * 0.4, sp.Y - 2.5 * dropScale), 0.75, 0.75);
                    dc.DrawEllipse(_splashDropBrush, null, new Point(sp.X + rx * 0.4, sp.Y - 2.0 * dropScale), 0.75, 0.75);
                }
            }
        }

        private void RenderRainDrops(DrawingContext dc, Func<double, double, bool> isInsideAnyWindow)
        {
            double wind = _s.LeafWindStrength * 80.0;
            double bottomLimit = _s.AccumulateOnTaskbar ? Height - 42 : Height + 25;

            for (int i = 0; i < _rainDrops.Count; i++)
            {
                var drop = _rainDrops[i];
                if (drop.Y >= bottomLimit || isInsideAnyWindow(drop.X, drop.Y)) continue;

                double dx = wind * (drop.Length / drop.Vy);
                var p1 = new Point(drop.X, drop.Y);
                var p2 = new Point(drop.X + dx, drop.Y + drop.Length);
                dc.DrawLine(_rainPen, p1, p2);
            }
        }

        private void RenderLandedLeaves(DrawingContext dc, List<TrackedWindow> windows)
        {
            if (_landedLeaves.Count == 0) return;

            for (int wIdx = windows.Count - 1; wIdx >= 0; wIdx--)
            {
                var win = windows[wIdx];
                if (win.IsTaskbar && !_s.AccumulateOnTaskbar) continue;

                List<LandedLeaf>? windowLeaves = null;
                for (int i = 0; i < _landedLeaves.Count; i++)
                {
                    if (_landedLeaves[i].HWnd == win.HWnd)
                    {
                        (windowLeaves ??= new List<LandedLeaf>()).Add(_landedLeaves[i]);
                    }
                }
                if (windowLeaves == null) continue;

                Geometry? clipGeom = null;
                Rect windowLedgeArea = new Rect(win.Rect.Left - 20, win.Rect.Top - 35, win.Rect.Width + 40, 65);

                for (int h = 0; h < wIdx; h++)
                {
                    var higher = windows[h];
                    if (higher.Rect.IntersectsWith(windowLedgeArea))
                    {
                        clipGeom ??= new RectangleGeometry(windowLedgeArea);
                        clipGeom = Geometry.Combine(clipGeom, new RectangleGeometry(higher.Rect), GeometryCombineMode.Exclude, null);
                    }
                }

                if (clipGeom != null)
                {
                    if (clipGeom.IsEmpty()) continue;
                    dc.PushClip(clipGeom);
                }

                for (int i = 0; i < windowLeaves.Count; i++)
                {
                    var leaf = windowLeaves[i];
                    double worldX = win.Rect.Left + leaf.RelX;
                    double worldY = win.Rect.Top + leaf.RelY;

                    var img = LeafSprites.GetLeaf(leaf.Variant, leaf.ColorVariant, leaf.Rot);
                    double dia = leaf.Size * 1.5;

                    bool needOpacity = leaf.Opacity < 0.99;
                    if (needOpacity)
                    {
                        dc.PushOpacity(leaf.Opacity);
                    }

                    dc.DrawImage(img, new Rect(worldX - dia * 0.5, worldY - dia * 0.5, dia, dia));

                    if (needOpacity)
                    {
                        dc.Pop();
                    }
                }

                if (clipGeom != null)
                {
                    dc.Pop();
                }
            }
        }
    }
}