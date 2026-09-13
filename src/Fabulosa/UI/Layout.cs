using SkiaSharp;

namespace Fabulosa.UI
{
    /// <summary>Design-space geometry (1672 x 941). The top row and footer follow the reference render; the middle rows hold the dancer and scopes.</summary>
    public static class Layout
    {
        public const float Width = 1672f;
        public const float Height = 941f;

        public static readonly SKRect Window = new SKRect(0, 0, Width, Height);
        public static readonly SKRect Content = new SKRect(12, 12, 1660, 929);
        public static readonly SKRect TitleBar = new SKRect(12, 12, 1660, 49);
        public static readonly SKRect MenuBar = new SKRect(12, 53, 1660, 91);

        public static readonly SKRect BtnMinimize = new SKRect(1549, 18, 1576, 43);
        public static readonly SKRect BtnMaximize = new SKRect(1588, 18, 1615, 43);
        public static readonly SKRect BtnClose = new SKRect(1627, 18, 1655, 43);

        public static readonly SKRect NowPlaying = new SKRect(16, 100, 318, 374);
        public static readonly SKRect Spectrum = new SKRect(326, 100, 1104, 374);
        public static readonly SKRect Meters = new SKRect(1114, 100, 1656, 374);

        public static readonly SKRect Dancer = new SKRect(16, 382, 350, 850);
        public static readonly SKRect Scope = new SKRect(358, 382, 1656, 590);
        public static readonly SKRect Spectrum3D = new SKRect(358, 598, 1300, 850);
        public static readonly SKRect Phase = new SKRect(1308, 598, 1656, 850);

        public static readonly SKRect BottomBar = new SKRect(16, 858, 1656, 930);
        public static readonly SKRect ShuffleBox = new SKRect(740, 872, 938, 918);
        public static readonly SKRect RepeatBox = new SKRect(955, 872, 1133, 918);
        public static readonly SKRect TickerBox = new SKRect(1148, 872, 1537, 918);
        public static readonly SKRect ClockBox = new SKRect(1552, 872, 1640, 918);

        public static readonly string[] MenuTitles = { "File", "Playback", "Visualisation", "Tools", "Help" };
        public static readonly float[] MenuX = { 30, 90, 187, 326, 397 };

        /// <summary>The inset display surface of a panel: 8 px at the sides and bottom, below the 32 px header.</summary>
        public static SKRect ScreenOf(SKRect panel) => new SKRect(panel.Left + 8, panel.Top + 32, panel.Right - 8, panel.Bottom - 8);
    }
}
