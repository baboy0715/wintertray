using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using WinterTray.Native;

namespace WinterTray.Engine
{
    public sealed class WaterDroplet
    {
        public IntPtr SourceHwnd;
        public double X;
        public double Y;
        public double Vy;
        public bool FromTop;
        public double TargetY;
        public double Life;
        public bool Splashed;
        public double SplashAge;
    }

    public sealed class SnowWorld
    {
        private const double LedgeMinTop = 28.0;   // ennél feljebb lévő (pl. teljes méretű) ablaknak nincs szabad teteje
        private const int MaxParticles = 600;

        private readonly Settings _s;
        private readonly Random _rnd = new();
        private readonly List<SnowFlake> _flakes = new();
        private readonly Particle[] _particles = new Particle[MaxParticles];
        private int _particleCount;

        private readonly Dictionary<IntPtr, SnowBank> _banks = new();
        private readonly List<SnowBank> _bankList = new();
        private List<TrackedWindow> _windows = new();
        private List<ScreenInfo> _screens = new();
        private readonly List<(double MinX, double MaxX)> _openSkyIntervals = new();

        private readonly Brush _pileFill;
        private readonly Pen _pileEdge;
        private readonly Pen _pileHalo;

        private readonly Brush _icicleFill;
        private readonly Pen _icicleHighlight;
        private readonly Pen _icicleHalo;

        private readonly Brush _dropletBrush;
        private readonly Brush _dropletGlint;
        private readonly List<WaterDroplet> _droplets = new(32);
        private double _dripAcc;

        private readonly LeafSimulation _leafSim;
        private AppSeason _lastSeason;

        private double _time;
        private double _sweepSpawnAcc;
        private (double min, double max, int style, double speed) _lastShape;

        public double Width { get; private set; } = 1920;
        public double Height { get; private set; } = 1080;

        public SnowWorld(Settings settings)
        {
            _s = settings;
            _leafSim = new LeafSimulation(settings);
            _lastSeason = settings.Season;
            SnowSprites.Init();

            var fill = new LinearGradientBrush(
                Color.FromRgb(255, 255, 255), Color.FromRgb(214, 229, 245),
                new Point(0, 0), new Point(0, 1));
            fill.Freeze();
            _pileFill = fill;

            var edge = new Pen(new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)), 1.6)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            edge.Freeze();
            _pileEdge = edge;

            var halo = new Pen(new SolidColorBrush(Color.FromArgb(50, 235, 246, 255)), 3.5)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            halo.Freeze();
            _pileHalo = halo;

            var icicleFill = new LinearGradientBrush(
                Color.FromArgb(235, 245, 252, 255), Color.FromArgb(190, 180, 225, 250),
                new Point(0, 0), new Point(0, 1));
            icicleFill.GradientStops.Add(new GradientStop(Color.FromArgb(245, 250, 255, 255), 1.0));
            icicleFill.Freeze();
            _icicleFill = icicleFill;

            var icicleHighlight = new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), 0.85);
            icicleHighlight.Freeze();
            _icicleHighlight = icicleHighlight;

            var icicleHalo = new Pen(new SolidColorBrush(Color.FromArgb(40, 160, 230, 255)), 2.2);
            icicleHalo.Freeze();
            _icicleHalo = icicleHalo;

            var dropBrush = new SolidColorBrush(Color.FromArgb(200, 195, 230, 255));
            dropBrush.Freeze();
            _dropletBrush = dropBrush;

            var dropGlint = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255));
            dropGlint.Freeze();
            _dropletGlint = dropGlint;
        }

        private int MaxBudget()
        {
            return _s.AnimationQuality switch
            {
                2 => MaxParticles,
                1 => 250,
                _ => 0
            };
        }

        // ------------------------------------------------------------------ beállítás / méret

        public void Resize(double width, double height)
        {
            Width = width;
            Height = height;
            ResetFlakes();
            _leafSim.Resize(width, height);
        }

        public void Reconfigure()
        {
            if (_s.Season != _lastSeason)
            {
                _lastSeason = _s.Season;
                if (_s.Season == AppSeason.Autumn)
                {
                    _leafSim.ResetLeaves();
                }
                else
                {
                    ResetFlakes();
                }
            }

            _leafSim.Reconfigure();

            var shape = (_s.FlakeSizeMin, _s.FlakeSizeMax, _s.FlakeStyle, _s.FallSpeed);
            if (shape != _lastShape)
            {
                ResetFlakes();
                return;
            }

            while (_flakes.Count < _s.FlakeCount) _flakes.Add(NewFlake(true));
            if (_flakes.Count > _s.FlakeCount) _flakes.RemoveRange(_s.FlakeCount, _flakes.Count - _s.FlakeCount);
        }

        private void ResetFlakes()
        {
            _lastShape = (_s.FlakeSizeMin, _s.FlakeSizeMax, _s.FlakeStyle, _s.FallSpeed);
            _flakes.Clear();
            for (int i = 0; i < _s.FlakeCount; i++) _flakes.Add(NewFlake(true));
        }

        /// <summary>"Letakarítás": minden halom lezúdul télen, levelek lerepülnek ősszel.</summary>
        public void ClearAll()
        {
            if (_s.Season == AppSeason.Autumn)
            {
                _leafSim.ClearAll();
            }
            else
            {
                foreach (var bank in _bankList) Avalanche(bank, 0, 0);
            }
        }

        // ------------------------------------------------------------------ ablakok

        public void SetScreensAndWindows(List<ScreenInfo> screens, List<TrackedWindow> windows)
        {
            _screens = screens;
            SetWindows(windows);
        }

        public void SetWindows(List<TrackedWindow> windows)
        {
            _windows = windows;

            var keep = new HashSet<IntPtr>();
            if (_s.AccumulateOnWindows)
            {
                foreach (var w in windows)
                {
                    if (w.IsTaskbar && !_s.AccumulateOnTaskbar) continue;

                    double sTop = 0;
                    double sBottom = Height;
                    for (int s = 0; s < _screens.Count; s++)
                    {
                        var sc = _screens[s];
                        if (sc.Bounds.IntersectsWith(w.Rect))
                        {
                            sTop = sc.WorkingArea.Top;
                            sBottom = sc.WorkingArea.Bottom;
                            break;
                        }
                    }

                    if (w.Rect.Top < sTop + LedgeMinTop || w.Rect.Top > sBottom - 40) continue;
                    if (w.Rect.Width < 120) continue;

                    keep.Add(w.HWnd);
                    if (!_banks.TryGetValue(w.HWnd, out var bank))
                    {
                        bank = new SnowBank
                        {
                            HWnd = w.HWnd,
                            IsTaskbar = w.IsTaskbar,
                            LastPxLeft = w.PxRect.Left,
                            LastPxTop = w.PxRect.Top,
                            LastPxWidth = w.PxRect.Width,
                            LastPxHeight = w.PxRect.Height
                        };
                        _banks[w.HWnd] = bank;
                        _bankList.Add(bank);
                    }
                    bank.UpdateBounds(w.Rect.Left, w.Rect.Top, w.Rect.Width, w.Rect.Height, (x, y, amt, dir) => HandleResizeShed(x, y, amt, dir, bank.HWnd));
                }
            }

            if (_banks.Count != keep.Count)
            {
                List<IntPtr>? remove = null;
                foreach (var key in _banks.Keys)
                {
                    if (!keep.Contains(key)) (remove ??= new List<IntPtr>()).Add(key);
                }
                if (remove != null)
                {
                    foreach (var key in remove)
                    {
                        var bank = _banks[key];
                        Avalanche(bank, 0, 0);
                        PopulateExposedArea(new Rect(bank.Left, bank.Top, bank.Width, Math.Min(400, Height - bank.Top)));
                        _banks.Remove(key);
                    }
                }
            }

            // A _bankList sorrendje mindig pontosan kövesse a Windows valódi Z-sorrendjét (elölről hátrafelé)
            _bankList.Clear();
            foreach (var w in _windows)
            {
                if (_banks.TryGetValue(w.HWnd, out var b))
                {
                    _bankList.Add(b);
                }
            }

            UpdateWindowsClearance();
            UpdateOpenSkyIntervals();
        }

        private void UpdateOpenSkyIntervals()
        {
            _openSkyIntervals.Clear();

            if (_screens.Count == 0)
            {
                _openSkyIntervals.Add((0, Width));
                return;
            }

            for (int s = 0; s < _screens.Count; s++)
            {
                var screen = _screens[s];
                if (screen.IsCovered) continue; // Teljesen lefedett monitor: semmilyen hópehely nem eshet!

                double sLeft = screen.WorkingArea.Left;
                double sRight = screen.WorkingArea.Right;
                double sTop = screen.WorkingArea.Top;

                var intervals = new List<(double min, double max)> { (sLeft, sRight) };

                for (int w = 0; w < _windows.Count; w++)
                {
                    var win = _windows[w];
                    if (win.IsTaskbar) continue;
                    // Ha az ablak eléri a monitor felső peremét (pl. Win+Balra/Jobbra vagy maximalizált),
                    // akkor a fölötte lévő oszlopból nem indulhat hóesés!
                    if (win.Rect.Top < sTop + LedgeMinTop && win.Rect.Right > sLeft && win.Rect.Left < sRight)
                    {
                        double bMin = win.Rect.Left - 4;
                        double bMax = win.Rect.Right + 4;

                        var next = new List<(double min, double max)>();
                        for (int i = 0; i < intervals.Count; i++)
                        {
                            var seg = intervals[i];
                            if (bMax <= seg.min || bMin >= seg.max)
                            {
                                next.Add(seg);
                            }
                            else
                            {
                                if (seg.min < bMin) next.Add((seg.min, bMin));
                                if (seg.max > bMax) next.Add((bMax, seg.max));
                            }
                        }
                        intervals = next;
                        if (intervals.Count == 0) break;
                    }
                }

                for (int i = 0; i < intervals.Count; i++)
                {
                    if (intervals[i].max - intervals[i].min >= 8.0)
                    {
                        _openSkyIntervals.Add(intervals[i]);
                    }
                }
            }
        }

        /// <summary>
        /// Minden egyes render-képkockán (120 FPS-nél 8.3 ezredmásodpercenként) lefutó azonnali szinkronizáció.
        /// Nulla felesleges EnumWindows hívás nélkül, közvetlenül a követett ablakok HWND-jét kérdezi le,
        /// így a hó hajszálpontosan és késleltetés nélkül mozog az ablakkal 0 ms fáziskéséssel!
        /// </summary>
        public void SyncWindowPositions(Func<Point, Point>? toDip, double dt)
        {
            if (toDip == null || _bankList.Count == 0) return;

            for (int i = 0; i < _bankList.Count; i++)
            {
                var bank = _bankList[i];
                if (bank.HWnd == IntPtr.Zero) continue;

                // Ha az ablak bezárult vagy minimalizálódott
                if (!Win32.IsWindowVisible(bank.HWnd) || Win32.IsIconic(bank.HWnd) || Win32.IsWindowCloaked(bank.HWnd))
                {
                    var oldRect = new Rect(bank.Left, bank.Top, bank.Width, 200);
                    OnWindowChanged(bank.HWnd, oldRect, null, dt);
                    i--;
                    continue;
                }

                if (!Win32.GetAccurateWindowRect(bank.HWnd, out var px)) continue;

                if (px.Left != bank.LastPxLeft || px.Top != bank.LastPxTop || px.Width != bank.LastPxWidth || px.Height != bank.LastPxHeight)
                {
                    Point tl = toDip(new Point(px.Left, px.Top));
                    Point br = toDip(new Point(px.Right, px.Bottom));
                    var newRect = new Rect(tl, br);
                    var oldRect = new Rect(bank.Left, bank.Top, bank.Width, bank.Height);

                    bank.LastPxLeft = px.Left;
                    bank.LastPxTop = px.Top;
                    bank.LastPxWidth = px.Width;
                    bank.LastPxHeight = px.Height;

                    // Frissítjük a TrackedWindow téglalapját is valós időben (120 FPS szinkron Z-kitakaráshoz)
                    for (int wIdx = 0; wIdx < _windows.Count; wIdx++)
                    {
                        if (_windows[wIdx].HWnd == bank.HWnd)
                        {
                            _windows[wIdx] = new TrackedWindow
                            {
                                HWnd = bank.HWnd,
                                Title = _windows[wIdx].Title,
                                ClassName = _windows[wIdx].ClassName,
                                Rect = newRect,
                                PxRect = px,
                                IsTaskbar = _windows[wIdx].IsTaskbar
                            };
                            break;
                        }
                    }

                    OnWindowChanged(bank.HWnd, oldRect, newRect, dt);
                }
            }

            UpdateWindowsClearance();
            UpdateOpenSkyIntervals();
        }

        /// <summary>
        /// Ablak bezárásakor / letálcázásakor azonnal feltöltjük a frissen felfedett asztalterületet
        /// eső hópelyhekkel, mintha mindvégig esett volna a háttérben.
        /// </summary>
        public void PopulateExposedArea(Rect area)
        {
            if (area.Width < 50 || area.Height < 50) return;

            // Csak a parkolópályán lévő (-9999) pelyheket helyezzük át az újonnan megnyílt területre egyenletesen
            for (int i = 0; i < _flakes.Count; i++)
            {
                var f = _flakes[i];
                if (f.Y <= -9000)
                {
                    double candX = area.Left + _rnd.NextDouble() * area.Width;
                    double candY = area.Top + _rnd.NextDouble() * area.Height;
                    if (IsOccluded(candX, candY, f.Size * 1.3)) continue;
                    f.X = candX;
                    f.Y = candY;
                    f.PrevY = f.Y;
                    f.Dvx = 0;
                    f.Dvy = 0;
                }
            }
        }

        /// <summary>
        /// Ablak mozgatása, bezárása vagy letálcázása.
        /// Fizikai tehetetlenség: lassú mozgatásnál a hó az ablak tetején marad és vele utazik;
        /// gyors rántásnál vagy hirtelen irányváltásnál a tehetetlenség arányában csúszik és hullik le!
        /// Bezáráskor/letálcázáskor pedig a teljes hó lezúdul.
        /// </summary>
        public void OnWindowChanged(IntPtr hwnd, Rect oldRect, Rect? newRect, double dt)
        {
            if (!newRect.HasValue)
            {
                _leafSim.OnWindowClosed(hwnd, oldRect);
            }

            if (!_banks.TryGetValue(hwnd, out var bank)) return;

            // 1. ESET: Az ablak bezárult vagy letálcázódott
            if (!newRect.HasValue)
            {
                Avalanche(bank, bank.Vx, bank.Vy);
                PopulateExposedArea(oldRect);
                _banks.Remove(hwnd);
                _bankList.Remove(bank);
                return;
            }

            // 2. ESET: Az ablak még nyitva van és mozog a képernyőn
            var r = newRect.Value;
            double sTop = 0;
            double sBottom = Height;
            for (int s = 0; s < _screens.Count; s++)
            {
                var sc = _screens[s];
                if (sc.Bounds.IntersectsWith(r))
                {
                    sTop = sc.WorkingArea.Top;
                    sBottom = sc.WorkingArea.Bottom;
                    break;
                }
            }

            if (r.Top < sTop + LedgeMinTop || r.Top > sBottom - 40 || r.Width < 120)
            {
                Avalanche(bank, bank.Vx, bank.Vy);
                _banks.Remove(hwnd);
                _bankList.Remove(bank);
                return;
            }

            // A pozíció és méret azonnal frissül:
            // Ha az ablakot kicsinyítik (szélét behúzzák), a levágott peremről a hó szépen leomlik,
            // a megmaradt felületen lévő hó pedig képernyő-helyén marad, nem torzul.
            bank.UpdateBounds(r.Left, r.Top, r.Width, r.Height, (x, y, amt, dir) => HandleResizeShed(x, y, amt, dir, bank.HWnd));

            double safeDt = Math.Clamp(dt, 0.005, 0.1);
            double vx = (r.X - oldRect.X) / safeDt;
            double vy = (r.Y - oldRect.Y) / safeDt;

            // Sebesség és gyorsulás számítása
            bank.Vx = bank.Vx * 0.35 + vx * 0.65;
            bank.Vy = bank.Vy * 0.35 + vy * 0.65;
            double speed = Math.Sqrt(vx * vx + vy * vy);
            _leafSim.OnWindowMoved(hwnd, oldRect, r, vx, vy);

            double ax = (vx - bank.PrevVx) / safeDt;
            double ay = (vy - bank.PrevVy) / safeDt;
            bank.PrevVx = vx;
            bank.PrevVy = vy;
            double accel = Math.Sqrt(ax * ax + ay * ay);

            // TEHETETLENSÉG FIZIKÁJA:
            // A hó az ablak tetején maradva 1:1-ben együtt mozog az ablakkal!
            // Ha a felhasználó hirtelen megrántja az ablakot vagy gyorsan mozgatja,
            // a tehetetlenség hatására hódarabok szakadnak le és zuhannak lefelé.
            if (_s.FallOffOnWindowMove && bank.Peak >= 0.4f)
            {
                int budget = MaxBudget();

                // Hirtelen rántás (gyorsulás) vagy nagy sebesség miatti lepergés
                double shedRatio = 0;
                if (speed > 280.0)
                {
                    shedRatio += ((speed - 280.0) / 1000.0) * safeDt * 3.0;
                }
                if (accel > 2400.0)
                {
                    shedRatio += ((accel - 2400.0) / 7000.0) * 0.22;
                }

                shedRatio = Math.Clamp(shedRatio, 0.0, 0.35);
                if (shedRatio > 0.010 && budget > 0)
                {
                    bank.Shed(shedRatio, (px, py, amount) =>
                    {
                        if (_particleCount < budget)
                        {
                            // A lehulló hódarabok megőrzik az ablak lendületét és hókupacokként hullanak le
                            SpawnInertialClump(px, py, vx * 0.40 + Gauss() * 25, vy * 0.30 + 20 + _rnd.NextDouble() * 30, amount, bank.HWnd);
                        }
                    });
                }
            }
        }

        private void HandleResizeShed(double x, double y, float amount, double edgeDir, IntPtr hwnd)
        {
            int budget = MaxBudget();
            if (budget <= 0 || _particleCount >= budget) return;

            // 1. Leomló hódarabok (Clump) - darabos lavina a visszahúzott peremről
            int clumpCount = Math.Clamp((int)Math.Ceiling(amount / 3.0), 1, 3);
            for (int k = 0; k < clumpCount && _particleCount < budget; k++)
            {
                double dir = edgeDir != 0.0 ? edgeDir : (Gauss() > 0 ? 1.0 : -1.0);
                double vx = dir * (16.0 + _rnd.NextDouble() * 30.0) + Gauss() * 16.0;
                double vy = 12.0 + _rnd.NextDouble() * 32.0;
                double size = 2.4 + Math.Min(amount, 6.0) * 0.7 + _rnd.NextDouble() * 1.8;

                SpawnParticle(new Particle
                {
                    SourceHwnd = hwnd,
                    Kind = ParticleKind.Clump,
                    X = x + (_rnd.NextDouble() - 0.5) * 6,
                    Y = y + (_rnd.NextDouble() - 0.5) * 4,
                    Size = size,
                    Vx = vx,
                    Vy = vy,
                    Rot = _rnd.NextDouble() * 6.28,
                    RotSpeed = (_rnd.NextDouble() - 0.5) * 6,
                    Life = 0.70 + _rnd.NextDouble() * 0.60,
                    Variant = _rnd.Next(SnowSprites.ClumpVariants)
                });
            }

            // 2. Finom hófúvás / porfelhő (Dust) a levágott szél mentén
            if (_particleCount < budget)
            {
                double dir = edgeDir != 0.0 ? edgeDir : 0.0;
                SpawnParticle(new Particle
                {
                    SourceHwnd = hwnd,
                    Kind = ParticleKind.Dust,
                    X = x + (_rnd.NextDouble() - 0.5) * 4,
                    Y = y - 1 - _rnd.NextDouble() * 3,
                    Vx = dir * 14.0 + Gauss() * 22.0,
                    Vy = -8.0 - _rnd.NextDouble() * 22.0,
                    Size = 1.4 + _rnd.NextDouble() * 1.4,
                    Life = 0.35 + _rnd.NextDouble() * 0.30
                });
            }
        }

        private void SpawnInertialClump(double x, double y, double vx, double vy, float amount, IntPtr hwnd)
        {
            if (_particleCount >= MaxBudget()) return;
            double size = 2.4 + Math.Min(amount, 5.0) * 0.6 + _rnd.NextDouble() * 1.6;
            SpawnParticle(new Particle
            {
                SourceHwnd = hwnd,
                Kind = ParticleKind.Clump,
                X = x + (_rnd.NextDouble() - 0.5) * 6,
                Y = y + (_rnd.NextDouble() - 0.5) * 4,
                Size = size,
                Vx = vx,
                Vy = vy,
                Rot = _rnd.NextDouble() * 6.28,
                RotSpeed = (_rnd.NextDouble() - 0.5) * 6,
                Life = 0.65 + _rnd.NextDouble() * 0.65,
                Variant = _rnd.Next(SnowSprites.ClumpVariants)
            });
        }

        public bool IsCoveredByTaskbar(double x, double y, double margin = 0.0)
        {
            for (int i = 0; i < _windows.Count; i++)
            {
                var w = _windows[i];
                if (w.IsTaskbar)
                {
                    if (x >= w.Rect.Left - margin && x <= w.Rect.Right + margin &&
                        y >= w.Rect.Top - margin && y <= w.Rect.Bottom + margin)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public bool IsOccluded(double x, double y, double radius)
        {
            if (x < -120 || x > Width + 120 || y > Height + 50) return true;

            // 1. Ha a pont olyan monitoron van, amely teljesen le van fedve
            for (int s = 0; s < _screens.Count; s++)
            {
                var sc = _screens[s];
                if (sc.IsCovered && sc.Bounds.Contains(x, y))
                {
                    return true;
                }
            }

            // 2. Tálca általi közvetlen kitakarás:
            // A hó nem egy virtuális vonalnál tűnik el a levegőben, hanem KONKRÉTAN maga a tálca takarja el!
            // Így oldalra helyezett tálcánál (balra vagy jobbra) és többképernyős rendszereknél is tökéletes.
            for (int i = 0; i < _windows.Count; i++)
            {
                var w = _windows[i];
                if (w.IsTaskbar)
                {
                    if (x + radius >= w.Rect.Left && x - radius <= w.Rect.Right &&
                        y + radius >= w.Rect.Top && y - radius <= w.Rect.Bottom)
                    {
                        return true;
                    }
                }
            }

            // 3. Ablakok általi kitakarás vizsgálata
            for (int i = 0; i < _windows.Count; i++)
            {
                var w = _windows[i];
                if (w.IsTaskbar) continue;

                double wLeft = w.Rect.Left - 2.0;
                double wRight = w.Rect.Right + 2.0;
                double wTop = w.Rect.Top + 6.0;
                double wBottom = w.Rect.Bottom + 2.0;

                // Monitor munkaterület felső és alsó határa az ablak környezetében
                double sTop = 0;
                double sBottom = Height;
                for (int s = 0; s < _screens.Count; s++)
                {
                    var sc = _screens[s];
                    if (sc.Bounds.IntersectsWith(w.Rect))
                    {
                        sTop = sc.WorkingArea.Top;
                        sBottom = sc.WorkingArea.Bottom;
                        break;
                    }
                }

                // Ha az ablak eléri a képernyő tetejét (pl. 2 ablak egymás mellett, maximalizált ablak, stb.)
                // akkor fölötte / a fejlécénél NEM eshet hó!
                if (w.Rect.Top < sTop + LedgeMinTop)
                {
                    wTop = -9999.0;
                }

                // Ha az ablak az alsó peremet / tálcát is eléri
                if (w.Rect.Bottom >= sBottom - 45)
                {
                    wBottom = 9999.0;
                }

                // Ha a hópehely köre metszi az ablak kitakarási téglalapját
                if (x + radius >= wLeft && x - radius <= wRight &&
                    y + radius >= wTop && y - radius <= wBottom)
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsInsideAnyWindow(double x, double y)
        {
            return IsOccluded(x, y, 0);
        }

        public bool IsCoveredByHigher(double x, double y, IntPtr hwnd)
        {
            for (int i = 0; i < _windows.Count; i++)
            {
                var w = _windows[i];
                if (w.HWnd == hwnd) break; // Z-sorrendben a saját ablakunkig nézzük a feljebb lévőket
                if (w.Rect.Contains(x, y)) return true;
            }
            return false;
        }

        private bool IsCoveredByOther(double x, double y, IntPtr except)
        {
            return IsCoveredByHigher(x, y, except);
        }

        internal SnowBank? GetBankForTesting(IntPtr hwnd)
        {
            _banks.TryGetValue(hwnd, out var b);
            return b;
        }

        internal int GetActiveFlakeCountForTesting()
        {
            double totalW = GetTotalScreenWorkingWidth();
            double openW = GetOpenSkyWidth();
            double ratio = totalW > 0 ? (openW / totalW) : 1.0;
            return (int)Math.Round(_s.FlakeCount * Math.Clamp(ratio, 0.0, 1.0));
        }

        public void UpdateWindowsClearance()
        {
            if (_bankList.Count == 0) return;

            for (int i = 0; i < _bankList.Count; i++)
            {
                var bank = _bankList[i];
                if (bank.IsTaskbar)
                {
                    bank.BottomClearance = 0;
                    continue;
                }

                double clearance = Math.Max(0.0, Height - bank.Bottom);

                for (int w = 0; w < _windows.Count; w++)
                {
                    var win = _windows[w];
                    if (win.IsTaskbar)
                    {
                        // Ha a tálca horizontálisan átfedi az ablakot
                        if (bank.Left < win.Rect.Right && (bank.Left + bank.Width) > win.Rect.Left)
                        {
                            double dist = win.Rect.Top - bank.Bottom;
                            if (dist < clearance)
                            {
                                clearance = Math.Max(0.0, dist);
                            }
                        }
                    }
                }

                bank.BottomClearance = clearance;
            }
        }

        // ------------------------------------------------------------------ frissítés

        public void Update(double dt, CursorState cursor)
        {
            _time += dt;

            UpdateWindowsClearance();

            if (_s.Season == AppSeason.Autumn)
            {
                _leafSim.Update(dt, cursor, _windows, IsCoveredByHigher, IsInsideAnyWindow);
                return;
            }

            double maxH = _s.LimitSnowHeight ? _s.MaxSnowHeight : 9999.0;
            double melt = _s.MeltRateSeconds > 0 ? maxH / _s.MeltRateSeconds : 0;
            for (int i = 0; i < _bankList.Count; i++) _bankList[i].Tick(dt, maxH, melt);

            UpdateFlakes(dt, cursor);
            if (_s.MouseSweepSnow) Sweep(cursor, dt);
            UpdateParticles(dt);
            UpdateDroplets(dt);
        }

        private void UpdateDroplets(double dt)
        {
            if (!_s.ShowIcicles || _bankList.Count == 0)
            {
                _droplets.Clear();
                return;
            }

            _dripAcc += dt;
            if (_dripAcc > 1.2 && _droplets.Count < 24)
            {
                _dripAcc = 0;
                int bIdx = _rnd.Next(_bankList.Count);
                var bank = _bankList[bIdx];
                if (bank.TryGetDropletSource(_rnd, out double dx, out double dy, out bool fromTop, out double ty))
                {
                    // Ha a jégcsap-forrás el van takarva egy előtéri ablak vagy tálca által, nem indítunk cseppet!
                    if (!IsCoveredByHigher(dx, dy, bank.HWnd) && !IsCoveredByTaskbar(dx, dy))
                    {
                        _droplets.Add(new WaterDroplet
                        {
                            SourceHwnd = bank.HWnd,
                            X = dx,
                            Y = dy,
                            Vy = 35.0 + _rnd.NextDouble() * 30.0,
                            FromTop = fromTop,
                            TargetY = ty,
                            Life = 4.0
                        });
                    }
                }
            }

            for (int i = 0; i < _droplets.Count; i++)
            {
                var d = _droplets[i];
                d.Life -= dt;
                if (d.Life <= 0)
                {
                    _droplets.RemoveAt(i--);
                    continue;
                }

                if (d.Splashed)
                {
                    d.SplashAge += dt;
                    if (d.SplashAge > 0.15)
                    {
                        _droplets.RemoveAt(i--);
                        continue;
                    }
                }
                else
                {
                    d.Vy += 620.0 * dt;
                    d.Y += d.Vy * dt;

                    // Ha a vízcsepp menet közben egy másik (előtéri) ablak mögé vagy a tálca mögé esik:
                    if (IsCoveredByHigher(d.X, d.Y, d.SourceHwnd) || IsCoveredByTaskbar(d.X, d.Y))
                    {
                        _droplets.RemoveAt(i--);
                        continue;
                    }

                    // Felső jégcsapról eső csepp: az ablak aljára érve szétfröccsen / lecseppen
                    if (d.FromTop && d.Y >= d.TargetY)
                    {
                        d.Y = d.TargetY;
                        d.Splashed = true;
                        d.SplashAge = 0;

                        int budget = MaxBudget();
                        if (budget > 0 && _particleCount < budget)
                        {
                            for (int k = 0; k < 3; k++)
                            {
                                SpawnParticle(new Particle
                                {
                                    SourceHwnd = d.SourceHwnd,
                                    Kind = ParticleKind.Dust,
                                    X = d.X + (_rnd.NextDouble() - 0.5) * 6,
                                    Y = d.TargetY - 1 - _rnd.NextDouble() * 3,
                                    Vx = (_rnd.NextDouble() - 0.5) * 45,
                                    Vy = -15 - _rnd.NextDouble() * 35,
                                    Size = 1.2,
                                    Life = 0.22
                                });
                            }
                        }
                    }
                    // Alsó jégcsapról eső csepp: a képernyő alján vagy a tálcán eltűnik
                    else if (!d.FromTop && (d.Y >= Height + 20 || IsCoveredByTaskbar(d.X, d.Y)))
                    {
                        _droplets.RemoveAt(i--);
                        continue;
                    }
                }
            }
        }

        /// <summary>
        /// Egérkattintás feldolgozása: a jégcsapokra kattintva széttörhetjük őket!
        /// A nagyobb jégcsapok több kattintásra törnek szét.
        /// </summary>
        public void HandleMouseClick(double screenX, double screenY)
        {
            if (_s.Season == AppSeason.Autumn) return;
            if (!_s.ShowIcicles) return;

            for (int b = 0; b < _bankList.Count; b++)
            {
                var bank = _bankList[b];
                if (bank.TryHitIcicle(screenX, screenY, out bool shattered, out double hx, out double hy, out double icLen))
                {
                    int budget = MaxBudget();
                    if (budget > 0)
                    {
                        if (shattered)
                        {
                            // Látványos szétrobbanó jégcsap-szilánkok!
                            int shardCount = (int)Math.Clamp(icLen * 0.7, 7, 14);
                            for (int k = 0; k < shardCount && _particleCount < budget; k++)
                            {
                                bool isDust = _rnd.NextDouble() < 0.45;
                                SpawnParticle(new Particle
                                {
                                    SourceHwnd = bank.HWnd,
                                    Kind = isDust ? ParticleKind.Dust : ParticleKind.Clump,
                                    X = hx + (_rnd.NextDouble() - 0.5) * 8,
                                    Y = hy + (_rnd.NextDouble() - 0.5) * (icLen * 0.6),
                                    Vx = (_rnd.NextDouble() - 0.5) * 130,
                                    Vy = -25 + _rnd.NextDouble() * 110,
                                    Size = isDust ? 1.6 : 2.4 + _rnd.NextDouble() * 2.5,
                                    Rot = _rnd.NextDouble() * 6.28,
                                    RotSpeed = (_rnd.NextDouble() - 0.5) * 12,
                                    Life = 0.45 + _rnd.NextDouble() * 0.45,
                                    Variant = _rnd.Next(SnowSprites.ClumpVariants)
                                });
                            }
                        }
                        else
                        {
                            // Kis szilánkok és jégpor pattan le az ütéstől
                            for (int k = 0; k < 3 && _particleCount < budget; k++)
                            {
                                SpawnParticle(new Particle
                                {
                                    SourceHwnd = bank.HWnd,
                                    Kind = ParticleKind.Dust,
                                    X = hx + (_rnd.NextDouble() - 0.5) * 4,
                                    Y = hy + (_rnd.NextDouble() - 0.5) * 6,
                                    Vx = (_rnd.NextDouble() - 0.5) * 60,
                                    Vy = -10 + _rnd.NextDouble() * 50,
                                    Size = 1.3,
                                    Life = 0.3
                                });
                            }
                        }
                    }
                    return;
                }
            }
        }

        internal double GetGroundY(double x)
        {
            // 1. Megnézzük, hogy az adott X koordinátán van-e tálca
            for (int i = 0; i < _windows.Count; i++)
            {
                var w = _windows[i];
                if (w.IsTaskbar && x >= w.Rect.Left && x <= w.Rect.Right)
                {
                    return w.Rect.Top;
                }
            }

            // 2. Ha nincs külön tálca ablak ezen a ponton, a képernyő munkaterületének alja
            for (int s = 0; s < _screens.Count; s++)
            {
                var sc = _screens[s];
                if (x >= sc.Bounds.Left && x <= sc.Bounds.Right)
                {
                    return sc.WorkingArea.Bottom;
                }
            }

            return Height - 40;
        }

        private double GetTotalScreenWorkingWidth()
        {
            if (_screens.Count == 0) return Math.Max(100.0, Width);
            double total = 0;
            for (int s = 0; s < _screens.Count; s++)
            {
                total += _screens[s].WorkingArea.Width;
            }
            return Math.Max(100.0, total);
        }

        private double GetOpenSkyWidth()
        {
            double total = 0;
            for (int i = 0; i < _openSkyIntervals.Count; i++)
            {
                total += Math.Max(0.0, _openSkyIntervals[i].MaxX - _openSkyIntervals[i].MinX);
            }
            return total;
        }

        private SnowFlake RespawnFlake(double remainingDist)
        {
            var nf = NewFlake(false);
            if (nf.Y > -9000)
            {
                double extraDelay = Math.Clamp(remainingDist, 0.0, Height);
                nf.Y = -nf.Size * 3 - _rnd.NextDouble() * 50 - extraDelay;
                nf.PrevY = nf.Y;
            }
            return nf;
        }

        private void UpdateFlakes(double dt, CursorState cur)
        {
            double windDrift = _s.WindStrength * 60.0;
            double damp = Math.Exp(-2.6 * dt);

            bool mouse = _s.MouseRepelEnabled && cur.Valid;
            double radius = 0, radiusSq = 0;
            if (mouse)
            {
                radius = _s.MouseRepelRadius + Math.Min(cur.Speed * 0.06 * _s.MouseSpeedSensitivity, 120);
                radiusSq = radius * radius;
            }
            double radialAccel = 520.0 * _s.MouseWindPower;
            double dragAccel = 1.5 * _s.MouseSpeedSensitivity;

            double totalW = GetTotalScreenWorkingWidth();
            double openW = GetOpenSkyWidth();
            double ratio = totalW > 0 ? (openW / totalW) : 1.0;
            int targetActiveCount = (int)Math.Round(_s.FlakeCount * Math.Clamp(ratio, 0.0, 1.0));

            for (int i = 0; i < _flakes.Count; i++)
            {
                var f = _flakes[i];

                // Ha az ablakok kitakarása miatt arányosan kevesebb pehely kell a szabad égboltra,
                // a felesleges pelyheket parkolópályára tesszük (-9999), így a szabad részen
                // a hóesés sűrűsége hajszálpontosan azonos marad, nem alakul ki természetellenes torlódás!
                if (i >= targetActiveCount)
                {
                    if (f.Y != -9999)
                    {
                        f.X = -9999;
                        f.Y = -9999;
                        f.PrevY = -9999;
                    }
                    continue;
                }

                // Ha korábban parkolópályán volt, de az égbolt megnyílt (pl. minimalizálták/bezárták a maximalizált ablakot):
                // A pelyheket elszórtan a teljes magasságban hozzuk létre (nem pedig egyszerre a felső szélről zuhannak le)
                if (f.Y <= -9000)
                {
                    _flakes[i] = NewFlake(true);
                    continue;
                }

                if (mouse)
                {
                    double dx = f.X - cur.X;
                    double dy = f.Y - cur.Y;
                    double d2 = dx * dx + dy * dy;
                    if (d2 < radiusSq)
                    {
                        double d = Math.Sqrt(d2) + 0.001;
                        double fall = 1 - d / radius;
                        fall *= fall;
                        double weight = 1.15 - 0.5 * f.Depth;

                        double ax = (dx / d) * radialAccel * fall + cur.Vx * dragAccel * fall;
                        double ay = (dy / d) * radialAccel * fall * 0.8 + cur.Vy * dragAccel * fall;
                        f.Dvx = Math.Clamp(f.Dvx + ax * weight * dt, -900, 900);
                        f.Dvy = Math.Clamp(f.Dvy + ay * weight * dt, -900, 900);
                    }
                }

                f.PrevY = f.Y;
                double sway = Math.Sin(_time * f.SwaySpeed + f.SwayPhase) * f.SwayAmp;
                f.X += (sway + windDrift * (0.5 + f.Depth) + f.Dvx) * dt;
                f.Y += (f.FallVy + f.Dvy) * dt;
                f.Dvx *= damp;
                f.Dvy *= damp;
                f.Rot += f.RotSpeed * dt;

                if (_s.AccumulateOnWindows && _bankList.Count > 0 && TryLand(f, cur))
                {
                    _flakes[i] = RespawnFlake(Height - f.Y);
                    continue;
                }

                // Ha a hópehely elérte a képernyő alját vagy elhagyta a képernyőt
                if (f.Y >= Height + 25 || f.X < -90 || f.X > Width + 90)
                {
                    _flakes[i] = RespawnFlake(0);
                }
            }
        }

        private bool TryLand(SnowFlake f, CursorState cur)
        {
            // Előtéri életlen bokeh effekt és egérszél által elfújt pelyhek nem építenek hóréteget
            if (f.Kind == FlakeKind.Bokeh) return false;
            if (f.Dvx * f.Dvx + f.Dvy * f.Dvy > 225.0) return false;

            // Ha a felhasználó épp az egérrel söpör a pehely közvetlen helyén, ne rakódjon le a hó a kurzor alatt
            if (cur.Valid && cur.Speed >= 15.0)
            {
                double cdx = f.X - cur.X;
                double cdy = f.Y - cur.Y;
                const double sweepR = 13.0;
                if (cdx * cdx + cdy * cdy <= sweepR * sweepR) return false;
            }

            double foot = f.Y + f.Size * 0.25;
            double prevFoot = f.PrevY + f.Size * 0.25;

            for (int b = 0; b < _bankList.Count; b++)
            {
                var bank = _bankList[b];
                double activeLeft = bank.Left + SnowBank.EdgeMargin;
                double activeRight = bank.Left + bank.Width - SnowBank.EdgeMargin;
                if (f.X < activeLeft + 2 || f.X > activeRight - 2) continue;

                double surface = bank.Top - bank.HeightAt(f.X);
                if (foot < surface || prevFoot > surface + 3 || foot > bank.Top + 16) continue;

                // Csak akkor rakódhat le a hó az ablak tetejére, ha az ablak pereme feletti rész
                // nem takart egy feljebb lévő (előtéri) ablak által!
                if (IsCoveredByHigher(f.X, bank.Top - 4, bank.HWnd) ||
                    IsCoveredByHigher(f.X, surface - 2, bank.HWnd) ||
                    IsCoveredByHigher(f.X, bank.Top + 2, bank.HWnd))
                {
                    continue;
                }

                double effSize = Math.Min(f.Size, 4.2);
                double volume = effSize * effSize * 2.2 * _s.AccumulationSpeed;
                double maxH = _s.LimitSnowHeight ? _s.MaxSnowHeight : 9999.0;
                bank.Add(f.X, volume, maxH);

                if (_particleCount < MaxBudget())
                {
                    SpawnParticle(new Particle
                    {
                        SourceHwnd = bank.HWnd,
                        Kind = ParticleKind.Settle,
                        X = f.X,
                        Y = surface,
                        Size = f.Size,
                        Life = 0.35,
                        Variant = 0
                    });
                }
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ egérrel söprés

        private static double DistToSegment(double px, double py, double x0, double y0, double x1, double y1, out double closestX, out double closestY)
        {
            double vx = x1 - x0;
            double vy = y1 - y0;
            double lenSq = vx * vx + vy * vy;
            if (lenSq < 1e-4)
            {
                closestX = x1;
                closestY = y1;
                double dx0 = px - x1;
                double dy0 = py - y1;
                return Math.Sqrt(dx0 * dx0 + dy0 * dy0);
            }
            double t = Math.Clamp(((px - x0) * vx + (py - y0) * vy) / lenSq, 0.0, 1.0);
            closestX = x0 + t * vx;
            closestY = y0 + t * vy;
            double dx = px - closestX;
            double dy = py - closestY;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private void Sweep(CursorState cur, double dt)
        {
            if (!cur.Valid || cur.Speed < 15) return;

            double speed = cur.Speed;
            // Szigorúan csak maga az egérkurzor söpörje le a havat (ne a kurzor távoli környezete!)
            double R = 9.0 + Math.Min(speed * 0.004, 4.0);
            int emitted = 0;
            int budget = MaxBudget();

            double segMinX = Math.Min(cur.PrevX, cur.X);
            double segMaxX = Math.Max(cur.PrevX, cur.X);
            double segMinY = Math.Min(cur.PrevY, cur.Y);
            double segMaxY = Math.Max(cur.PrevY, cur.Y);

            for (int b = 0; b < _bankList.Count; b++)
            {
                var bank = _bankList[b];
                if (segMaxX < bank.Left - R - 4 || segMinX > bank.Left + bank.Width + R + 4) continue;
                if (segMaxY < bank.Top - bank.Peak - 10 || segMinY > bank.Top + 12) continue;

                double activeLeft = bank.Left + SnowBank.EdgeMargin;
                int c0 = Math.Max(0, (int)((segMinX - R - activeLeft) / SnowBank.Step));
                int c1 = Math.Min(bank.Count - 1, (int)((segMaxX + R - activeLeft) / SnowBank.Step));
                bool changed = false;

                for (int i = c0; i <= c1; i++)
                {
                    float h = Math.Max(bank.H[i], bank.T[i]);
                    if (h <= 0.005f) continue;

                    double px = activeLeft + i * SnowBank.Step;
                    double py = bank.Top - bank.H[i] * 0.5;

                    // Ha a hóréteg ezen pontja egy feljebb lévő (előtéri) ablak alatt van, ne söpörhessük le a takart havat
                    if (IsCoveredByHigher(px, py, bank.HWnd)) continue;

                    DistToSegment(px, py, cur.PrevX, cur.PrevY, cur.X, cur.Y, out double cx, out double cy);
                    double dx = Math.Abs(px - cx);
                    if (dx > R) continue;

                    // Vertikálisan is konkrétan a hóréteget kell érintenie a kurzornak
                    if (cy < bank.Top - h - 7.0 || cy > bank.Top + 9.0) continue;

                    double f = 1.0 - dx / R;
                    double frac = Math.Clamp(f * (0.65 + speed / 500.0), 0.25, 1.0);

                    float prevH = bank.H[i];
                    float removed = (float)(prevH * frac);

                    // Ha a kurzor áthaladt rajta, H[i]-t és T[i]-t is 0-ra töröljük az adott pontban
                    if (prevH - removed < 1.0f || f > 0.30)
                    {
                        removed = prevH;
                        bank.H[i] = 0f;
                        bank.T[i] = 0f;
                    }
                    else
                    {
                        float newH = Math.Max(0f, prevH - removed);
                        bank.H[i] = newH;
                        bank.T[i] = newH;
                    }
                    changed = true;

                    if (budget > 0 && removed > 0.05f)
                    {
                        _sweepSpawnAcc += removed * SnowBank.Step / 10.0;
                        while (_sweepSpawnAcc >= 1 && emitted < 8 && _particleCount < budget)
                        {
                            _sweepSpawnAcc -= 1;
                            emitted++;
                            double dir = cur.Vx != 0 ? Math.Sign(cur.Vx) : (px >= cx ? 1 : -1);
                            bool isClump = _rnd.NextDouble() < 0.28; // 72% finom porhó a valósághű porzó hatáshoz
                            SpawnParticle(new Particle
                            {
                                SourceHwnd = bank.HWnd,
                                Kind = isClump ? ParticleKind.Clump : ParticleKind.Dust,
                                X = px + (_rnd.NextDouble() - 0.5) * 5,
                                Y = bank.Top - prevH * _rnd.NextDouble(),
                                Vx = cur.Vx * 0.35 + dir * (30 + _rnd.NextDouble() * 100),
                                Vy = cur.Vy * 0.2 - 35 - _rnd.NextDouble() * 120,
                                Size = isClump ? 2.2 + _rnd.NextDouble() * 2.5 : 1.4 + _rnd.NextDouble() * 2.2,
                                Rot = _rnd.NextDouble() * 6.28,
                                RotSpeed = (_rnd.NextDouble() - 0.5) * 8,
                                Life = isClump ? (0.45 + _rnd.NextDouble() * 0.45) : (0.6 + _rnd.NextDouble() * 0.5),
                                Variant = _rnd.Next(SnowSprites.ClumpVariants)
                            });
                        }
                    }
                }

                if (changed)
                {
                    float newPeak = 0;
                    for (int i = 0; i < bank.H.Length; i++)
                    {
                        if (bank.H[i] > newPeak) newPeak = bank.H[i];
                    }
                    bank.Peak = newPeak;
                    bank.MarkDirty();
                }
            }
            if (_sweepSpawnAcc > 20) _sweepSpawnAcc = 0;
        }

        // ------------------------------------------------------------------ lavina és részecskék

        private void Avalanche(SnowBank bank, double vx, double vy)
        {
            int budget = MaxBudget();
            if (_s.FallOffOnWindowMove && bank.Peak >= 0.8f && budget > 0)
            {
                int step = _s.AnimationQuality == 1 ? 8 : 4;
                double activeLeft = bank.Left + SnowBank.EdgeMargin;

                for (int i = 0; i < bank.Count && _particleCount < budget; i += step)
                {
                    float h = bank.H[i];
                    if (h < 0.8f) continue;

                    double x0 = activeLeft + i * SnowBank.Step;
                    int n = 1 + (int)(h / 6.0);
                    for (int k = 0; k < n && _particleCount < budget; k++)
                    {
                        SpawnParticle(new Particle
                        {
                            SourceHwnd = bank.HWnd,
                            Kind = ParticleKind.Clump,
                            X = x0 + _rnd.NextDouble() * 8,
                            Y = bank.Top - _rnd.NextDouble() * h,
                            Size = 2.8 + _rnd.NextDouble() * 4.0 * (0.5 + h / Math.Max(8.0, _s.MaxSnowHeight)),
                            Vx = vx * 0.4 + Gauss() * 34,
                            Vy = vy * 0.25 - _rnd.NextDouble() * 30,
                            Rot = _rnd.NextDouble() * 6.28,
                            RotSpeed = (_rnd.NextDouble() - 0.5) * 5,
                            Life = 0.7 + _rnd.NextDouble() * 0.7,
                            Variant = _rnd.Next(SnowSprites.ClumpVariants)
                        });
                    }
                }
            }
            bank.Clear();
        }

        private void SpawnParticle(in Particle p)
        {
            if (_particleCount < MaxParticles)
            {
                _particles[_particleCount++] = p;
            }
        }

        private void UpdateParticles(double dt)
        {
            double dragClump = Math.Exp(-0.5 * dt);
            double dragDust = Math.Exp(-1.8 * dt);
            int budget = MaxBudget();

            for (int i = _particleCount - 1; i >= 0; i--)
            {
                ref var p = ref _particles[i];
                p.Age += dt;
                if (p.Age >= p.Life || p.Y > Height + 40)
                {
                    // O(1) törlés memóriacsúsztatás nélkül
                    _particles[i] = _particles[_particleCount - 1];
                    _particleCount--;
                    continue;
                }

                switch (p.Kind)
                {
                    case ParticleKind.Clump:
                        p.Vy += 980.0 * dt;
                        p.Vx *= dragClump;
                        p.X += p.Vx * dt;
                        p.Y += p.Vy * dt;
                        p.Rot += p.RotSpeed * dt;

                        // Leeső hódarabból szálló hópor
                        if (_s.AnimationQuality == 2 && _particleCount < budget && _rnd.NextDouble() < 14 * dt)
                        {
                            SpawnParticle(new Particle
                            {
                                SourceHwnd = p.SourceHwnd,
                                Kind = ParticleKind.Dust,
                                X = p.X + (_rnd.NextDouble() - 0.5) * p.Size,
                                Y = p.Y + (_rnd.NextDouble() - 0.3) * p.Size,
                                Vx = p.Vx * 0.15 + Gauss() * 16,
                                Vy = p.Vy * 0.1 + Gauss() * 8,
                                Size = 1.4 + _rnd.NextDouble() * 1.5,
                                Life = 0.4 + _rnd.NextDouble() * 0.35
                            });
                        }
                        break;

                    case ParticleKind.Dust:
                        p.Vy += 130.0 * dt;
                        p.Vx *= dragDust;
                        p.Vy *= dragDust;
                        p.X += p.Vx * dt;
                        p.Y += p.Vy * dt;
                        break;
                }
            }
        }

        // ------------------------------------------------------------------ létrehozás

        private SnowFlake NewFlake(bool initial)
        {
            double min = _s.FlakeSizeMin;
            double max = Math.Max(min + 0.5, _s.FlakeSizeMax);
            double d = _rnd.NextDouble();

            var f = new SnowFlake
            {
                Depth = d,
                Size = min + (max - min) * d,
                FallVy = (26 + d * 74) * _s.FallSpeed * (0.9 + _rnd.NextDouble() * 0.2),
                Opacity = 0.5 + 0.45 * d,
                Rot = _rnd.NextDouble() * Math.PI * 2,
                RotSpeed = (_rnd.NextDouble() - 0.5) * 1.8,
                SwayPhase = _rnd.NextDouble() * Math.PI * 2,
                SwaySpeed = 0.6 + _rnd.NextDouble() * 1.6,
                SwayAmp = 8 + _rnd.NextDouble() * 22,
                Variant = _rnd.Next(SnowSprites.CrystalVariants)
            };

            if (_openSkyIntervals.Count == 0)
            {
                // Ha minden képernyőtető vagy monitor le van fedve, a pehely nem látható helyre kerül
                f.X = -9999;
                f.Y = -9999;
                f.PrevY = -9999;
            }
            else
            {
                double totalWidth = 0;
                for (int i = 0; i < _openSkyIntervals.Count; i++)
                {
                    totalWidth += (_openSkyIntervals[i].MaxX - _openSkyIntervals[i].MinX);
                }

                double r = _rnd.NextDouble() * totalWidth;
                double chosenX = _openSkyIntervals[0].MinX;
                for (int i = 0; i < _openSkyIntervals.Count; i++)
                {
                    double segW = _openSkyIntervals[i].MaxX - _openSkyIntervals[i].MinX;
                    if (r <= segW || i == _openSkyIntervals.Count - 1)
                    {
                        chosenX = _openSkyIntervals[i].MinX + r;
                        break;
                    }
                    r -= segW;
                }

                f.X = chosenX;
                f.Y = initial ? _rnd.NextDouble() * Height : -f.Size * 3 - _rnd.NextDouble() * 60;
                f.PrevY = f.Y;
            }

            switch (_s.FlakeStyle)
            {
                case 1:
                    f.Kind = d < 0.25 ? FlakeKind.Dust : (d > 0.9 ? FlakeKind.Bokeh : FlakeKind.Puff);
                    break;
                case 2:
                    f.Kind = d < 0.3 ? FlakeKind.Dust : FlakeKind.Crystal;
                    break;
                default:
                    f.Kind = d < 0.25 ? FlakeKind.Dust
                        : (d > 0.92 ? FlakeKind.Bokeh
                        : (_rnd.NextDouble() < 0.55 ? FlakeKind.Crystal : FlakeKind.Puff));
                    break;
            }
            return f;
        }

        private double Gauss()
        {
            double u1 = 1.0 - _rnd.NextDouble();
            double u2 = _rnd.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        // ------------------------------------------------------------------ 120 FPS villámgyors rajzolás

        public void Render(DrawingContext dc)
        {
            if (_s.Season == AppSeason.Autumn)
            {
                _leafSim.Render(dc, _windows, IsInsideAnyWindow);
                return;
            }

            // 1. Hóhalmok és jégcsapok az ablakokon:
            // Z-sorrendben hátulról előre rajzolva, és a feljebb lévő (előtéri) ablakok területét kitakarva (PushClip)!
            // Így a háttérben lévő ablak hava sosem lóg át az előtérben lévő ablakra!
            for (int i = _bankList.Count - 1; i >= 0; i--)
            {
                var bank = _bankList[i];
                if (bank.HWnd == IntPtr.Zero) continue;

                int wIndex = _windows.FindIndex(w => w.HWnd == bank.HWnd);
                Geometry? clipGeom = null;

                double maxH = Math.Max(20.0, _s.LimitSnowHeight ? _s.MaxSnowHeight : 200.0);
                Rect bankBounds = new Rect(
                    bank.Left - 8.0,
                    bank.Top - maxH - 8.0,
                    bank.Width + 16.0,
                    bank.Height + maxH + 60.0);

                // 1. A normál alkalmazásablakok díszítését a tálca MINDIG kitakarja (akár aktív előtérben lévő ablak, akár háttérbeli),
                // hiszen a Windowsban a tálca mindig az alkalmazások felett ül (Z-sorrendben elöl van)!
                if (!bank.IsTaskbar)
                {
                    for (int t = 0; t < _windows.Count; t++)
                    {
                        if (_windows[t].IsTaskbar)
                        {
                            Rect tbRect = _windows[t].Rect;
                            if (tbRect.IntersectsWith(bankBounds))
                            {
                                clipGeom ??= new RectangleGeometry(bankBounds);
                                clipGeom = Geometry.Combine(clipGeom, new RectangleGeometry(tbRect), GeometryCombineMode.Exclude, null);
                            }
                        }
                    }
                }

                // 2. Előtéri (Z-sorrendben magasabb) egyéb ablakok általi kitakarás
                if (wIndex > 0)
                {
                    for (int j = 0; j < wIndex; j++)
                    {
                        var higher = _windows[j];
                        if (higher.HWnd == bank.HWnd || higher.IsTaskbar) continue;

                        Rect higherArea = new Rect(
                            higher.Rect.Left,
                            higher.Rect.Top,
                            higher.Rect.Width,
                            higher.Rect.Height);

                        if (higherArea.IntersectsWith(bankBounds))
                        {
                            clipGeom ??= new RectangleGeometry(bankBounds);
                            clipGeom = Geometry.Combine(clipGeom, new RectangleGeometry(higherArea), GeometryCombineMode.Exclude, null);
                        }
                    }
                }

                if (clipGeom != null)
                {
                    if (clipGeom.IsEmpty()) continue;
                    dc.PushClip(clipGeom);
                }

                bank.Render(
                    dc, _pileFill, _pileEdge, _pileHalo,
                    _icicleFill, _icicleHighlight, _icicleHalo,
                    _s.ShowIcicles, _s.IcicleDensity, Height);

                if (clipGeom != null)
                {
                    dc.Pop();
                }
            }

            // 2. Részecskék (lavina és söprés) - közvetlen sprite kirajzolás, NULLA PushOpacity / PushTransform
            for (int i = 0; i < _particleCount; i++)
            {
                ref readonly var p = ref _particles[i];
                if (p.Y < -20 || p.Y > Height + 30) continue;

                if (p.SourceHwnd != IntPtr.Zero)
                {
                    // Csak a forrásablaknál feljebb (előtérben) lévő ablakok, vagy a tálca takarhatja el a lesöpört port
                    if (IsCoveredByHigher(p.X, p.Y, p.SourceHwnd)) continue;
                    if (IsCoveredByTaskbar(p.X, p.Y)) continue;
                }
                else
                {
                    if (IsOccluded(p.X, p.Y, p.Size * 1.1)) continue;
                }

                double t = p.Age / p.Life;
                switch (p.Kind)
                {
                    case ParticleKind.Clump:
                    {
                        double scale = 1.0 - 0.35 * t * t;
                        double op = t < 0.65 ? 1.0 : 1.0 - (t - 0.65) / 0.35;
                        double dia = p.Size * 2.2 * scale;
                        var img = SnowSprites.GetClump(p.Variant, p.Rot, op);
                        dc.DrawImage(img, new Rect(p.X - dia * 0.5, p.Y - dia * 0.5, dia, dia));
                        break;
                    }
                    case ParticleKind.Dust:
                    {
                        double op = (1.0 - t) * 0.8;
                        double dia = p.Size * 2.0;
                        var img = SnowSprites.GetDust(op);
                        dc.DrawImage(img, new Rect(p.X - dia * 0.5, p.Y - dia * 0.5, dia, dia));
                        break;
                    }
                    case ParticleKind.Settle:
                    {
                        double op = (1.0 - t) * 0.9;
                        double dia = p.Size * 2.2 * (1.0 - 0.4 * t);
                        var img = SnowSprites.GetPuff(op);
                        dc.DrawImage(img, new Rect(p.X - dia * 0.5, p.Y - dia * 0.5, dia, dia));
                        break;
                    }
                }
            }

            // 3. Hulló hópelyhek - nyitott ablakokra vagy a tálcára nem esik hó (maga a tálca vagy ablak fedi el)
            for (int i = 0; i < _flakes.Count; i++)
            {
                var f = _flakes[i];
                if (f.Y < -20 || f.Y > Height + 30) continue;

                if (IsOccluded(f.X, f.Y, f.Size * 1.3)) continue;

                switch (f.Kind)
                {
                    case FlakeKind.Dust:
                    {
                        double dia = f.Size * 1.6;
                        var img = SnowSprites.GetDust(f.Opacity * 0.8);
                        dc.DrawImage(img, new Rect(f.X - dia * 0.5, f.Y - dia * 0.5, dia, dia));
                        break;
                    }
                    case FlakeKind.Puff:
                    {
                        double dia = f.Size * 2.6;
                        var img = SnowSprites.GetPuff(f.Opacity);
                        dc.DrawImage(img, new Rect(f.X - dia * 0.5, f.Y - dia * 0.5, dia, dia));
                        break;
                    }
                    case FlakeKind.Crystal:
                    {
                        double dia = f.Size * 2.6;
                        var img = SnowSprites.GetCrystal(f.Variant, f.Rot, f.Opacity);
                        dc.DrawImage(img, new Rect(f.X - dia * 0.5, f.Y - dia * 0.5, dia, dia));
                        break;
                    }
                }
            }

            // 4. Előtéri bokeh pelyhek (külön menet a mélységérzethez)
            for (int i = 0; i < _flakes.Count; i++)
            {
                var f = _flakes[i];
                if (f.Kind == FlakeKind.Bokeh)
                {
                    if (f.Y < -20 || f.Y > Height + 30) continue;

                    if (IsOccluded(f.X, f.Y, f.Size * 2.3)) continue;

                    double dia = f.Size * 4.6;
                    var img = SnowSprites.GetBokeh(f.Opacity * 0.6);
                    dc.DrawImage(img, new Rect(f.X - dia * 0.5, f.Y - dia * 0.5, dia, dia));
                }
            }

            // 5. Vízcseppek a jégcsapokról (előtéri ablakok és tálca általi kitakarással)
            if (_droplets.Count > 0)
            {
                for (int i = 0; i < _droplets.Count; i++)
                {
                    var d = _droplets[i];
                    if (IsCoveredByHigher(d.X, d.Y, d.SourceHwnd) || IsCoveredByTaskbar(d.X, d.Y)) continue;

                    if (d.Splashed)
                    {
                        double scale = d.SplashAge / 0.15;
                        double op = (1.0 - scale) * 0.85;
                        var splashBrush = new SolidColorBrush(Color.FromArgb((byte)(op * 255), 215, 240, 255));
                        dc.DrawEllipse(splashBrush, null, new Point(d.X, d.Y), 2.5 + scale * 4.0, 1.0 + scale * 1.5);
                    }
                    else
                    {
                        dc.DrawEllipse(_dropletBrush, null, new Point(d.X, d.Y), 1.5, 2.6);
                        dc.DrawEllipse(_dropletGlint, null, new Point(d.X - 0.3, d.Y - 0.7), 0.6, 0.8);
                    }
                }
            }
        }
    }
}
