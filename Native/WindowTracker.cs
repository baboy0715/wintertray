using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace WinterTray.Native
{
    public sealed class TrackedWindow
    {
        public IntPtr HWnd { get; init; }
        public string Title { get; init; } = "";
        public string ClassName { get; init; } = "";
        /// <summary>Az ablak helye az overlay DIP koordinátáiban.</summary>
        public Rect Rect { get; init; }
        /// <summary>Az ablak helye fizikai pixelben.</summary>
        public Win32.RECT PxRect { get; init; }
        public bool IsTaskbar { get; init; }
    }

    public sealed class WindowTracker
    {
        private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Progman", "WorkerW", "Windows.UI.Core.CoreWindow",
            "XamlExplorerHostIslandWindow", "DesktopWindowXamlSource", "Shell_LightDismissOverlay",
            "ScreenClippingHost", "ScreenClippingHostWindow", "SnippingTool"
        };

        private readonly IntPtr _overlay;
        private readonly Func<Point, Point> _toDip;
        private readonly StringBuilder _sb = new(256);
        private readonly Dictionary<IntPtr, Rect> _known = new();
        private readonly List<TrackedWindow> _list = new(32);
        private readonly HashSet<IntPtr> _seen = new(32);
        private readonly List<IntPtr> _gone = new(8);

        /// <summary>(ablak, régi hely, új hely vagy null ha megszűnt/minimalizálódott, eltelt idő mp-ben)</summary>
        public event Action<IntPtr, Rect, Rect?, double>? WindowChanged;

        public WindowTracker(IntPtr overlayHwnd, Func<Point, Point> screenToDip)
        {
            _overlay = overlayHwnd;
            _toDip = screenToDip;
        }

        public List<TrackedWindow> Update(double dt)
        {
            _list.Clear();
            _seen.Clear();

            Win32.EnumWindows((hWnd, _) =>
            {
                if (hWnd == _overlay) return true;
                if (!Win32.IsWindowVisible(hWnd) || Win32.IsIconic(hWnd)) return true;
                if (Win32.IsWindowCloaked(hWnd)) return true;

                _sb.Clear();
                Win32.GetClassName(hWnd, _sb, _sb.Capacity);
                string cls = _sb.ToString();
                if (IgnoredClasses.Contains(cls)) return true;

                bool taskbar = cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd";
                if (!taskbar)
                {
                    int ex = Win32.GetWindowLong(hWnd, Win32.GWL_EXSTYLE);
                    // átkattintható, átlátszó segédablakok nem számítanak ablaknak
                    if ((ex & Win32.WS_EX_TRANSPARENT) != 0 && (ex & Win32.WS_EX_LAYERED) != 0) return true;
                    // eszközablakok sem
                    if ((ex & Win32.WS_EX_TOOLWINDOW) != 0 && (ex & Win32.WS_EX_APPWINDOW) == 0) return true;
                }

                if (!Win32.GetAccurateWindowRect(hWnd, out var px)) return true;
                if (taskbar ? (px.Width < 100 || px.Height < 20) : (px.Width < 100 || px.Height < 40)) return true;

                _sb.Clear();
                Win32.GetWindowText(hWnd, _sb, _sb.Capacity);
                string title = _sb.ToString();

                Point tl = _toDip(new Point(px.Left, px.Top));
                Point br = _toDip(new Point(px.Right, px.Bottom));
                var rect = new Rect(tl, br);

                _list.Add(new TrackedWindow
                {
                    HWnd = hWnd,
                    Title = title,
                    ClassName = cls,
                    Rect = rect,
                    PxRect = px,
                    IsTaskbar = taskbar
                });
                _seen.Add(hWnd);

                if (_known.TryGetValue(hWnd, out var old))
                {
                    if (Moved(old, rect))
                    {
                        _known[hWnd] = rect;
                        WindowChanged?.Invoke(hWnd, old, rect, dt);
                    }
                }
                else
                {
                    _known[hWnd] = rect;
                }
                return true;
            }, IntPtr.Zero);

            _gone.Clear();
            foreach (var key in _known.Keys)
            {
                if (!_seen.Contains(key))
                {
                    _gone.Add(key);
                }
            }
            if (_gone.Count > 0)
            {
                for (int i = 0; i < _gone.Count; i++)
                {
                    var h = _gone[i];
                    var old = _known[h];
                    _known.Remove(h);
                    WindowChanged?.Invoke(h, old, null, dt);
                }
            }

            return _list;
        }

        private static bool Moved(Rect a, Rect b)
        {
            return Math.Abs(a.X - b.X) > 3 ||
                   Math.Abs(a.Y - b.Y) > 3 ||
                   Math.Abs(a.Width - b.Width) > 3;
        }
    }
}
