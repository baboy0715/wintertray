using System;
using System.IO;
using System.Text;

namespace WinterTray.Native
{
    /// <summary>
    /// Az overlay ablakot az asztal (háttérkép + ikonok) FÖLÉ, de az összes normál ablak ALÁ teszi a Z-sorrendben.
    /// Így a Windows maga takarja ki a havat minden ablaknál (nagyított, háttérben lévő, animált ablakok is),
    /// a tálca pedig (ami "topmost") mindig a hó fölött marad.
    /// </summary>
    public static class ZOrder
    {
        private static readonly StringBuilder Sb = new(128);

        public static string? LastDiagnostic { get; private set; }

        public static void Enforce(IntPtr overlay)
        {
            if (overlay == IntPtr.Zero) return;

            // Ha az "Asztal megjelenítése" minimalizálná az overlayt, azonnal visszaállítjuk.
            if (Win32.IsIconic(overlay))
            {
                Win32.ShowWindow(overlay, Win32.SW_RESTORE);
            }

            // Képernyőmentés (Snipping Tool / ScreenClippingHost) közben nem toljuk az overlay-t a legtetejére,
            // így a Képmetsző felülete tisztán az overlay felett jelenik meg,
            // miközben a hóréteg benne marad a készülő képernyőfotóban!
            if (Win32.IsScreenCaptureActive())
            {
                return;
            }

            // Normál esetben az overlay Topmost szinten ül, így garantáltan az ablakok felett marad,
            // és semmilyen előtérbe hozott alkalmazás (pl. Fájlkezelő) nem tudja elfedni a saját havát!
            Win32.SetWindowPos(overlay, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        }

        /// <summary>Hibakereséshez: az Z-sorrend elejének kiírása fájlba.</summary>
        public static void DumpZOrder(IntPtr overlay, string file, string label, int max = 30)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"--- {label} ({DateTime.Now:HH:mm:ss}) ---");
                int index = 0;
                var cls = new StringBuilder(128);
                var title = new StringBuilder(128);
                Win32.EnumWindows((h, _) =>
                {
                    if (!Win32.IsWindowVisible(h)) return true;
                    cls.Clear();
                    title.Clear();
                    Win32.GetClassName(h, cls, cls.Capacity);
                    Win32.GetWindowText(h, title, title.Capacity);
                    Win32.GetWindowRect(h, out var r);
                    int ex = Win32.GetWindowLong(h, Win32.GWL_EXSTYLE);
                    sb.AppendLine(
                        $"{index,2}: {h.ToInt64():X} [{cls}] \"{title}\" ({r.Left},{r.Top},{r.Width}x{r.Height}) " +
                        $"{((ex & Win32.WS_EX_TOPMOST) != 0 ? "TOPMOST " : "")}{(h == overlay ? "<== OVERLAY" : "")}");
                    index++;
                    return index < max;
                }, IntPtr.Zero);
                File.AppendAllText(file, sb.ToString());
            }
            catch
            {
                // diagnosztika, nem kritikus
            }
        }
    }
}
