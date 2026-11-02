using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace WinterTray.Engine
{
    public sealed class IcicleData
    {
        public int Id;
        public bool IsTop;           // true = felső jégcsap (az alkalmazás fejlécébe lóg), false = alsó jégcsap
        public double RelX;          // X az ablak bal szélétől
        public double BaseY;         // Tő Y koordinátája az ablakhoz képest
        public double Width;         // Tő szélessége (3.0 - 6.5 px)
        public double TargetLength;  // Végleges kifejlett hossz
        public double CurrentLength; // Jelenlegi hossz (idővel lassan, természetesen növekszik!)
        public double Length => CurrentLength;
        public double Slant;         // Enyhe aszimmetria
        public int MaxHits;          // Széttöréshez szükséges kattintások (1-3)
        public int HitsRemaining;    // Hátralévő életerő
        public bool Shattered;       // Széttört-e már
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
        public const double EdgeMargin = 2.5;    // 2.5 px: pontosan a Windows 11 kerek sarkának ívére illeszkedik, semmi sem lóg ki a levegőbe!
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
            UpdateBounds(left, top, width, height, null);
        }

        /// <summary>
        /// Ablak határainak frissítése.
        /// - Ha az ablak csak mozog (mérete nem változott), hardveres TranslateTransform-mal frissül azonnal (0 ms lag).
        /// - Ha az ablakot a szélénél fogva átméretezik (kisebbre húzzák), a levágott peremről a hó szépen és
        ///   valósághűen lehullik (Clump és Dust részecskék), a megmaradt felületen lévő hó pedig nem nyomódik össze,
        ///   hanem valós képernyő-koordinátái szerint pontosan megmarad a helyén!
        /// </summary>
        public void UpdateBounds(
            double newLeft, double newTop, double newWidth, double newHeight,
            Action<double, double, float, double>? onShedOffEdge = null)
        {
            if (H.Length == 0)
            {
                Left = newLeft;
                Top = newTop;
                Width = newWidth;
                Height = newHeight;
                _transform.X = newLeft;
                _transform.Y = newTop;

                double activeWidth = Math.Max(Step * 2, newWidth - EdgeMargin * 2.0);
                int n = Math.Max(2, (int)Math.Ceiling(activeWidth / Step) + 1);
                H = new float[n];
                T = new float[n];
                _tmp = new float[n];
                _topPts = new Point[n];
                _basePts = new Point[n];
                _dirty = true;
                return;
            }

            bool widthChanged = Math.Abs(newWidth - Width) > 0.5;
            bool heightChanged = Math.Abs(newHeight - Height) > 0.5;

            // Csak sima ablakmozgatás (szélesség és magasság nem változott): 120 FPS DirectX TranslateTransform
            if (!widthChanged && !heightChanged)
            {
                Left = newLeft;
                Top = newTop;
                _transform.X = newLeft;
                _transform.Y = newTop;
                return;
            }

            // ABLAK ÁTMÉRETEZÉSE (szél vagy sarok húzása)
            double oldLeft = Left;
            double oldTop = Top;
            double oldWidth = Width;
            double oldHeight = Height;
            var oldH = H;
            var oldT = T;
            int oldN = oldH.Length;

            double oldActiveLeft = oldLeft + EdgeMargin;
            double oldActiveRight = oldLeft + oldWidth - EdgeMargin;

            double newActiveLeft = newLeft + EdgeMargin;
            double newActiveRight = newLeft + newWidth - EdgeMargin;
            double newActiveWidth = Math.Max(Step * 2, newWidth - EdgeMargin * 2.0);
            int newN = Math.Max(2, (int)Math.Ceiling(newActiveWidth / Step) + 1);

            // 1. A visszahúzott peremről leeső hó kezelése:
            // Amikor a felhasználó összenyomja / kisebbre méretezi az ablakot, a hó nem csak
            // összemegy, hanem a megvont felületről szépen és fizikailag leomlik/lehullik!
            if (onShedOffEdge != null && oldN > 0)
            {
                for (int i = 0; i < oldN; i++)
                {
                    float h = oldH[i];
                    if (h < 0.25f) continue;

                    double worldX = oldActiveLeft + i * Step;

                    // Jobb szél húzása befelé (jobbról balra):
                    if (worldX > newActiveRight)
                    {
                        onShedOffEdge(worldX, oldTop - h * 0.5, h, 1.0);
                    }
                    // Bal szél húzása befelé (balról jobbra):
                    else if (worldX < newActiveLeft)
                    {
                        onShedOffEdge(worldX, oldTop - h * 0.5, h, -1.0);
                    }
                }

                // Ha az ablak magassága csökkent (alulról húzták fel), a lelógó alsó jégcsapok letörnek
                if (newHeight < oldHeight - 4.0 && Icicles.Count > 0)
                {
                    for (int i = 0; i < Icicles.Count; i++)
                    {
                        var ic = Icicles[i];
                        if (!ic.IsTop && !ic.Shattered)
                        {
                            ic.Shattered = true;
                            onShedOffEdge(oldLeft + ic.RelX, oldTop + oldHeight, (float)ic.Length, 0.0);
                        }
                    }
                }
            }

            // 2. A megmaradt ablakfelületen lévő hó megtartása valós koordinátákban:
            // A hó NEM torzul, nem tolódik el mesterségesen, hanem a képernyő-helyén marad.
            var newH = new float[newN];
            var newT = new float[newN];
            float newPeak = 0;

            for (int j = 0; j < newN; j++)
            {
                double worldX = newActiveLeft + j * Step;

                if (worldX >= oldActiveLeft && worldX <= oldActiveRight && oldN > 1)
                {
                    double t = (worldX - oldActiveLeft) / Step;
                    int i0 = (int)Math.Floor(t);
                    int i1 = Math.Min(oldN - 1, i0 + 1);
                    i0 = Math.Clamp(i0, 0, oldN - 1);
                    double f = Math.Clamp(t - i0, 0.0, 1.0);

                    float hInterp = (float)(oldH[i0] * (1.0 - f) + oldH[i1] * f);
                    float tInterp = (float)(oldT[i0] * (1.0 - f) + oldT[i1] * f);

                    newH[j] = hInterp;
                    newT[j] = tInterp;
                    if (hInterp > newPeak) newPeak = hInterp;
                }
                else
                {
                    newH[j] = 0f;
                    newT[j] = 0f;
                }
            }

            H = newH;
            T = newT;
            Peak = newPeak;
            _tmp = new float[newN];
            _topPts = new Point[newN];
            _basePts = new Point[newN];

            Left = newLeft;
            Top = newTop;
            Width = newWidth;
            Height = newHeight;
            _transform.X = newLeft;
            _transform.Y = newTop;
            _dirty = true;
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

            // Jégcsapok lassú, természetes növekedése az idő előrehaladtával
            if (Icicles.Count > 0)
            {
                bool icicleGrew = false;
                for (int i = 0; i < Icicles.Count; i++)
                {
                    var ic = Icicles[i];
                    if (ic.Shattered) continue;

                    double localSnow = HeightAt(Left + ic.RelX);
                    // Ha van hó felette a peremen, szépen lassan növekszik a jégcsap
                    if (localSnow > 0.5 || Peak > 0.7f)
                    {
                        if (ic.CurrentLength < ic.TargetLength)
                        {
                            double growSpeed = 0.40 + Math.Min(localSnow, 10.0) * 0.05;
                            ic.CurrentLength = Math.Min(ic.TargetLength, ic.CurrentLength + growSpeed * dt);
                            icicleGrew = true;
                        }
                    }
                    else
                    {
                        // Ha nincs hó (pl. lesöpörték), a jégcsap lassan visszahúzódik / elolvad
                        if (ic.CurrentLength > 0.3)
                        {
                            ic.CurrentLength = Math.Max(0.0, ic.CurrentLength - 0.7 * dt);
                            icicleGrew = true;
                        }
                    }
                }
                if (icicleGrew) _dirty = true;
            }
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
                if (ic.Shattered || ic.CurrentLength < 2.0) continue;

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
                    ic.CurrentLength = 0;
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

            double activeLeft = 20.0;
            double activeRight = Width - 20.0;
            double span = activeRight - activeLeft;
            if (span < 40.0) return;

            double baseSpacing = 16.0 / Math.Clamp(density, 0.4, 2.5);

            // ALSÓ JÉGCSAPOK (lelógnak az ablak aljáról a háttérbe)
            // Csak ha az ablak nem ül a tálcán
            if (Bottom < screenHeight - 42)
            {
                double curX = activeLeft + 6.0 + rnd.NextDouble() * 8.0;
                while (curX < activeRight - 10.0)
                {
                    if (rnd.NextDouble() < 0.28)
                    {
                        curX += baseSpacing * (1.2 + rnd.NextDouble() * 1.6);
                        continue;
                    }

                    int group = rnd.Next(1, 4);
                    for (int g = 0; g < group && curX < activeRight - 8.0; g++)
                    {
                        double w = 3.2 + rnd.NextDouble() * 3.4; // 3.2 - 6.6 px széles tő
                        double lenRoll = rnd.NextDouble();
                        double len = lenRoll < 0.45 ? (6.0 + rnd.NextDouble() * 7.0)
                                   : (lenRoll < 0.82 ? (11.0 + rnd.NextDouble() * 9.0)
                                   : (20.0 + rnd.NextDouble() * 10.0));

                        int maxHits = len > 18.0 ? 3 : (len > 10.0 ? 2 : 1);

                        Icicles.Add(new IcicleData
                        {
                            Id = ++_icicleCounter,
                            IsTop = false,
                            RelX = curX,
                            BaseY = Height,
                            Width = w,
                            TargetLength = len,
                            CurrentLength = 0.5,
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

            // 1. Felső hóréteg: a sarkoknál (6px-ben) finoman és természetesen lefut 0-ra
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

            // A Windows 11 lekerekített sarkainál szigorúan 0-ra kényszerítjük a szélső pontokat
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

            // 3. Pontok generálása:
            // Y = 3.0 takarja el a sötét ablakkeretet (a fekete csík 100%-ban megszűnt!).
            // X = activeLeft (2.5px) és X = Width - 2.5px pontosan a Windows 11 lekerekített sarkára illeszkedik,
            // így egyetlen pixel sem lóg ki a levegőbe a sarkoknál, tökéletesen kerek marad az ablak!
            const double YOverlap = 3.0;
            for (int i = 0; i < n; i++)
            {
                double x = Math.Min(activeLeft + i * Step, activeLeft + activeWidth);
                _basePts[i] = new Point(x, YOverlap);
                _topPts[i] = new Point(x, YOverlap - _tmp[i]);
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
                    while (i < n && _tmp[i] <= 0.15f) i++;
                    if (i >= n) break;

                    int spanStart = Math.Max(0, i - 1);
                    while (i < n && _tmp[i] > 0.15f) i++;
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
                    if (ic.Shattered || ic.CurrentLength < 1.0) continue;

                    hasAny = true;
                    double curLen = ic.CurrentLength;
                    double growRatio = Math.Clamp(curLen / Math.Max(3.0, ic.TargetLength), 0.35, 1.0);
                    double px = ic.RelX;
                    double py = ic.BaseY;
                    double hw = ic.Width * 0.5 * growRatio;
                    double tipX = px + ic.Slant * growRatio;
                    double tipY = py + curLen;
                    double midY = py + curLen * 0.45;

                    Point pBaseL = new Point(px - hw, py);
                    Point pMidL = new Point(px - hw * 0.35 + ic.Slant * 0.4 * growRatio, midY);
                    Point pTip = new Point(tipX, tipY);
                    Point pMidR = new Point(px + hw * 0.35 + ic.Slant * 0.4 * growRatio, midY);
                    Point pBaseR = new Point(px + hw, py);

                    fillCtx.BeginFigure(pBaseL, true, true);
                    fillCtx.LineTo(pMidL, true, true);
                    fillCtx.LineTo(pTip, true, true);
                    fillCtx.LineTo(pMidR, true, true);
                    fillCtx.LineTo(pBaseR, true, true);

                    // Belső kristálycsillogás
                    Point pShineTop = new Point(px - hw * 0.4, py + 1.0);
                    Point pShineMid = new Point(px - hw * 0.15 + ic.Slant * 0.3 * growRatio, midY);
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
