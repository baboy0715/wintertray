using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace WinterTray.Engine
{
    public sealed class IcicleData
    {
        public int Id;
        public bool IsTop;        // true = felső jégcsap (az alkalmazás fejlécébe lóg), false = alsó jégcsap
        public double RelX;       // X az ablak bal szélétől
        public double BaseY;      // Tő Y koordinátája az ablakhoz képest
        public double Width;      // Tő szélessége (3.0 - 6.5 px)
        public double Length;     // Hossza lefelé
        public double Slant;      // Enyhe aszimmetria
        public int MaxHits;       // Széttöréshez szükséges kattintások (1-3)
        public int HitsRemaining; // Hátralévő életerő
        public bool Shattered;    // Széttört-e már
    }

    /// <summary>
    /// Egy ablak téli díszítése:
    /// - Tetején természetes, organikus hóréteg (a sarkoknál tökéletesen lekopva)
    /// - Felső kis jégcsapok, amelyek belelógnak az alkalmazás fejlécébe
    /// - Alsó látványos jégcsapok, amelyek lelógnak a háttérbe
    /// - Kattintással széttörhető jégcsapok szilánkokra robbanó animációval
    /// - Vízcsepp-források
    /// - Null-overhead DirectX TranslateTransform, így ablakmozgatáskor nulla késleltetés és nulla akadás!
    /// </summary>
    public sealed class SnowBank
    {
        public const double Step = 4.0;
        public const double EdgeMargin = 8.0;    // 8 px védőtávolság a Windows 11 lekerekített sarkaihoz (tiszta sarkok!)
        private const float Slope = 2.6f;

        public IntPtr HWnd;
        public double Left, Top, Width, Height;
        public double Bottom => Top + Height;
        public bool IsTaskbar;

        public double Vx, Vy;
        public double PrevVx, PrevVy;

        public int LastPxLeft = int.MinValue;
        public int LastPxTop = int.MinValue;
        public int LastPxWidth = int.MinValue;
        public int LastPxHeight = int.MinValue;

        public float[] H = Array.Empty<float>();
        public float[] T = Array.Empty<float>();
        public float Peak;

        private float[] _tmp = Array.Empty<float>();
        private Point[] _topPts = Array.Empty<Point>();
        private Point[] _basePts = Array.Empty<Point>();

        // Geometriák lokális koordinátákban (0, 0-tól Width, Height-ig)
        private Geometry? _fill;
        private Geometry? _edge;
        private Geometry? _icicleFill;
        private Geometry? _icicleHighlight;

        public readonly List<IcicleData> Icicles = new(48);
        private int _lastIcicleWidth = -1;
        private double _lastIcicleDensity = -1;
        private int _icicleCounter = 0;

        private bool _dirty = true;
        private readonly TranslateTransform _transform = new();

        public int Count => H.Length;

        public void MarkDirty() => _dirty = true;

        public void SetRect(double left, double top, double width, double height)
        {
            Left = left;
            Top = top;
            _transform.X = left;
            _transform.Y = top;

            if (H.Length == 0 || Math.Abs(width - Width) > 0.5 || Math.Abs(height - Height) > 0.5)
            {
                Width = width;
                Height = height;

                double activeWidth = Math.Max(Step * 2, width - EdgeMargin * 2.0);
                int n = Math.Max(2, (int)Math.Ceiling(activeWidth / Step) + 1);
                if (n != H.Length)
                {
                    Array.Resize(ref H, n);
                    Array.Resize(ref T, n);
                    _tmp = new float[n];
                    _topPts = new Point[n];
                    _basePts = new Point[n];
                }
                _dirty = true;
            }
        }

        public double HeightAt(double x)
        {
            double activeLeft = Left + EdgeMargin;
            double t = (x - activeLeft) / Step;
            int i = (int)Math.Floor(t);
            if (i < 0 || i >= H.Length - 1) return 0;
            double f = t - i;
            return H[i] * (1 - f) + H[i + 1] * f;
        }

        public void Add(double x, double volume, double maxHeight)
        {
            double activeLeft = Left + EdgeMargin;
            int c = (int)Math.Round((x - activeLeft) / Step);
            const int span = 5;
            const double sigma = 2.4;

            double norm = 0;
            for (int k = -span; k <= span; k++)
            {
                norm += Math.Exp(-(k * k) / (2 * sigma * sigma));
            }

            for (int k = -span; k <= span; k++)
            {
                int i = c + k;
                if (i < 0 || i >= T.Length) continue;
                double w = Math.Exp(-(k * k) / (2 * sigma * sigma));
                T[i] = (float)Math.Min(maxHeight, T[i] + volume * w / (norm * Step));
            }
        }

        public void Clear()
        {
            Array.Clear(H, 0, H.Length);
            Array.Clear(T, 0, T.Length);
            Peak = 0;
            _dirty = true;
        }

        /// <summary>Gyorsulás vagy nagy sebesség miatti hólepergés.</summary>
        public void Shed(double ratio, Action<double, double, float> onShed)
        {
            if (ratio <= 0.001 || Peak < 0.4f) return;
            int n = H.Length;
            double activeLeft = Left + EdgeMargin;

            for (int i = 0; i < n; i++)
            {
                float h = H[i];
                if (h < 0.4f) continue;
                float shed = (float)(h * ratio);
                H[i] -= shed;
                T[i] = Math.Max(0f, T[i] - shed);

                if (shed > 0.15f && i % 2 == 0)
                {
                    double px = activeLeft + i * Step;
                    onShed(px, Top - h * 0.5, shed);
                }
            }
            MarkDirty();
        }

        public void Tick(double dt, double maxHeight, double meltPerSecond)
        {
            int n = H.Length;
            if (n == 0) return;

            float cap = (float)maxHeight;
            float melt = (float)(meltPerSecond * dt);

            for (int i = 0; i < n; i++)
            {
                float t = T[i];
                if (melt > 0) t = Math.Max(0f, t - melt);
                if (t > cap) t = cap;
                T[i] = t;
            }

            // lejtő kiegyenlítés
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < n - 1; i++)
                {
                    float diff = T[i] - T[i + 1];
                    if (diff > Slope)
                    {
                        float m = (diff - Slope) * 0.5f;
                        T[i] -= m;
                        T[i + 1] += m;
                    }
                    else if (diff < -Slope)
                    {
                        float m = (-diff - Slope) * 0.5f;
                        T[i] += m;
                        T[i + 1] += m;
                    }
                }
            }

            double k = 1 - Math.Exp(-7.0 * dt);
            float maxDelta = 0;
            float peak = 0;
            for (int i = 0; i < n; i++)
            {
                float d = T[i] - H[i];
                H[i] += (float)(d * k);
                float ad = Math.Abs(d);
                if (ad > maxDelta) maxDelta = ad;
                if (H[i] > peak) peak = H[i];
            }
            Peak = peak;
            if (maxDelta > 0.02f) _dirty = true;
        }

        // ------------------------------------------------------------------ JÉGCSAPOK KATTINTÁSA ÉS CSEPPENÉSE

        /// <summary>
        /// Megvizsgálja, hogy a felhasználó rákattintott-e egy jégcsapra.
        /// Kisebb jégcsap 1 ütésre törik, nagyobbhoz 2-3 ütés kell.
        /// </summary>
        public bool TryHitIcicle(double screenX, double screenY, out bool shattered, out double hitX, out double hitY, out double icicleLen)
        {
            shattered = false;
            hitX = 0;
            hitY = 0;
            icicleLen = 0;

            if (Icicles.Count == 0) return false;

            double localX = screenX - Left;
            double localY = screenY - Top;

            for (int i = 0; i < Icicles.Count; i++)
            {
                var ic = Icicles[i];
                if (ic.Shattered) continue;

                double rootX = ic.RelX;
                double rootY = ic.BaseY;
                double hw = ic.Width * 0.6 + 3.0; // kicsit megbocsátóbb kattintási hitbox

                if (localX < rootX - hw || localX > rootX + hw) continue;

                if (localY < rootY - 2.0 || localY > rootY + ic.Length + 4.0) continue;

                // Találat!
                ic.HitsRemaining--;
                hitX = Left + rootX + ic.Slant * 0.5;
                hitY = Top + rootY + ic.Length * 0.5;
                icicleLen = ic.Length;

                if (ic.HitsRemaining <= 0)
                {
                    ic.Shattered = true;
                    shattered = true;
                }

                _dirty = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Véletlenszerűen kiválaszt egy létező jégcsapot vízcsepp forrásként.
        /// </summary>
        public bool TryGetDropletSource(Random rnd, out double dropX, out double dropY, out bool fromTop, out double targetY)
        {
            dropX = 0;
            dropY = 0;
            fromTop = false;
            targetY = 0;

            if (Icicles.Count == 0) return false;

            int attempts = Math.Min(6, Icicles.Count);
            for (int a = 0; a < attempts; a++)
            {
                int idx = rnd.Next(Icicles.Count);
                var ic = Icicles[idx];
                if (ic.Shattered || ic.Length < 4.0) continue;

                dropX = Left + ic.RelX + ic.Slant;
                dropY = Top + ic.BaseY + ic.Length;
                fromTop = ic.IsTop;
                targetY = ic.IsTop ? Bottom : double.MaxValue;
                return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ RAJZOLÁS ÉS GEOMETRIA

        public void Render(
            DrawingContext dc,
            Brush fill,
            Pen edge,
            Pen halo,
            Brush icicleFill,
            Pen icicleHighlight,
            Pen icicleHalo,
            bool showIcicles,
            double icicleDensity,
            double screenHeight)
        {
            if (_dirty || _fill == null)
            {
                Build(showIcicles, icicleDensity, screenHeight);
            }

            _transform.X = Left;
            _transform.Y = Top;
            dc.PushTransform(_transform);

            // 1. Jégcsapok rajzolása (alsó és felső)
            if (showIcicles && _icicleFill != null)
            {
                if (icicleHalo != null) dc.DrawGeometry(null, icicleHalo, _icicleFill);
                dc.DrawGeometry(icicleFill, null, _icicleFill);
                if (_icicleHighlight != null && icicleHighlight != null)
                {
                    dc.DrawGeometry(null, icicleHighlight, _icicleHighlight);
                }
            }

            // 2. Felső hóréteg rajzolása
            if (Peak >= 0.15f && _fill != null)
            {
                double alpha = Math.Clamp((Peak - 0.15) / 1.4, 0.0, 1.0);
                bool pushOpacity = alpha < 0.96;
                if (pushOpacity) dc.PushOpacity(alpha);

                if (_edge != null) dc.DrawGeometry(null, halo, _edge);
                dc.DrawGeometry(fill, null, _fill);
                if (_edge != null) dc.DrawGeometry(null, edge, _edge);

                if (pushOpacity) dc.Pop();
            }

            dc.Pop();
        }

        private void EnsureIcicles(bool showIcicles, double density, double screenHeight)
        {
            if (!showIcicles || IsTaskbar || Width < 120 || Height < 50)
            {
                Icicles.Clear();
                return;
            }

            int wInt = (int)Math.Round(Width);
            if (wInt == _lastIcicleWidth && Math.Abs(density - _lastIcicleDensity) < 0.05 && Icicles.Count > 0)
            {
                return;
            }

            _lastIcicleWidth = wInt;
            _lastIcicleDensity = density;
            Icicles.Clear();

            int seed = unchecked((int)HWnd.ToInt64() ^ 0x4B3A1C);
            var rnd = new Random(seed);

            double activeLeft = 16.0;
            double activeRight = Width - 16.0;
            double span = activeRight - activeLeft;
            if (span < 30.0) return;

            double baseSpacing = 14.0 / Math.Clamp(density, 0.4, 2.5);

            // A) FELSŐ JÉGCSAPOK (belelógnak a fejlécbe a hóréteg alól)
            double curX = activeLeft + 3.0 + rnd.NextDouble() * 6.0;
            while (curX < activeRight - 6.0)
            {
                if (rnd.NextDouble() < 0.32)
                {
                    curX += baseSpacing * (1.2 + rnd.NextDouble() * 1.5);
                    continue;
                }

                int group = rnd.Next(1, 3);
                for (int g = 0; g < group && curX < activeRight - 5.0; g++)
                {
                    double w = 2.8 + rnd.NextDouble() * 2.8;
                    double len = 4.0 + rnd.NextDouble() * 8.5; // 4 - 12.5 px-t lóg be a címsorba
                    if (rnd.NextDouble() < 0.20) len += 4.0;   // néha 16.5 px

                    int maxHits = len > 11.0 ? 2 : 1;

                    Icicles.Add(new IcicleData
                    {
                        Id = ++_icicleCounter,
                        IsTop = true,
                        RelX = curX,
                        BaseY = 0,
                        Width = w,
                        Length = len,
                        Slant = (rnd.NextDouble() - 0.5) * 0.8,
                        MaxHits = maxHits,
                        HitsRemaining = maxHits,
                        Shattered = false
                    });

                    curX += w + 2.0 + rnd.NextDouble() * 4.0;
                }

                curX += baseSpacing * (0.8 + rnd.NextDouble() * 1.0);
            }

            // B) ALSÓ JÉGCSAPOK (lelógnak az ablak aljáról a háttérbe)
            // Csak ha az ablak nem ül a tálcán
            if (Bottom < screenHeight - 42)
            {
                curX = activeLeft + 4.0 + rnd.NextDouble() * 8.0;
                while (curX < activeRight - 6.0)
                {
                    if (rnd.NextDouble() < 0.24)
                    {
                        curX += baseSpacing * (1.2 + rnd.NextDouble() * 1.6);
                        continue;
                    }

                    int group = rnd.Next(1, 4);
                    for (int g = 0; g < group && curX < activeRight - 5.0; g++)
                    {
                        double w = 3.4 + rnd.NextDouble() * 3.2; // 3.4 - 6.6 px széles tő
                        double lenRoll = rnd.NextDouble();
                        double len = lenRoll < 0.45 ? (5.0 + rnd.NextDouble() * 6.0)
                                   : (lenRoll < 0.82 ? (10.0 + rnd.NextDouble() * 8.0)
                                   : (18.0 + rnd.NextDouble() * 9.0));

                        int maxHits = len > 18.0 ? 3 : (len > 10.0 ? 2 : 1);

                        Icicles.Add(new IcicleData
                        {
                            Id = ++_icicleCounter,
                            IsTop = false,
                            RelX = curX,
                            BaseY = Height,
                            Width = w,
                            Length = len,
                            Slant = (rnd.NextDouble() - 0.5) * 1.0,
                            MaxHits = maxHits,
                            HitsRemaining = maxHits,
                            Shattered = false
                        });

                        curX += w + 2.0 + rnd.NextDouble() * 5.0;
                    }

                    curX += baseSpacing * (0.7 + rnd.NextDouble() * 1.1);
                }
            }
        }

        private void Build(bool showIcicles, double icicleDensity, double screenHeight)
        {
            int n = H.Length;
            if (n < 2)
            {
                _fill = null;
                _edge = null;
                _icicleFill = null;
                _icicleHighlight = null;
                _dirty = false;
                return;
            }

            double activeLeft = EdgeMargin;
            double activeWidth = Math.Max(Step * 2, Width - EdgeMargin * 2.0);

            if (_topPts.Length < n)
            {
                _topPts = new Point[n];
                _basePts = new Point[n];
            }

            // 1. Felső hóréteg: az ablak vízszintes peremén fut végig, a sarkok előtt (6px-ben) finoman lefut nullára
            const double TaperDist = 6.0;
            for (int i = 0; i < n; i++)
            {
                double x = Math.Min(activeLeft + i * Step, activeLeft + activeWidth);
                double dLeft = x - activeLeft;
                double dRight = (activeLeft + activeWidth) - x;
                double d = Math.Min(dLeft, dRight);
                double taper = 1.0;
                if (d < TaperDist)
                {
                    double u = Math.Clamp(d / TaperDist, 0.0, 1.0);
                    taper = u * u * (3.0 - 2.0 * u);
                }

                _tmp[i] = (float)(H[i] * taper);
            }

            // 2. Simítás a természetes, lágy hótakaróért
            for (int pass = 0; pass < 2; pass++)
            {
                float prev = _tmp[0];
                for (int i = 0; i < n; i++)
                {
                    float cur = _tmp[i];
                    float next = i < n - 1 ? _tmp[i + 1] : cur;
                    _tmp[i] = 0.25f * prev + 0.5f * cur + 0.25f * next;
                    prev = cur;
                }
            }

            // A sarkok garantált tisztasága: a széleken szigorúan 0-ra kényszerítjük
            for (int i = 0; i < n; i++)
            {
                double x = Math.Min(activeLeft + i * Step, activeLeft + activeWidth);
                double dLeft = x - activeLeft;
                double dRight = (activeLeft + activeWidth) - x;
                double d = Math.Min(dLeft, dRight);
                if (d < TaperDist)
                {
                    double u = Math.Clamp(d / TaperDist, 0.0, 1.0);
                    _tmp[i] *= (float)(u * u * (3.0 - 2.0 * u));
                }
            }
            _tmp[0] = 0f;
            _tmp[n - 1] = 0f;

            // 3. Pontok generálása: az alapvonal szigorúan Y = 0 (az ablak felső éle), a hó Y <= 0 (fölötte ül)
            // A lekerekített sarok íve alá/mögé semmi sem lóg be, a Windows 11 kerek sarkai 100%-ban tiszták!
            for (int i = 0; i < n; i++)
            {
                double x = Math.Min(activeLeft + i * Step, activeLeft + activeWidth);
                _basePts[i] = new Point(x, 0);
                _topPts[i] = new Point(x, -_tmp[i]);
            }

            // 3. Felső hógeometria építése
            var fill = new StreamGeometry();
            var edge = new StreamGeometry();

            bool hasMounds = false;
            using (var fillCtx = fill.Open())
            using (var edgeCtx = edge.Open())
            {
                int i = 0;
                while (i < n)
                {
                    while (i < n && _tmp[i] <= 0.04f) i++;
                    if (i >= n) break;

                    int spanStart = Math.Max(0, i - 1);
                    while (i < n && _tmp[i] > 0.04f) i++;
                    int spanEnd = Math.Min(n - 1, i);

                    if (spanEnd <= spanStart) continue;

                    hasMounds = true;

                    Point firstTop = _topPts[spanStart];
                    fillCtx.BeginFigure(firstTop, true, true);
                    edgeCtx.BeginFigure(firstTop, false, false);

                    for (int k = spanStart + 1; k <= spanEnd; k++)
                    {
                        fillCtx.LineTo(_topPts[k], true, true);
                        edgeCtx.LineTo(_topPts[k], true, true);
                    }

                    for (int k = spanEnd; k >= spanStart; k--)
                    {
                        fillCtx.LineTo(_basePts[k], true, false);
                    }
                }
            }

            if (hasMounds)
            {
                fill.Freeze();
                edge.Freeze();
                _fill = fill;
                _edge = edge;
            }
            else
            {
                _fill = null;
                _edge = null;
            }

            // 4. Jégcsapok építése
            if (showIcicles)
            {
                EnsureIcicles(showIcicles, icicleDensity, screenHeight);
                BuildIcicleGeometry();
            }
            else
            {
                _icicleFill = null;
                _icicleHighlight = null;
            }

            _dirty = false;
        }

        private void BuildIcicleGeometry()
        {
            if (Icicles.Count == 0)
            {
                _icicleFill = null;
                _icicleHighlight = null;
                return;
            }

            var fillGeom = new StreamGeometry();
            var shineGeom = new StreamGeometry();

            bool hasAny = false;
            using (var fillCtx = fillGeom.Open())
            using (var shineCtx = shineGeom.Open())
            {
                for (int i = 0; i < Icicles.Count; i++)
                {
                    var ic = Icicles[i];
                    if (ic.Shattered) continue;

                    hasAny = true;
                    double px = ic.RelX;
                    double py = ic.BaseY;
                    double hw = ic.Width * 0.5;
                    double tipX = px + ic.Slant;
                    double tipY = py + (ic.IsTop ? ic.Length : ic.Length);
                    double midY = py + (ic.IsTop ? ic.Length * 0.45 : ic.Length * 0.45);

                    Point pBaseL = new Point(px - hw, py);
                    Point pMidL = new Point(px - hw * 0.35 + ic.Slant * 0.4, midY);
                    Point pTip = new Point(tipX, tipY);
                    Point pMidR = new Point(px + hw * 0.35 + ic.Slant * 0.4, midY);
                    Point pBaseR = new Point(px + hw, py);

                    fillCtx.BeginFigure(pBaseL, true, true);
                    fillCtx.LineTo(pMidL, true, true);
                    fillCtx.LineTo(pTip, true, true);
                    fillCtx.LineTo(pMidR, true, true);
                    fillCtx.LineTo(pBaseR, true, true);

                    // Belső kristálycsillogás
                    Point pShineTop = new Point(px - hw * 0.4, py + 1.0);
                    Point pShineMid = new Point(px - hw * 0.15 + ic.Slant * 0.3, midY);
                    Point pShineTip = new Point(tipX - 0.2, tipY - 1.2);

                    shineCtx.BeginFigure(pShineTop, false, false);
                    shineCtx.LineTo(pShineMid, true, true);
                    shineCtx.LineTo(pShineTip, true, true);
                }
            }

            if (hasAny)
            {
                fillGeom.Freeze();
                shineGeom.Freeze();
                _icicleFill = fillGeom;
                _icicleHighlight = shineGeom;
            }
            else
            {
                _icicleFill = null;
                _icicleHighlight = null;
            }
        }
    }
}
