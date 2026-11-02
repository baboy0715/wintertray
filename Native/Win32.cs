using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WinterTray.Native
{
    public static class Win32
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_APPWINDOW = 0x00040000;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        public const int DWMWA_CLOAKED = 14;

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;

        public const uint GW_HWNDPREV = 3;
        public const int SW_SHOWNOACTIVATE = 4;

        public const uint SPI_GETDESKWALLPAPER = 0x0073;
        public const uint SPI_SETDESKWALLPAPER = 0x0014;
        public const uint SPIF_UPDATEINIFILE = 0x01;
        public const uint SPIF_SENDCHANGE = 0x02;

        public static readonly IntPtr HWND_TOP = IntPtr.Zero;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public const int VK_LBUTTON = 0x01;

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int SystemParametersInfo(uint uAction, uint uParam, StringBuilder lpvParam, uint fuWinIni);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int SystemParametersInfo(uint uAction, uint uParam, string lpvParam, uint fuWinIni);

        /// <summary>Az ablak látható kerete (árnyék és láthatatlan átméretező szél nélkül), fizikai pixelben.</summary>
        public static bool GetAccurateWindowRect(IntPtr hWnd, out RECT rect)
        {
            int hr = DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>());
            if (hr == 0 && rect.Width > 0 && rect.Height > 0)
            {
                return true;
            }
            return GetWindowRect(hWnd, out rect);
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        /// <summary>
        /// Ellenőrzi, hogy a Windows Képmetsző / Snipping Tool (Win+Shift+S) vagy képernyőkép-készítő aktív-e.
        /// Ilyenkor az overlay azonnal elrejtőzik, így semmilyen hó nem takarja ki a képernyőfotót!
        /// </summary>
        public static bool IsScreenCaptureActive()
        {
            try
            {
                IntPtr h1 = FindWindow("ScreenClippingHost", null);
                if (h1 != IntPtr.Zero && IsWindowVisible(h1)) return true;

                IntPtr h2 = FindWindow("Microsoft.ScreenSketch", null);
                if (h2 != IntPtr.Zero && IsWindowVisible(h2)) return true;

                IntPtr h3 = FindWindow("Windows.Internal.Shell.ScreenClipping", null);
                if (h3 != IntPtr.Zero && IsWindowVisible(h3)) return true;
            }
            catch { }
            return false;
        }

        public static bool IsWindowCloaked(IntPtr hWnd)
        {
            int hr = DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int isCloaked, sizeof(int));
            return hr == 0 && isCloaked != 0;
        }
    }
}
