using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WinterTray.Engine;
using WinterTray.Native;

namespace WinterTray.UI
{
    public sealed class SnowCanvas : FrameworkElement
    {
        private readonly SnowWorld _world;

        public SnowCanvas(SnowWorld world)
        {
            _world = world;
            IsHitTestVisible = false;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            _world.Render(dc);
        }
    }

    public partial class SnowOverlayWindow : Window
    {
        private readonly Settings _settings;
        private readonly SnowWorld _world;
        private readonly SnowCanvas _canvas;
        private WindowTracker? _tracker;
        private Func<Point, Point>? _toDip;
        private IntPtr _hwnd;

        private readonly Stopwatch _clock = new();
        private double _lastTime;
        private double _pollAccumulator;
        private double _zOrderAccumulator;
        private bool _isPaused;
        private bool _allCovered;

        private Point _prevCursor = new(-1e9, -1e9);
        private double _mouseVx, _mouseVy;
        private bool _prevMouseDown;

        public bool IsPaused => _isPaused;
        public IntPtr Hwnd => _hwnd;

        public SnowOverlayWindow(Settings settings)
        {
            _settings = settings;
            InitializeComponent();

            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            _world = new SnowWorld(settings);
            _world.Resize(Width, Height);
            _canvas = new SnowCanvas(_world);
            RootGrid.Children.Add(_canvas);

            Loaded += OnLoaded;
            Closed += OnClosed;
            StateChanged += (s, e) =>
            {
                if (WindowState != WindowState.Normal)
                {
                    WindowState = WindowState.Normal;
                }
            };
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;

            // átkattintható, nem aktiválódik, nem jelenik meg az Alt+Tab-ban
            int exStyle = Win32.GetWindowLong(_hwnd, Win32.GWL_EXSTYLE);
            Win32.SetWindowLong(_hwnd, Win32.GWL_EXSTYLE,
                exStyle | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_LAYERED);

            _world.Resize(ActualWidth, ActualHeight);

            var dpi = VisualTreeHelper.GetDpi(this);
            double dpiX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
            double dpiY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

            _toDip = screenPt =>
            {
                try { return PointFromScreen(screenPt); }
                catch { return new Point(screenPt.X / dpiX, screenPt.Y / dpiY); }
            };
            _tracker = new WindowTracker(_hwnd, _toDip);
            _tracker.WindowChanged += _world.OnWindowChanged;

            // A hóréteg és hóesés képernyőképeken (Snipping Tool, PrintScreen, stb.) tisztán megjelenik
            Win32.SetWindowDisplayAffinity(_hwnd, Win32.WDA_NONE);

            ZOrder.Enforce(_hwnd);

            _clock.Start();
            CompositionTarget.Rendering += OnRendering;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            CompositionTarget.Rendering -= OnRendering;
        }

        public void SetPaused(bool paused)
        {
            _isPaused = paused;
            if (paused)
            {
                Visibility = Visibility.Hidden;
            }
            else
            {
                _lastTime = _clock.Elapsed.TotalSeconds;
                Visibility = Visibility.Visible;
                _canvas.InvalidateVisual();
            }
        }

        public void ClearSnow()
        {
            _world.ClearAll();
            _canvas.InvalidateVisual();
        }

        public void Reconfigure()
        {
            _world.Reconfigure();
            _canvas.InvalidateVisual();
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            double now = _clock.Elapsed.TotalSeconds;
            double dt = now - _lastTime;
            if (dt <= 0) return;

            if (_settings.TargetFps > 0)
            {
                double interval = 1.0 / _settings.TargetFps;
                if (dt < interval * 0.85) return;
            }
            _lastTime = now;

            if (_isPaused || _tracker == null)
            {
                if (Visibility != Visibility.Hidden) Visibility = Visibility.Hidden;
                return;
            }

            _pollAccumulator += dt;
            _zOrderAccumulator += dt;

            if (_hwnd != IntPtr.Zero && Win32.IsIconic(_hwnd))
            {
                Win32.ShowWindow(_hwnd, Win32.SW_RESTORE);
                ZOrder.Enforce(_hwnd);
            }

            // az overlay Z-sorrendjének fenntartása (asztal fölött, ablakok alatt)
            if (_zOrderAccumulator >= 0.5)
            {
                _zOrderAccumulator = 0;
                ZOrder.Enforce(_hwnd);
            }


            bool covered = _allCovered && _settings.PauseWhenCovered;
            if (_pollAccumulator >= (covered ? 0.12 : 0.033))
            {
                var windows = _tracker.Update(_pollAccumulator);
                _pollAccumulator = 0;

                var screens = ComputeScreenInfos(windows);
                bool wasCovered = _allCovered;
                _allCovered = screens.Count > 0 && screens.TrueForAll(s => s.IsCovered);

                _world.SetScreensAndWindows(screens, windows);

                if (wasCovered && !_allCovered)
                {
                    _world.PopulateExposedArea(new Rect(0, 0, ActualWidth, ActualHeight));
                }
                covered = _allCovered && _settings.PauseWhenCovered;
            }

            // Ha minden monitort alkalmazásablakok teljesen takarnak (akár egy maximalizált, akár több egymás melletti ablak),
            // az overlay-t azonnal elrejtjük (Visibility.Hidden).
            // Így garantáltan semmilyen hópehely nem látszódik a teljes képernyős/kétablakos felületek felett/mögött,
            // és a GPU / CPU használat is teljesen leáll (0% terhelés)!
            if (covered)
            {
                if (Visibility != Visibility.Hidden) Visibility = Visibility.Hidden;
                return;
            }
            else
            {
                if (Visibility != Visibility.Visible) Visibility = Visibility.Visible;
            }

            double simDt = Math.Min(dt, 0.05);

            // 120 FPS valós idejű ablakpozíció-szinkronizáció (nulla késleltetés ablakmozgatásnál!)
            _world.SyncWindowPositions(_toDip, simDt);

            _world.Update(simDt, ReadCursor(simDt));
            _canvas.InvalidateVisual();
        }

        private CursorState ReadCursor(double dt)
        {
            var state = new CursorState();
            if (!Win32.GetCursorPos(out var pt)) return state;

            Point c;
            try { c = PointFromScreen(new Point(pt.X, pt.Y)); }
            catch
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                double dpiX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
                double dpiY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;
                c = new Point(pt.X / dpiX, pt.Y / dpiY);
            }

            Point prev = _prevCursor.X > -1e8 ? _prevCursor : c;
            if (_prevCursor.X > -1e8 && dt > 0.0001)
            {
                double vx = (c.X - _prevCursor.X) / dt;
                double vy = (c.Y - _prevCursor.Y) / dt;
                double a = 1 - Math.Exp(-16.0 * dt);
                _mouseVx += (vx - _mouseVx) * a;
                _mouseVy += (vy - _mouseVy) * a;
            }
            _prevCursor = c;

            state.Valid = true;
            state.X = c.X;
            state.Y = c.Y;
            state.PrevX = prev.X;
            state.PrevY = prev.Y;
            state.Vx = _mouseVx;
            state.Vy = _mouseVy;
            state.Speed = Math.Sqrt(_mouseVx * _mouseVx + _mouseVy * _mouseVy);

            short lBtn = Win32.GetAsyncKeyState(Win32.VK_LBUTTON);
            bool isDown = (lBtn & 0x8000) != 0;
            if (isDown && !_prevMouseDown)
            {
                _world.HandleMouseClick(c.X, c.Y);
            }
            _prevMouseDown = isDown;

            return state;
        }

        private List<ScreenInfo> ComputeScreenInfos(List<TrackedWindow> windows)
        {
            var list = new List<ScreenInfo>();
            var allScreens = System.Windows.Forms.Screen.AllScreens;
            if (allScreens.Length == 0) return list;

            foreach (var screen in allScreens)
            {
                bool covered = IsScreenCovered(screen, windows);

                Point tlB = _toDip != null ? _toDip(new Point(screen.Bounds.Left, screen.Bounds.Top)) : new Point(screen.Bounds.Left, screen.Bounds.Top);
                Point brB = _toDip != null ? _toDip(new Point(screen.Bounds.Right, screen.Bounds.Bottom)) : new Point(screen.Bounds.Right, screen.Bounds.Bottom);
                Point tlW = _toDip != null ? _toDip(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top)) : new Point(screen.WorkingArea.Left, screen.WorkingArea.Top);
                Point brW = _toDip != null ? _toDip(new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom)) : new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom);

                list.Add(new ScreenInfo
                {
                    Bounds = new Rect(tlB, brB),
                    WorkingArea = new Rect(tlW, brW),
                    IsCovered = covered
                });
            }

            return list;
        }

        /// <summary>
        /// Ellenőrzi, hogy egy monitor munkaterületét teljesen kitakarják-e az ablakok.
        /// Támogatja az egyetlen nagy ablakot, a 2 egymás melletti félbevágott ablakot (Win+Balra/Jobbra),
        /// valamint a 3-4 felé osztott rács-elrendezést is.
        /// </summary>
        internal static bool IsScreenCovered(System.Windows.Forms.Screen screen, List<TrackedWindow> windows)
        {
            var wa = screen.WorkingArea;
            if (wa.Width <= 0 || wa.Height <= 0) return false;

            // 1. Gyors ellenőrzés: egyetlen ablak lefedi-e a teljes képernyőt
            for (int i = 0; i < windows.Count; i++)
            {
                var w = windows[i];
                if (w.IsTaskbar) continue;
                if (string.IsNullOrWhiteSpace(w.Title) &&
                    !w.ClassName.StartsWith("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase) &&
                    !w.ClassName.Equals("ApplicationFrameWindow", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var r = w.PxRect;
                if (r.Left <= wa.Left + 12 && r.Top <= wa.Top + 12 &&
                    r.Right >= wa.Right - 12 && r.Bottom >= wa.Bottom - 12)
                {
                    return true;
                }
            }

            // 2. Több ablakos (pl. 2 egymás melletti ablak, rács) lefedés ellenőrzése GDI régió-kivonással
            const int margin = 12;
            int testLeft = wa.Left + margin;
            int testTop = wa.Top + margin;
            int testRight = wa.Right - margin;
            int testBottom = wa.Bottom - margin;

            if (testRight <= testLeft || testBottom <= testTop) return false;

            IntPtr rgn = Win32.CreateRectRgn(testLeft, testTop, testRight, testBottom);
            if (rgn == IntPtr.Zero) return false;

            try
            {
                for (int i = 0; i < windows.Count; i++)
                {
                    var w = windows[i];
                    if (w.IsTaskbar) continue;
                    if (string.IsNullOrWhiteSpace(w.Title) &&
                        !w.ClassName.StartsWith("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase) &&
                        !w.ClassName.Equals("ApplicationFrameWindow", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var r = w.PxRect;
                    if (r.Right <= testLeft || r.Left >= testRight || r.Bottom <= testTop || r.Top >= testBottom)
                    {
                        continue;
                    }

                    // Ablak-téglalap 6 pixeles kibővítése, hogy az egymás melletti ablakok közti 1-4 px rést/hézagot áthidalja
                    IntPtr winRgn = Win32.CreateRectRgn(r.Left - 6, r.Top - 6, r.Right + 6, r.Bottom + 6);
                    if (winRgn != IntPtr.Zero)
                    {
                        Win32.CombineRgn(rgn, rgn, winRgn, Win32.RGN_DIFF);
                        Win32.DeleteObject(winRgn);
                    }

                    int rgnType = Win32.GetRgnBox(rgn, out var box);
                    if (rgnType == Win32.NULLREGION || box.Width <= 0 || box.Height <= 0)
                    {
                        return true;
                    }
                }

                int finalType = Win32.GetRgnBox(rgn, out var finalBox);
                return finalType == Win32.NULLREGION || (finalBox.Width <= 50 && finalBox.Height <= 50);
            }
            finally
            {
                Win32.DeleteObject(rgn);
            }
        }
    }
}
