using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinterTray.Engine
{
    /// <summary>
    /// Előre generált, hardveresen gyorsított falevél sprite-ok különböző formákkal,
    /// őszi színekkel (narancs, aranybarna, vörös) és 36 lépéses (10 fokonkénti) finom forgással.
    /// Futás közben 120 FPS mellett is NULLA mátrix-allokáció és NULLA GC szemét keletkezik!
    /// </summary>
    public static class LeafSprites
    {
        public const int Variants = 3;    // 0 = Juhar (Maple), 1 = Tölgy (Oak), 2 = Nyír (Birch)
        public const int Colors = 4;      // 0 = Narancs, 1 = Aranybarna, 2 = Vörös, 3 = Alkonyat
        public const int RotSteps = 36;   // 360 fok / 36 = 10 fokonként (szuper sima forgás)

        private static ImageSource[,,] _leafCache = null!;
        private static bool _initialized;

        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;

            _leafCache = new ImageSource[Variants, Colors, RotSteps];

            for (int v = 0; v < Variants; v++)
            {
                for (int c = 0; c < Colors; c++)
                {
                    for (int r = 0; r < RotSteps; r++)
                    {
                        double rad = (r * 10.0) * Math.PI / 180.0;
                        int varIdx = v;
                        int colIdx = c;
                        _leafCache[v, c, r] = Render(46, (dc, s) => DrawLeaf(dc, s, varIdx, colIdx), rad);
                    }
                }
            }
        }

        public static ImageSource GetLeaf(int variant, int color, double rotRad)
        {
            int v = (variant % Variants + Variants) % Variants;
            int c = (color % Colors + Colors) % Colors;
            double a = rotRad % (Math.PI * 2.0);
            if (a < 0) a += Math.PI * 2.0;
            int r = Math.Clamp((int)Math.Round(a / (Math.PI * 2.0) * (RotSteps - 1)), 0, RotSteps - 1);
            return _leafCache[v, c, r];
        }

        public static ImageSource GetLeaf(int variant, int color, double rotRad, double opacity) => GetLeaf(variant, color, rotRad);

        private static ImageSource Render(int size, Action<DrawingContext, double> draw, double rotAngleRad = 0)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                if (Math.Abs(rotAngleRad) > 0.001)
                {
                    var m = Matrix.Identity;
                    m.RotateAt(rotAngleRad * 180.0 / Math.PI, size / 2.0, size / 2.0);
                    dc.PushTransform(new MatrixTransform(m));
                }

                draw(dc, size);

                if (Math.Abs(rotAngleRad) > 0.001) dc.Pop();
            }

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        private static void GetColorPalette(int colorIdx, out Brush fill, out Pen veinPen, out Pen outlinePen)
        {
            Color topCol, botCol, veinCol, outCol;
            switch (colorIdx)
            {
                case 0: // Narancs / Amber
                    topCol = Color.FromRgb(255, 152, 0);
                    botCol = Color.FromRgb(216, 67, 21);
                    veinCol = Color.FromArgb(200, 150, 30, 0);
                    outCol = Color.FromArgb(140, 255, 183, 77);
                    break;
                case 1: // Aranybarna / Ochre
                    topCol = Color.FromRgb(225, 160, 45);
                    botCol = Color.FromRgb(125, 80, 45);
                    veinCol = Color.FromArgb(210, 70, 40, 20);
                    outCol = Color.FromArgb(140, 255, 215, 90);
                    break;
                case 2: // Mélyvörös / Crimson
                    topCol = Color.FromRgb(220, 40, 40);
                    botCol = Color.FromRgb(130, 10, 20);
                    veinCol = Color.FromArgb(210, 80, 5, 15);
                    outCol = Color.FromArgb(140, 255, 100, 100);
                    break;
                default: // 3: Alkonyati vörös-arany átmenet
                    topCol = Color.FromRgb(255, 110, 25);
                    botCol = Color.FromRgb(140, 20, 80);
                    veinCol = Color.FromArgb(210, 90, 10, 40);
                    outCol = Color.FromArgb(140, 255, 170, 120);
                    break;
            }

            var grad = new LinearGradientBrush(topCol, botCol, new Point(0.5, 0.0), new Point(0.5, 1.0));
            grad.Freeze();
            fill = grad;

            var vPen = new Pen(new SolidColorBrush(veinCol), 1.0)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            vPen.Freeze();
            veinPen = vPen;

            var oPen = new Pen(new SolidColorBrush(outCol), 0.75)
            {
                LineJoin = PenLineJoin.Round
            };
            oPen.Freeze();
            outlinePen = oPen;
        }

        private static void DrawLeaf(DrawingContext dc, double s, int variant, int colorIdx)
        {
            GetColorPalette(colorIdx, out var fill, out var veinPen, out var outlinePen);

            double cx = s / 2.0;
            double cy = s / 2.0;

            switch (variant)
            {
                case 0:
                    DrawMapleLeaf(dc, cx, cy, s * 0.42, fill, veinPen, outlinePen);
                    break;
                case 1:
                    DrawOakLeaf(dc, cx, cy, s * 0.44, fill, veinPen, outlinePen);
                    break;
                default:
                    DrawBirchLeaf(dc, cx, cy, s * 0.42, fill, veinPen, outlinePen);
                    break;
            }
        }

        /// <summary>5-ágú jellegzetes őszi juharlevél.</summary>
        private static void DrawMapleLeaf(DrawingContext dc, double cx, double cy, double r, Brush fill, Pen veinPen, Pen outlinePen)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(cx, cy + r * 0.95), true, true);

                // Bal alsó karéj
                ctx.LineTo(new Point(cx - r * 0.28, cy + r * 0.65), true, false);
                ctx.LineTo(new Point(cx - r * 0.70, cy + r * 0.45), true, false);
                ctx.LineTo(new Point(cx - r * 0.50, cy + r * 0.25), true, false);

                // Bal oldali főkaréj
                ctx.LineTo(new Point(cx - r * 0.88, cy + r * 0.05), true, false);
                ctx.LineTo(new Point(cx - r * 0.82, cy - r * 0.22), true, false);
                ctx.LineTo(new Point(cx - r * 0.48, cy - r * 0.18), true, false);
                ctx.LineTo(new Point(cx - r * 0.58, cy - r * 0.48), true, false);
                ctx.LineTo(new Point(cx - r * 0.32, cy - r * 0.40), true, false);

                // Középső felső főkaréj
                ctx.LineTo(new Point(cx - r * 0.25, cy - r * 0.72), true, false);
                ctx.LineTo(new Point(cx, cy - r * 0.98), true, false);
                ctx.LineTo(new Point(cx + r * 0.25, cy - r * 0.72), true, false);

                // Jobb oldali főkaréj
                ctx.LineTo(new Point(cx + r * 0.32, cy - r * 0.40), true, false);
                ctx.LineTo(new Point(cx + r * 0.58, cy - r * 0.48), true, false);
                ctx.LineTo(new Point(cx + r * 0.48, cy - r * 0.18), true, false);
                ctx.LineTo(new Point(cx + r * 0.82, cy - r * 0.22), true, false);
                ctx.LineTo(new Point(cx + r * 0.88, cy + r * 0.05), true, false);

                // Jobb alsó karéj
                ctx.LineTo(new Point(cx + r * 0.50, cy + r * 0.25), true, false);
                ctx.LineTo(new Point(cx + r * 0.70, cy + r * 0.45), true, false);
                ctx.LineTo(new Point(cx + r * 0.28, cy + r * 0.65), true, false);
            }
            geo.Freeze();

            dc.DrawGeometry(fill, outlinePen, geo);

            // Erezet
            dc.DrawLine(veinPen, new Point(cx, cy + r * 0.95), new Point(cx, cy - r * 0.85));
            dc.DrawLine(veinPen, new Point(cx, cy + r * 0.40), new Point(cx - r * 0.68, cy - r * 0.12));
            dc.DrawLine(veinPen, new Point(cx, cy + r * 0.40), new Point(cx + r * 0.68, cy - r * 0.12));
            dc.DrawLine(veinPen, new Point(cx, cy + r * 0.55), new Point(cx - r * 0.52, cy + r * 0.38));
            dc.DrawLine(veinPen, new Point(cx, cy + r * 0.55), new Point(cx + r * 0.52, cy + r * 0.38));
        }

        /// <summary>Hullámos karéjú klasszikus őszi tölgyfalevél.</summary>
        private static void DrawOakLeaf(DrawingContext dc, double cx, double cy, double r, Brush fill, Pen veinPen, Pen outlinePen)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(cx, cy + r * 0.95), true, true);

                // Bal oldal karéjai (lentről felfelé)
                ctx.BezierTo(new Point(cx - r * 0.25, cy + r * 0.78), new Point(cx - r * 0.45, cy + r * 0.65), new Point(cx - r * 0.35, cy + r * 0.48), true, false);
                ctx.BezierTo(new Point(cx - r * 0.55, cy + r * 0.38), new Point(cx - r * 0.62, cy + r * 0.15), new Point(cx - r * 0.42, cy + r * 0.05), true, false);
                ctx.BezierTo(new Point(cx - r * 0.60, cy - r * 0.15), new Point(cx - r * 0.55, cy - r * 0.45), new Point(cx - r * 0.32, cy - r * 0.55), true, false);
                ctx.BezierTo(new Point(cx - r * 0.38, cy - r * 0.75), new Point(cx - r * 0.18, cy - r * 0.95), new Point(cx, cy - r * 0.98), true, false);

                // Jobb oldal karéjai (fentről lefelé)
                ctx.BezierTo(new Point(cx + r * 0.18, cy - r * 0.95), new Point(cx + r * 0.38, cy - r * 0.75), new Point(cx + r * 0.32, cy - r * 0.55), true, false);
                ctx.BezierTo(new Point(cx + r * 0.55, cy - r * 0.45), new Point(cx + r * 0.60, cy - r * 0.15), new Point(cx + r * 0.42, cy + r * 0.05), true, false);
                ctx.BezierTo(new Point(cx + r * 0.62, cy + r * 0.15), new Point(cx + r * 0.55, cy + r * 0.38), new Point(cx + r * 0.35, cy + r * 0.48), true, false);
                ctx.BezierTo(new Point(cx + r * 0.45, cy + r * 0.65), new Point(cx + r * 0.25, cy + r * 0.78), new Point(cx, cy + r * 0.95), true, false);
            }
            geo.Freeze();

            dc.DrawGeometry(fill, outlinePen, geo);

            // Tölgyfalevél erezet
            dc.DrawLine(veinPen, new Point(cx, cy + r * 0.95), new Point(cx, cy - r * 0.88));
            dc.DrawLine(veinPen, new Point(cx, cy + r * 0.50), new Point(cx - r * 0.32, cy + r * 0.46));
            dc.DrawLine(veinPen, new Point(cx, cy + r * 0.50), new Point(cx + r * 0.32, cy + r * 0.46));
            dc.DrawLine(veinPen, new Point(cx, cy + r * 0.10), new Point(cx - r * 0.40, cy + r * 0.05));
            dc.DrawLine(veinPen, new Point(cx, cy + r * 0.10), new Point(cx + r * 0.40, cy + r * 0.05));
            dc.DrawLine(veinPen, new Point(cx, cy - r * 0.30), new Point(cx - r * 0.36, cy - r * 0.42));
            dc.DrawLine(veinPen, new Point(cx, cy - r * 0.30), new Point(cx + r * 0.36, cy - r * 0.42));
        }

        /// <summary>Elegáns szív-ovális alakú nyírfalevél.</summary>
        private static void DrawBirchLeaf(DrawingContext dc, double cx, double cy, double r, Brush fill, Pen veinPen, Pen outlinePen)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(cx, cy + r * 0.92), true, true);

                // Bal oldal fűrészes íve
                ctx.BezierTo(new Point(cx - r * 0.42, cy + r * 0.78), new Point(cx - r * 0.65, cy + r * 0.42), new Point(cx - r * 0.55, cy + r * 0.10), true, false);
                ctx.BezierTo(new Point(cx - r * 0.48, cy - r * 0.30), new Point(cx - r * 0.28, cy - r * 0.72), new Point(cx, cy - r * 0.98), true, false);

                // Jobb oldal fűrészes íve
                ctx.BezierTo(new Point(cx + r * 0.28, cy - r * 0.72), new Point(cx + r * 0.48, cy - r * 0.30), new Point(cx + r * 0.55, cy + r * 0.10), true, false);
                ctx.BezierTo(new Point(cx + r * 0.65, cy + r * 0.42), new Point(cx + r * 0.42, cy + r * 0.78), new Point(cx, cy + r * 0.92), true, false);
            }
            geo.Freeze();

            dc.DrawGeometry(fill, outlinePen, geo);

            // Nyírfalevél finom erezet
            dc.DrawLine(veinPen, new Point(cx, cy + r * 0.92), new Point(cx, cy - r * 0.90));
            for (int i = 0; i < 4; i++)
            {
                double yBase = cy + r * (0.6 - i * 0.35);
                double yTip = yBase - r * 0.18;
                double xSpread = r * (0.45 - i * 0.08);
                dc.DrawLine(veinPen, new Point(cx, yBase), new Point(cx - xSpread, yTip));
                dc.DrawLine(veinPen, new Point(cx, yBase), new Point(cx + xSpread, yTip));
            }
        }
    }
}
