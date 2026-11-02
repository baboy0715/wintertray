using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using WinterTray.Native;

namespace WinterTray.Engine
{
    public sealed class WaterDroplet
    {
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

        private double _time;
        private double _sweepSpawnAcc;
        private (double min, double max, int style, double speed) _lastShape;

        public double Width { get; private set; } = 1920;
        public double Height { get; private set; } = 1080;

        public SnowWorld(Settings settings)
        {
            _s = settings;
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
        }

        public void Reconfigure()
        {
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

        /// <summary>"Hó letakarítása": minden halom lezúdul.</summary>
        public void ClearAll()
        {
            foreach (var bank in _bankList) Avalanche(bank, 0, 0);
        }

        // ------------------------------------------------------------------ ablakok

        public void SetWindows(List<TrackedWindow> windows)
        {
            _windows = windows;

            var keep = new HashSet<IntPtr>();
            if (_s.AccumulateOnWindows)
            {
                foreach (var w in windows)
                {
                    if (w.IsTaskbar && !_s.AccumulateOnTaskbar) continue;
                    if (w.Rect.Top < LedgeMinTop || w.Rect.Top > Height - 40) continue;
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
                    bank.SetRect(w.Rect.Left, w.Rect.Top, w.Rect.Width, w.Rect.Height);
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
                        _bankList.Remove(bank);
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

                    OnWindowChanged(bank.HWnd, oldRect, newRect, dt);
                }
            }
        }

        /// <summary>
        /// Ablak bezárásakor / letálcázásakor azonnal feltöltjük a frissen felfedett asztalterületet
        /// eső hópelyhekkel, mintha mindvégig esett volna a háttérben (nulla felesleges korábbi számítási igénnyel).
        /// </summary>
        public void PopulateExposedArea(Rect area)
        {
            if (area.Width < 50 || area.Height < 50) return;

            double screenArea = Math.Max(1.0, Width * Height);
            double exposedArea = area.Width * area.Height;
            int targetCount = (int)Math.Clamp(
                _s.FlakeCount * (exposedArea / screenArea),
                10, Math.Min(_flakes.Count, 90));

            int reseeded = 0;
            // 1. lépés: a képernyőn kívül vagy alján járó pelyheket helyezzük át a területre
            for (int i = 0; i < _flakes.Count && reseeded < targetCount; i++)
            {
                var f = _flakes[i];
                if (f.Y > Height - 40 || f.Y < -5 || f.X < -10 || f.X > Width + 10)
                {
                    f.X = area.Left + _rnd.NextDouble() * area.Width;
                    f.Y = area.Top + _rnd.NextDouble() * area.Height;
                    f.PrevY = f.Y;
                    f.Dvx = 0;
                    f.Dvy = 0;
                    reseeded++;
                }
            }

            // 2. lépés: ha még több pehely kell, a legkevésbé feltűnő (területen kívüli) pelyheket csoportosítjuk át
            for (int i = 0; i < _flakes.Count && reseeded < targetCount; i++)
            {
                var f = _flakes[i];
                if (!area.Contains(f.X, f.Y))
                {
                    f.X = area.Left + _rnd.NextDouble() * area.Width;
                    f.Y = area.Top + _rnd.NextDouble() * area.Height;
                    f.PrevY = f.Y;
                    f.Dvx = 0;
                    f.Dvy = 0;
                    reseeded++;
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
            if (r.Top < LedgeMinTop || r.Top > Height - 40 || r.Width < 120)
            {
                Avalanche(bank, bank.Vx, bank.Vy);
                _banks.Remove(hwnd);
                _bankList.Remove(bank);
                return;
            }

            // A pozíció azonnal frissül: A HÓ ÉS A JÉGCSAPOK AZ ABLAKKAL EGYÜTT MOZOGNAK!
            bank.SetRect(r.Left, r.Top, r.Width, r.Height);

            double safeDt = Math.Clamp(dt, 0.005, 0.1);
            double vx = (r.X - oldRect.X) / safeDt;
            double vy = (r.Y - oldRect.Y) / safeDt;

            // Sebesség és gyorsulás számítása
            bank.Vx = bank.Vx * 0.35 + vx * 0.65;
            bank.Vy = bank.Vy * 0.35 + vy * 0.65;
            double speed = Math.Sqrt(vx * vx + vy * vy);

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
                            SpawnInertialClump(px, py, vx * 0.40 + Gauss() * 25, vy * 0.30 + 20 + _rnd.NextDouble() * 30, amount);
                        }
                    });
                }
            }
        }

        private void SpawnInertialClump(double x, double y, double vx, double vy, float amount)
        {
            if (_particleCount >= MaxBudget()) return;
            double size = 2.4 + Math.Min(amount, 5.0) * 0.6 + _rnd.NextDouble() * 1.6;
            SpawnParticle(new Particle
            {
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

        public bool IsInsideAnyWindow(double x, double y)
        {
            for (int i = 0; i < _windows.Count; i++)
            {
                var w = _windows[i];
                if (w.Rect.Contains(x, y)) return true;
            }
            return false;
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

        // ------------------------------------------------------------------ frissítés

        public void Update(double dt, CursorState cursor)
        {
            _time += dt;

            double maxH = _s.MaxSnowHeight;
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
                    _droplets.Add(new WaterDroplet
                    {
                        X = dx,
                        Y = dy,
                        Vy = 35.0 + _rnd.NextDouble() * 30.0,
                        FromTop = fromTop,
                        TargetY = ty,
                        Life = 4.0
                    });
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
                    // Alsó jégcsapról eső csepp: a tálca tetején / képernyő alján eltűnik
                    else if (!d.FromTop && d.Y >= Height - 42)
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

            for (int i = 0; i < _flakes.Count; i++)
            {
                var f = _flakes[i];

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

                if (_s.AccumulateOnWindows && _bankList.Count > 0 && TryLand(f))
                {
                    _flakes[i] = NewFlake(false);
                    continue;
                }

                if (f.Y > Height + 30 || f.X < -90 || f.X > Width + 90)
                {
                    _flakes[i] = NewFlake(false);
                }
            }
        }

        private bool TryLand(SnowFlake f)
        {
            double foot = f.Y + f.Size * 0.25;
            double prevFoot = f.PrevY + f.Size * 0.25;

            for (int b = 0; b < _bankList.Count; b++)
            {
                var bank = _bankList[b];
                double activeLeft = bank.Left + SnowBank.EdgeMargin;
                double activeRight = bank.Left + bank.Width - SnowBank.EdgeMargin;
                if (f.X < activeLeft + 1 || f.X > activeRight - 1) continue;

                double surface = bank.Top - bank.HeightAt(f.X);
                if (foot < surface || prevFoot > surface + 3 || foot > bank.Top + 24) continue;
                if (IsCoveredByOther(f.X, bank.Top - 3, bank.HWnd)) continue;

                double volume = f.Size * f.Size * 4.0 * _s.AccumulationSpeed;
                bank.Add(f.X, volume, _s.MaxSnowHeight);

                if (_particleCount < MaxBudget())
                {
                    SpawnParticle(new Particle
                    {
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

        private void Sweep(CursorState cur, double dt)
        {
            if (!cur.Valid || cur.Speed < 70) return;

            double speed = cur.Speed;
            double R = 26 + Math.Min(speed * 0.045, 60);
            int emitted = 0;
            int budget = MaxBudget();

            for (int b = 0; b < _bankList.Count; b++)
            {
                var bank = _bankList[b];
                if (bank.Peak < 0.8f) continue;
                if (cur.X < bank.Left - R || cur.X > bank.Left + bank.Width + R) continue;
                if (cur.Y < bank.Top - bank.Peak - R - 14 || cur.Y > bank.Top + 38) continue;

                double activeLeft = bank.Left + SnowBank.EdgeMargin;
                int c0 = Math.Max(0, (int)((cur.X - R - activeLeft) / SnowBank.Step));
                int c1 = Math.Min(bank.Count - 1, (int)((cur.X + R - activeLeft) / SnowBank.Step));
                bool changed = false;

                for (int i = c0; i <= c1; i++)
                {
                    double px = activeLeft + i * SnowBank.Step;
                    double dx = Math.Abs(px - cur.X);
                    if (dx > R) continue;

                    float h = bank.H[i];
                    if (h < 0.5f) continue;

                    double dy = cur.Y - (bank.Top - h * 0.5);
                    if (Math.Abs(dy) > R * 0.9 + h * 0.5 + 10) continue;

                    double f = 1 - dx / R;
                    double frac = Math.Clamp(f * (0.15 + speed / 1200.0) * 12.0 * dt, 0, 0.6);
                    if (frac <= 0) continue;

                    float removed = (float)(h * frac);
                    bank.H[i] -= removed;
                    bank.T[i] = Math.Max(0f, bank.T[i] * (float)(1 - frac));
                    changed = true;

                    if (budget > 0)
                    {
                        _sweepSpawnAcc += removed * SnowBank.Step / 10.0;
                        while (_sweepSpawnAcc >= 1 && emitted < 8 && _particleCount < budget)
                        {
                            _sweepSpawnAcc -= 1;
                            emitted++;
                            double dir = px >= cur.X ? 1 : -1;
                            bool isClump = _rnd.NextDouble() < 0.28; // 72% finom porhó a valósághű porzó hatáshoz
                            SpawnParticle(new Particle
                            {
                                Kind = isClump ? ParticleKind.Clump : ParticleKind.Dust,
                                X = px + (_rnd.NextDouble() - 0.5) * 5,
                                Y = bank.Top - h * _rnd.NextDouble(),
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

                if (changed) bank.MarkDirty();
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

            f.X = _rnd.NextDouble() * (Width + 160) - 80;
            f.Y = initial ? _rnd.NextDouble() * Height : -f.Size * 3 - _rnd.NextDouble() * 60;
            f.PrevY = f.Y;

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
            // 1. Hóhalmok és jégcsapok az ablakokon
            for (int i = 0; i < _bankList.Count; i++)
            {
                _bankList[i].Render(
                    dc, _pileFill, _pileEdge, _pileHalo,
                    _icicleFill, _icicleHighlight, _icicleHalo,
                    _s.ShowIcicles, _s.IcicleDensity, Height);
            }

            // 2. Részecskék (lavina és söprés) - közvetlen sprite kirajzolás, NULLA PushOpacity / PushTransform
            for (int i = 0; i < _particleCount; i++)
            {
                ref readonly var p = ref _particles[i];
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

            // 3. Hulló hópelyhek - a nyitott ablakokra vagy a tálcára nem esik hó (csak a háttérben az asztalon)
            for (int i = 0; i < _flakes.Count; i++)
            {
                var f = _flakes[i];
                if (IsInsideAnyWindow(f.X, f.Y)) continue;

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
                    if (IsInsideAnyWindow(f.X, f.Y)) continue;

                    double dia = f.Size * 4.6;
                    var img = SnowSprites.GetBokeh(f.Opacity * 0.6);
                    dc.DrawImage(img, new Rect(f.X - dia * 0.5, f.Y - dia * 0.5, dia, dia));
                }
            }

            // 5. Vízcseppek a jégcsapokról (lecseppenés az ablak aljára / eltűnés a tálca alatt)
            if (_droplets.Count > 0)
            {
                for (int i = 0; i < _droplets.Count; i++)
                {
                    var d = _droplets[i];
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
