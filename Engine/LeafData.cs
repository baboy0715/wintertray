using System;

namespace WinterTray.Engine
{
    public sealed class LeafFlake
    {
        public double X, Y, PrevY;
        /// <summary>Egér vagy légáramlat által okozott lecsengő sebesség (px/s).</summary>
        public double Vx, Vy;
        public double FallVy;
        public double Size;
        public double Opacity;
        public double Depth;
        public double Rot, RotSpeed;
        public double SwayPhase, SwaySpeed, SwayAmp;
        public double FlipPhase, FlipSpeed;
        public int Variant;      // 0 = Juhar (Maple), 1 = Tölgy (Oak), 2 = Nyír (Birch)
        public int ColorVariant; // 0 = Narancs, 1 = Aranybarna, 2 = Vörös, 3 = Alkonyat
    }

    public sealed class LandedLeaf
    {
        public IntPtr HWnd;
        public double RelX;      // az ablak bal széléhez képest
        public double RelY;      // az ablak felső lécéhez képest
        public double Size;
        public double Rot;       // ráfekvési szög az ablak felületén (pontosan a becsapódási szög!)
        public int Variant;
        public int ColorVariant;
        public double Age;
        public double MaxLife = 35.0;
        public double Opacity = 1.0;
    }

    public struct RainDrop
    {
        public double X, Y;
        public double Vy;
        public double Length;
        public double Alpha;
    }

    public struct RainSplash
    {
        public double X, Y;
        public double Age;
        public double MaxLife;
        public double Radius;
    }

    /// <summary>
    /// Ablak felső peremén gyűlő vízréteg (pocsolya / film) és vízhullám dinamika.
    /// </summary>
    public sealed class WindowWaterFilm
    {
        public IntPtr HWnd;
        /// <summary>Vízszint vastagsága képpontban (0.0 - 4.0 px).</summary>
        public double Level;
        /// <summary>Ablakmozgatásból és kéz/kurzor seprésből származó hullámsebesség a lécen (px/s).</summary>
        public double SloshVx;
        /// <summary>Hullám tarajának vizuális kitérése.</summary>
        public double WaveOffset;
        /// <summary>Ablak szélein lefolyó cseppek generálási időzítője.</summary>
        public double SideDripAcc;
    }

    /// <summary>
    /// Az ablak függőleges bal vagy jobb oldalán valósághűen lecsorgó vízcsepp felületi feszültséggel.
    /// </summary>
    public sealed class RunningDrop
    {
        public IntPtr HWnd;
        public bool IsLeftEdge;        // true = bal oldalon folyik le, false = jobb oldalon
        public double OffsetX;         // finom oldalirányú rezgés a szegély mentén
        public double RelY;            // az ablak tetejétől lefelé megtett távolság (0 .. win.Height)
        public double Vy;              // lefolyási pillanatnyi sebesség
        public double Length;          // cseppuszály hossza (px)
        public double Thickness;       // csepp vastagsága (px)
        public double Alpha;           // áttetszőség
        public double HesitateTimer;   // felületi feszültség miatti torpanás/megcsúszás időzítője
    }
}
