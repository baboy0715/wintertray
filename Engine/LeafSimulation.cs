using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using WinterTray.Native;

namespace WinterTray.Engine
{
    /// <summary>
    /// Az őszi szimuláció motorja:
    /// - Őszi esőzés finom ferde esőcsíkokkal és csobbanó effektussal az ablakok tetején és az asztalon.
    /// - Nagyon ritka, hangulatosan hulló őszi falevelek (szinuszos lebegéssel és forgással).
    /// - Garantált ablak-ütközés: a levél PONTOSAN abban a szögben és formában fekszik rá az ablak tetejére, ahogy ráesett, és sosem tűnik el váratlanul!
    /// - Szigorúan zárt és korlátozott levélszám: lesöprésnél és megülésnél NEM szaporodnak a levelek!
    /// - 2 perces (vagy akár végtelen) élettartam, és 120 FPS analóg vajpuha fade out.
    /// - Tálca kezelése: beállítható, hogy a tálca tetejére is ráüljenek-e a levelek és csobbanjon-e az eső, vagy essenek tovább a tálca mögött.
    /// - Kurzor interakció (GetCursorPos): a megült levél lendületet kapva lerepül az ablakról és esik tovább!
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

        // Eső és csobbanások
        private readonly List<RainDrop> _rainDrops = new();
        private readonly List<RainSplash> _splashes = new();

        private readonly Pen _rainPen;
        private readonly Brush _splashDropBrush;
        private readonly Pen[] _splashPens;

        private double _time;
        private double _lastWidth = 1920;
        private double _lastHeight = 1080;

        public double Width => _lastWidth;
        public double Height => _lastHeight;

        public LeafSimulation(Settings settings)
        {
            _s = settings;
            LeafSprites.Init();

            // Esőcsíkok áttetsző kékes-ezüstös tolla
            var rainBrush = new SolidColorBrush(Color.FromArgb(150, 190, 215, 245));
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

            // Csobbanás karikák előre fagyasztott tollai (nulla frame-enkénti memóriaallokáció)
            _splashPens = new Pen[16];
            for (int p = 0; p < 16; p++)
            {
                byte alphaByte = (byte)(p * 12 + 15);
                var penBrush = new SolidColorBrush(Color.FromArgb(alphaByte, 200, 230, 255));
                penBrush.Freeze();
                var pen = new Pen(penBrush, 0.85);
                pen.Freeze();
                _splashPens[p] = pen;
            }
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
                SpawnFallingLeafFromLanded(l, (_rnd.NextDouble() - 0.5) * 40.0, -15.0);
            }
            _landedLeaves.Clear();
            _splashes.Clear();
        }

        public void OnWindowClosed(IntPtr hwnd, Rect oldRect)
        {
            for (int i = _landedLeaves.Count - 1; i >= 0; i--)
            {
                var l = _landedLeaves[i];
                if (l.HWnd == hwnd)
                {
                    double worldX = oldRect.Left + l.RelX;
                    double worldY = oldRect.Top + l.RelY;
                    _leaves.Add(new LeafFlake
                    {
                        X = worldX,
                        Y = worldY,
                        PrevY = worldY,
                        Size = l.Size,
                        Opacity = 1.0,
                        Depth = 0.7,
                        Rot = l.Rot,
                        RotSpeed = (_rnd.NextDouble() - 0.5) * 4.0,
                        SwayPhase = _rnd.NextDouble() * Math.PI * 2.0,
                        SwaySpeed = 1.4 + _rnd.NextDouble() * 1.5,
                        SwayAmp = 18.0 + _rnd.NextDouble() * 22.0,
                        FallVy = (40.0 + _rnd.NextDouble() * 35.0) * _s.LeafFallSpeed,
                        Vx = (_rnd.NextDouble() - 0.5) * 30.0,
                        Vy = 15.0 + _rnd.NextDouble() * 20.0,
                        Variant = l.Variant,
                        ColorVariant = l.ColorVariant
                    });
                    _landedLeaves.RemoveAt(i);
                }
            }
        }

        public void OnWindowMoved(IntPtr hwnd, Rect oldRect, Rect newRect, double vx, double vy)
        {
            double speed = Math.Sqrt(vx * vx + vy * vy);
            if (speed > 360.0)
            {
                for (int i = _landedLeaves.Count - 1; i >= 0; i--)
                {
                    var l = _landedLeaves[i];
                    if (l.HWnd == hwnd && _rnd.NextDouble() < 0.20)
                    {
                        double worldX = oldRect.Left + l.RelX;
                        double worldY = oldRect.Top + l.RelY;
                        _leaves.Add(new LeafFlake
                        {
                            X = worldX,
                            Y = worldY,
                            PrevY = worldY,
                            Size = l.Size,
                            Opacity = 1.0,
                            Depth = 0.7,
                            Rot = l.Rot,
                            RotSpeed = (_rnd.NextDouble() - 0.5) * 6.0,
                            SwayPhase = _rnd.NextDouble() * Math.PI * 2.0,
                            SwaySpeed = 1.5,
                            SwayAmp = 20.0,
                            FallVy = (42.0 + _rnd.NextDouble() * 35.0) * _s.LeafFallSpeed,
                            Vx = vx * 0.35 + (_rnd.NextDouble() - 0.5) * 40.0,
                            Vy = vy * 0.30 - 10.0,
                            Variant = l.Variant,
                            ColorVariant = l.ColorVariant
                        });
                        _landedLeaves.RemoveAt(i);
                    }
                }
            }
        }

        private void SpawnFallingLeafFromLanded(LandedLeaf l, double pushVx, double pushVy)
        {
            _leaves.Add(new LeafFlake
            {
                X = l.RelX,
                Y = l.RelY,
                PrevY = l.RelY,
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

            // 1. Esőcseppek és csobbanások
            if (_s.RainEnabled)
            {
                UpdateRain(dt, windows, isCoveredByHigher);
            }

            // 2. Megült levelek (120 FPS sima fade out és kurzorral leröpítés)
            UpdateLandedLeaves(dt, cursor, windows);

            // 3. Hulló falevelek (szinuszos ringatózás, forgás és ráfekvés az ablakra)
            UpdateFallingLeaves(dt, cursor, windows, isCoveredByHigher, isInsideAnyWindow);
        }

        // ------------------------------------------------------------------ ESŐ ÉS CSOBBANÁSOK

        private void UpdateRain(
            double dt,
            List<TrackedWindow> windows,
            Func<double, double, IntPtr, bool> isCoveredByHigher)
        {
            double speedMult = Math.Clamp(_s.RainFallSpeed, 0.4, 2.5);
            double wind = _s.LeafWindStrength * 80.0;

            // Csobbanások
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
                bool hitLandedLeaf = false;

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

                            // Ha a csepp olyan helyre esik a párkányon, ahol megült falevél van,
                            // a csepp a levélre hull (nem csobban fehér karikaként az ablakpárkányon)
                            double relX = drop.X - win.Rect.Left;
                            for (int l = 0; l < _landedLeaves.Count; l++)
                            {
                                var leaf = _landedLeaves[l];
                                if (leaf.HWnd == win.HWnd)
                                {
                                    if (Math.Abs(relX - leaf.RelX) <= leaf.Size * 0.70)
                                    {
                                        hitLandedLeaf = true;
                                        break;
                                    }
                                }
                            }
                            break;
                        }
                    }
                }

                // Ha nem ablakba, hanem a tálcára vagy a képernyő legaljára ér
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
                    // CSAK akkor hozunk létre csobbanás animációt, ha NEM falevélre esett!
                    if (!hitLandedLeaf && _splashes.Count < MaxSplashes)
                    {
                        _splashes.Add(new RainSplash
                        {
                            X = drop.X,
                            Y = hitSurfaceY,
                            Age = 0,
                            MaxLife = 0.14 + _rnd.NextDouble() * 0.06,
                            Radius = 3.5 + _rnd.NextDouble() * 4.5,
                            HWnd = hitHwnd
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
            const double fadeDuration = 5.0; // 5 másodperces folytonos, analóg fade out

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
                double worldX = win.Rect.Left + leaf.RelX;
                double worldY = win.Rect.Top + leaf.RelY;

                // INTERAKCIÓ: egérrel lerepítés
                if (checkMouse)
                {
                    double dx = worldX - cur.X;
                    double dy = worldY - cur.Y;
                    double distSq = dx * dx + dy * dy;

                    if (distSq <= triggerDistSq)
                    {
                        double dirX = dx != 0 ? Math.Sign(dx) : (_rnd.NextDouble() > 0.5 ? 1 : -1);
                        double pushVx = cur.Vx * 0.45 + dirX * (45.0 + _rnd.NextDouble() * 65.0);
                        double pushVy = -20.0 - _rnd.NextDouble() * 45.0 + cur.Vy * 0.2;

                        _leaves.Add(new LeafFlake
                        {
                            X = worldX,
                            Y = worldY,
                            PrevY = worldY,
                            Size = leaf.Size,
                            Opacity = 1.0,
                            Depth = 0.8,
                            Rot = leaf.Rot,
                            RotSpeed = (_rnd.NextDouble() - 0.5) * 7.0,
                            SwayPhase = _rnd.NextDouble() * Math.PI * 2.0,
                            SwaySpeed = 1.4 + _rnd.NextDouble() * 1.4,
                            SwayAmp = 18.0 + _rnd.NextDouble() * 24.0,
                            FallVy = (42.0 + _rnd.NextDouble() * 32.0) * _s.LeafFallSpeed,
                            Vx = pushVx,
                            Vy = pushVy,
                            Variant = leaf.Variant,
                            ColorVariant = leaf.ColorVariant
                        });

                        _landedLeaves.RemoveAt(i);
                        continue;
                    }
                }

                // ÉLETTARTAM: 0 = soha nem tűnik el! > 0 esetén a beállított ideig marad, utána finoman elhalványul
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

                // ABLAK-ÜTKÖZÉS: PONTOSAN úgy marad ott, ahogy ráesik, és garantáltan megül!
                if (_s.AccumulateLeavesOnWindows && TryLandLeaf(f, windows, isCoveredByHigher))
                {
                    // A levél megült: TÖRLŐDIK a hulló levelek listájából!
                    _leaves.RemoveAt(i);
                    i--;

                    // CSAK akkor születik új levél, ha a teljes populáció még nem érte el a célméretet
                    if (_leaves.Count + _landedLeaves.Count < targetLeafCount)
                    {
                        _leaves.Add(NewLeaf(false));
                    }
                    continue;
                }

                // Csak akkor tekintjük ablak belsejében lévőnek, ha mélyebben az ablak címsora alatt jár
                if (f.Y > 0 && isInsideAnyWindow(f.X, f.Y - 16))
                {
                    _leaves[i] = NewLeaf(false);
                    continue;
                }

                // Képernyő alja / tálca alatti eltűnés
                double bottomLimit = _s.AccumulateOnTaskbar ? Height - 30 : Height + 30;
                if (f.Y > bottomLimit || f.X < -100 || f.X > Width + 100)
                {
                    _leaves[i] = NewLeaf(false);
                }
            }

            // Ha lecsökkent a teljes levélszám, pótoljuk fokozatosan a képernyő tetején
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

                // Átlépte a felület tetejét lefelé ebben a frame-ben, vagy épp a felület tetején jár
                bool hitLedge = (prevFoot <= surfaceY + 4 && foot >= surfaceY - 4) ||
                                (foot >= surfaceY - 2 && f.Y <= surfaceY + 22);

                if (hitLedge)
                {
                    if (isCoveredByHigher(f.X, surfaceY - 2, w.HWnd)) continue;

                    // PONTOSAN ABBAN A HELYZETBEN ÉS SZÖGBEN FEKSZIK RÁ, AHOGY RÁESETT!
                    _landedLeaves.Add(new LandedLeaf
                    {
                        HWnd = w.HWnd,
                        RelX = relX,
                        RelY = (surfaceY - w.Rect.Top) - f.Size * 0.22,
                        Size = f.Size,
                        Rot = f.Rot, // pont az esési szög!
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
            // 1. Eső csobbanások az ablakok tetején és az asztalon (MINDIG A FALEVELEK ALATT!)
            if (_s.RainEnabled)
            {
                RenderSplashes(dc, windows);
            }

            // 2. Hulló esőcsíkok a háttérben és a levegőben
            if (_s.RainEnabled)
            {
                RenderRainStreaks(dc, isInsideAnyWindow);
            }

            // 3. Megült falevelek az ablakok tetején
            // (A megült falevelek A CSOBBANÁSOK ÉS ESŐCSÍKOK FELETT rajzolódnak ki, így a csobbanás animáció garantáltan NEM takarja el a faleveleket!)
            RenderLandedLeaves(dc, windows);

            // 4. Hulló falevelek az előtérben
            RenderFallingLeaves(dc, isInsideAnyWindow);
        }

        private void RenderSplashes(DrawingContext dc, List<TrackedWindow> windows)
        {
            for (int i = 0; i < _splashes.Count; i++)
            {
                var sp = _splashes[i];
                double progress = sp.Age / sp.MaxLife;
                if (progress >= 1.0) continue;

                // Ha a csobbanás ablaka időközben bezárult vagy magasabb Z-sorrendű ablak takarja, ne rajzoljuk ki
                if (sp.HWnd != IntPtr.Zero)
                {
                    int wIdx = windows.FindIndex(w => w.HWnd == sp.HWnd);
                    if (wIdx < 0) continue;

                    bool occluded = false;
                    for (int h = 0; h < wIdx; h++)
                    {
                        if (windows[h].Rect.Contains(sp.X, sp.Y))
                        {
                            occluded = true;
                            break;
                        }
                    }
                    if (occluded) continue;
                }

                double rx = sp.Radius * (0.3 + 0.7 * progress);
                double ry = rx * 0.35;
                double alpha = (1.0 - progress) * 0.8;

                int penIdx = Math.Clamp((int)(alpha * (_splashPens.Length - 1)), 0, _splashPens.Length - 1);
                dc.DrawEllipse(null, _splashPens[penIdx], new Point(sp.X, sp.Y), rx, ry);

                // Apró felcsapódó vízcseppek
                if (progress < 0.6)
                {
                    double dropScale = 1.0 - progress / 0.6;
                    dc.DrawEllipse(_splashDropBrush, null, new Point(sp.X - rx * 0.4, sp.Y - 2.5 * dropScale), 0.75, 0.75);
                    dc.DrawEllipse(_splashDropBrush, null, new Point(sp.X + rx * 0.4, sp.Y - 2.0 * dropScale), 0.75, 0.75);
                }
            }
        }

        private void RenderRainStreaks(DrawingContext dc, Func<double, double, bool> isInsideAnyWindow)
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

        private void RenderFallingLeaves(DrawingContext dc, Func<double, double, bool> isInsideAnyWindow)
        {
            double bottomLimit = _s.AccumulateOnTaskbar ? Height - 42 : Height + 25;
            for (int i = 0; i < _leaves.Count; i++)
            {
                var f = _leaves[i];
                if (f.Y >= bottomLimit || isInsideAnyWindow(f.X, f.Y - 16)) continue;

                var img = LeafSprites.GetLeaf(f.Variant, f.ColorVariant, f.Rot);
                double dia = f.Size * 1.5;
                dc.DrawImage(img, new Rect(f.X - dia * 0.5, f.Y - dia * 0.5, dia, dia));
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
