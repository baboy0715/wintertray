using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinterTray.Engine
{
    /// <summary>
    /// Előre generált, hardveresen gyorsított sprite-ok forgatási és átlátszósági lépésekkel.
    /// Így futás közben (120 FPS-nél is) NULLA mátrix-allokáció és NULLA PushOpacity / PushTransform szükséges!
    /// </summary>
    public static class SnowSprites
    {
        public const int CrystalVariants = 8;
        public const int CrystalRotSteps = 6;  // 60 fokos forgásszimmetria: 0, 10, 20, 30, 40, 50 fok
        public const int CrystalOpSteps = 3;   // Opacitás: 1.0, 0.75, 0.50

        public const int ClumpVariants = 3;
        public const int ClumpRotSteps = 8;    // 360 fok / 8 = 45 fokonként
        public const int ClumpOpSteps = 3;     // Opacitás: 1.0, 0.70, 0.40

        public const int PuffOpSteps = 3;
        public const int BokehOpSteps = 2;
        public const int DustOpSteps = 3;

        private static ImageSource[,,] _crystals = null!;
        private static ImageSource[,,] _clumps = null!;
        private static ImageSource[] _puffs = null!;
        private static ImageSource[] _bokehs = null!;
        private static ImageSource[] _dusts = null!;

        private static bool _initialized;

        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;

            // 1. Kristályok: variáns, forgási szög, átlátszóság
            double[] crystalOps = { 1.0, 0.75, 0.50 };
            _crystals = new ImageSource[CrystalVariants, CrystalRotSteps, CrystalOpSteps];
            for (int v = 0; v < CrystalVariants; v++)
            {
                int seed = v;
                for (int r = 0; r < CrystalRotSteps; r++)
                {
                    double rad = (r * 10.0) * Math.PI / 180.0;
                    for (int o = 0; o < CrystalOpSteps; o++)
                    {
                        _crystals[v, r, o] = Render(48, (dc, s) => DrawCrystal(dc, s, seed), rad, crystalOps[o]);
                    }
                }
            }

            // 2. Hódarabok (Clump): lavinához és söpréshez
            double[] clumpOps = { 1.0, 0.70, 0.40 };
            _clumps = new ImageSource[ClumpVariants, ClumpRotSteps, ClumpOpSteps];
            for (int v = 0; v < ClumpVariants; v++)
            {
                int seed = v;
                for (int r = 0; r < ClumpRotSteps; r++)
                {
                    double rad = (r * 45.0) * Math.PI / 180.0;
                    for (int o = 0; o < ClumpOpSteps; o++)
                    {
                        _clumps[v, r, o] = Render(36, (dc, s) => DrawClump(dc, s, seed), rad, clumpOps[o]);
                    }
                }
            }

            // 3. Bolyhos pelyhek (Puff)
            double[] puffOps = { 1.0, 0.75, 0.50 };
            _puffs = new ImageSource[PuffOpSteps];
            for (int o = 0; o < PuffOpSteps; o++)
            {
                _puffs[o] = Render(36, DrawPuff, 0, puffOps[o]);
            }

            // 4. Előtéri bokeh pelyhek (Bokeh)
            double[] bokehOps = { 0.60, 0.30 };
            _bokehs = new ImageSource[BokehOpSteps];
            for (int o = 0; o < BokehOpSteps; o++)
            {
                _bokehs[o] = Render(56, DrawBokeh, 0, bokehOps[o]);
            }

            // 5. Hópor (Dust)
            double[] dustOps = { 0.85, 0.55, 0.28 };
            _dusts = new ImageSource[DustOpSteps];
            for (int o = 0; o < DustOpSteps; o++)
            {
                _dusts[o] = Render(16, DrawDust, 0, dustOps[o]);
            }
        }

        // ------------------------------------------------------------------ Gyors lookup metódusok (O(1))

        public static ImageSource GetCrystal(int variant, double rotRad, double opacity)
        {
            int v = (variant % CrystalVariants + CrystalVariants) % CrystalVariants;
            // 6-ágú szimmetria (60 fok = PI / 3)
            double a = rotRad % (Math.PI / 3.0);
            if (a < 0) a += Math.PI / 3.0;
            int r = Math.Clamp((int)Math.Round(a / (Math.PI / 3.0) * (CrystalRotSteps - 1)), 0, CrystalRotSteps - 1);
            int o = opacity > 0.82 ? 0 : (opacity > 0.60 ? 1 : 2);
            return _crystals[v, r, o];
        }

        public static ImageSource GetClump(int variant, double rotRad, double opacity)
        {
            int v = (variant % ClumpVariants + ClumpVariants) % ClumpVariants;
            double a = rotRad % (Math.PI * 2.0);
            if (a < 0) a += Math.PI * 2.0;
            int r = Math.Clamp((int)Math.Round(a / (Math.PI * 2.0) * (ClumpRotSteps - 1)), 0, ClumpRotSteps - 1);
            int o = opacity > 0.78 ? 0 : (opacity > 0.48 ? 1 : 2);
            return _clumps[v, r, o];
        }

        public static ImageSource GetPuff(double opacity)
        {
            int o = opacity > 0.82 ? 0 : (opacity > 0.60 ? 1 : 2);
            return _puffs[o];
        }

        public static ImageSource GetBokeh(double opacity)
        {
            int o = opacity > 0.42 ? 0 : 1;
            return _bokehs[o];
        }

        public static ImageSource GetDust(double opacity)
        {
            int o = opacity > 0.65 ? 0 : (opacity > 0.38 ? 1 : 2);
            return _dusts[o];
        }

        // ------------------------------------------------------------------ Renderelés és formák

        private static ImageSource Render(int size, Action<DrawingContext, double> draw, double rotAngleRad = 0, double opacity = 1.0)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                if (opacity < 0.99) dc.PushOpacity(opacity);
                if (Math.Abs(rotAngleRad) > 0.001)
                {
                    var m = Matrix.Identity;
                    m.RotateAt(rotAngleRad * 180.0 / Math.PI, size / 2.0, size / 2.0);
                    dc.PushTransform(new MatrixTransform(m));
                }

                draw(dc, size);

                if (Math.Abs(rotAngleRad) > 0.001) dc.Pop();
                if (opacity < 0.99) dc.Pop();
            }

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        private static void DrawCrystal(DrawingContext dc, double s, int seed)
        {
            var rnd = new Random(1000 + seed * 37);
            double c = s / 2.0;
            double radius = s * 0.43;

            int branches = 2 + rnd.Next(2);
            var pos = new double[branches];
            var len = new double[branches];
            for (int i = 0; i < branches; i++)
            {
                pos[i] = 0.28 + i * (0.5 / branches) + rnd.NextDouble() * 0.1;
                len[i] = radius * (0.18 + rnd.NextDouble() * 0.22) * (1.15 - pos[i] * 0.5);
            }

            double branchAngle = Math.PI / 180.0 * (42 + rnd.NextDouble() * 20);
            bool hexRing = rnd.NextDouble() < 0.45;
            bool tipDots = rnd.NextDouble() < 0.4;
            bool forkTip = rnd.NextDouble() < 0.5;

            for (int pass = 0; pass < 2; pass++)
            {
                var brush = new SolidColorBrush(pass == 0
                    ? Color.FromArgb(60, 200, 228, 255)
                    : Color.FromArgb(250, 255, 255, 255));
                var pen = new Pen(brush, pass == 0 ? s * 0.13 : s * 0.045)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };

                for (int arm = 0; arm < 6; arm++)
                {
                    double a = arm * Math.PI / 3.0;
                    double ca = Math.Cos(a), sa = Math.Sin(a);
                    var tip = new Point(c + ca * radius, c + sa * radius);
                    dc.DrawLine(pen, new Point(c, c), tip);

                    for (int b = 0; b < branches; b++)
                    {
                        double bx = c + ca * radius * pos[b];
                        double by = c + sa * radius * pos[b];
                        for (int sign = -1; sign <= 1; sign += 2)
                        {
                            double ba = a + sign * branchAngle;
                            dc.DrawLine(pen, new Point(bx, by),
                                new Point(bx + Math.Cos(ba) * len[b], by + Math.Sin(ba) * len[b]));
                        }
                    }

                    if (forkTip)
                    {
                        double fx = c + ca * radius * 0.88;
                        double fy = c + sa * radius * 0.88;
                        for (int sign = -1; sign <= 1; sign += 2)
                        {
                            double fa = a + sign * 0.6;
                            dc.DrawLine(pen, new Point(fx, fy),
                                new Point(fx + Math.Cos(fa) * radius * 0.17, fy + Math.Sin(fa) * radius * 0.17));
                        }
                    }

                    if (tipDots)
                    {
                        dc.DrawEllipse(brush, null, tip, s * 0.035, s * 0.035);
                    }
                }

                if (hexRing)
                {
                    var geo = new StreamGeometry();
                    using (var ctx = geo.Open())
                    {
                        double rr = radius * 0.3;
                        ctx.BeginFigure(new Point(c + rr, c), false, true);
                        for (int k = 1; k < 6; k++)
                        {
                            double ang = k * Math.PI / 3.0;
                            ctx.LineTo(new Point(c + Math.Cos(ang) * rr, c + Math.Sin(ang) * rr), true, false);
                        }
                    }
                    geo.Freeze();
                    dc.DrawGeometry(null, pen, geo);
                }
            }

            dc.DrawEllipse(Brushes.White, null, new Point(c, c), s * 0.05, s * 0.05);
        }

        private static void DrawPuff(DrawingContext dc, double s)
        {
            var b = new RadialGradientBrush();
            b.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(235, 250, 253, 255), 0.38));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(105, 238, 247, 255), 0.72));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 232, 244, 255), 1.0));
            dc.DrawEllipse(b, null, new Point(s / 2, s / 2), s / 2, s / 2);
        }

        private static void DrawBokeh(DrawingContext dc, double s)
        {
            var b = new RadialGradientBrush();
            b.GradientStops.Add(new GradientStop(Color.FromArgb(40, 255, 255, 255), 0.0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(70, 245, 250, 255), 0.55));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(150, 240, 248, 255), 0.85));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(80, 235, 245, 255), 0.95));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 235, 245, 255), 1.0));
            dc.DrawEllipse(b, null, new Point(s / 2, s / 2), s / 2, s / 2);
        }

        private static void DrawDust(DrawingContext dc, double s)
        {
            var b = new RadialGradientBrush();
            b.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(190, 250, 253, 255), 0.45));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 240, 248, 255), 1.0));
            dc.DrawEllipse(b, null, new Point(s / 2, s / 2), s / 2, s / 2);
        }

        private static void DrawClump(DrawingContext dc, double s, int seed)
        {
            var rnd = new Random(seed * 99 + 5);
            double c = s / 2.0;

            var shade = new RadialGradientBrush();
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(120, 196, 214, 236), 0.0));
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 196, 214, 236), 1.0));

            var lit = new RadialGradientBrush { GradientOrigin = new Point(0.4, 0.35) };
            lit.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.0));
            lit.GradientStops.Add(new GradientStop(Color.FromArgb(245, 250, 253, 255), 0.55));
            lit.GradientStops.Add(new GradientStop(Color.FromArgb(0, 228, 240, 252), 1.0));

            int blobs = 5 + rnd.Next(2);
            var cx = new double[blobs];
            var cy = new double[blobs];
            var cr = new double[blobs];
            for (int i = 0; i < blobs; i++)
            {
                cx[i] = c + (rnd.NextDouble() - 0.5) * s * 0.34;
                cy[i] = c + (rnd.NextDouble() - 0.5) * s * 0.34;
                cr[i] = s * (0.17 + rnd.NextDouble() * 0.11);
            }
            for (int i = 0; i < blobs; i++)
            {
                dc.DrawEllipse(shade, null, new Point(cx[i], cy[i] + s * 0.03), cr[i], cr[i]);
            }
            for (int i = 0; i < blobs; i++)
            {
                dc.DrawEllipse(lit, null, new Point(cx[i], cy[i]), cr[i], cr[i]);
            }
        }
    }
}
