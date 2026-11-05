using System;

namespace WinterTray.Engine
{
    public enum FlakeKind
    {
        Dust,     // apró, távoli porhó
        Crystal,  // részletes, forgó jégkristály
        Puff,     // lágy, bolyhos pehely
        Bokeh     // nagy, életlen (előtér) pehely a mélységélességhez
    }

    public sealed class SnowFlake
    {
        public double X, Y, PrevY;
        /// <summary>Az egér/szél által okozott, lecsengő sebesség (px/s).</summary>
        public double Dvx, Dvy;
        public double FallVy;
        public double Size;
        public double Opacity;
        public double Depth;
        public double Rot, RotSpeed;
        public double SwayPhase, SwaySpeed, SwayAmp;
        public FlakeKind Kind;
        public int Variant;
    }

    public enum ParticleKind
    {
        Clump,   // leeső hódarab
        Dust,    // hópor
        Settle   // leérkező pehely, ami beleolvad a halomba
    }

    public struct Particle
    {
        public IntPtr SourceHwnd;
        public double X, Y, Vx, Vy;
        public double Size;
        public double Rot, RotSpeed;
        public double Age, Life;
        public ParticleKind Kind;
        public int Variant;
    }

    public struct CursorState
    {
        public bool Valid;
        public double X, Y;
        public double PrevX, PrevY;
        public double Vx, Vy;
        public double Speed;
    }
}
