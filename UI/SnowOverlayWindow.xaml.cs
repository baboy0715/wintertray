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

            ZOrder.Enforce(_hwnd);

            _clock.Start();
            CompositionTarget.Rendering += OnRendering;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            CompositionTarget.Rendering -= OnRendering;
        }

        public void SetPaused(bool paused) => _isPaused = paused;

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

            if (_isPaused || _tracker == null) return;

            _pollAccumulator += dt;
            _zOrderAccumulator += dt;

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
                _world.SetWindows(windows);
                bool wasCovered = _allCovered;
                _allCovered = ComputeAllMonitorsCovered(windows);
                if (wasCovered && !_allCovered)
                {
                    _world.PopulateExposedArea(new Rect(0, 0, ActualWidth, ActualHeight));
                }
                covered = _allCovered && _settings.PauseWhenCovered;
            }

            // ha minden monitort egy ablak teljesen takar, a hó úgysem látszana: nem számolunk, nem rajzolunk
            if (covered) return;

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

        private static bool ComputeAllMonitorsCovered(List<TrackedWindow> windows)
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (screens.Length == 0) return false;

            foreach (var screen in screens)
            {
                var wa = screen.WorkingArea;
                bool covered = false;
                foreach (var w in windows)
                {
                    if (w.IsTaskbar) continue;
                    var r = w.PxRect;
                    if (r.Left <= wa.Left + 8 && r.Top <= wa.Top + 8 &&
                        r.Right >= wa.Right - 8 && r.Bottom >= wa.Bottom - 8)
                    {
                        covered = true;
                        break;
                    }
                }
                if (!covered) return false;
            }
            return true;
        }
    }
}
