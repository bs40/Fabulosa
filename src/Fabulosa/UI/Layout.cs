using System;
using System.Collections.Generic;
using System.Linq;
using Fabulosa.Model;
using SkiaSharp;

namespace Fabulosa.UI
{
    /// <summary>Design-space geometry (1672 x 941) and the arrangement of the visualisers the user has chosen.</summary>
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

        public static readonly SKRect BottomBar = new SKRect(16, 858, 1656, 930);
        public static readonly SKRect ShuffleBox = new SKRect(740, 872, 938, 918);
        public static readonly SKRect RepeatBox = new SKRect(955, 872, 1133, 918);
        public static readonly SKRect TickerBox = new SKRect(1148, 872, 1537, 918);
        public static readonly SKRect ClockBox = new SKRect(1552, 872, 1640, 918);

        /// <summary>Everything right of Now Playing down to the bottom bar.</summary>
        public static readonly SKRect VisualiserArea = new SKRect(326, 100, 1656, 850);

        public static readonly string[] MenuTitles = { "File", "Playback", "View", "Visualisation", "Tools", "Help" };

        /// <summary>The inset display surface of a panel: 8 px at the sides and bottom, below the 32 px header.</summary>
        public static SKRect ScreenOf(SKRect panel) => new SKRect(panel.Left + 8, panel.Top + 32, panel.Right - 8, panel.Bottom - 8);

        // ---- visualiser arrangement ------------------------------------------------------

        private const float Gap = 8f, LeftEdge = 16f, RightEdge = 1656f, TopEdge = 100f, BottomEdge = 850f;
        private const float BesideNowPlaying = 326f, BesideDancer = 358f, UpperBottom = 374f, LowerTop = 382f;
        private static readonly SKRect DancerColumn = new SKRect(16, 382, 350, 850);

        /// <summary>Rows in order: each has a height weight and its panels with width weights.</summary>
        private static readonly (float Height, (Visualiser Panel, float Width)[] Panels)[] Rows =
        {
            (274f, new[] { (Visualiser.Spectrum, 778f), (Visualiser.LevelMeters, 542f) }),
            (208f, new[] { (Visualiser.Oscilloscope, 950f), (Visualiser.PhaseScope, 340f) }),
            (252f, new[] { (Visualiser.Spectrum3D, 645f), (Visualiser.Radial, 645f) }),
        };

        /// <summary>
        /// Places the visible visualisers. Now Playing keeps the top-left corner and the Dancer the column below
        /// it. The first row with anything in it sits beside Now Playing; later rows share the space below and
        /// reach the left edge when the Dancer is hidden. Rows and panels stretch into whatever hidden ones free up.
        /// </summary>
        public static Dictionary<Visualiser, SKRect> Arrange(Func<Visualiser, bool> isVisible)
        {
            var placed = new Dictionary<Visualiser, SKRect>();
            bool dancer = isVisible(Visualiser.Dancer);
            var rows = Rows
                .Select(r => (r.Height, Panels: r.Panels.Where(p => isVisible(p.Panel)).ToArray()))
                .Where(r => r.Panels.Length > 0)
                .ToList();

            if (rows.Count == 0)
            {
                if (dancer) placed[Visualiser.Dancer] = VisualiserArea;   // on its own the Dancer gets the whole stage
                return placed;
            }
            if (dancer) placed[Visualiser.Dancer] = DancerColumn;

            if (rows.Count == 1)
            {
                PlaceRow(placed, rows[0].Panels, new SKRect(dancer ? BesideDancer : BesideNowPlaying, TopEdge, RightEdge, BottomEdge));
                return placed;
            }

            PlaceRow(placed, rows[0].Panels, new SKRect(BesideNowPlaying, TopEdge, RightEdge, UpperBottom));
            float left = dancer ? BesideDancer : LeftEdge;
            float weights = rows.Skip(1).Sum(r => r.Height);
            float space = BottomEdge - LowerTop - Gap * (rows.Count - 2);
            float y = LowerTop;
            for (int i = 1; i < rows.Count; i++)
            {
                float h = i == rows.Count - 1 ? BottomEdge - y : MathF.Round(space * rows[i].Height / weights);
                PlaceRow(placed, rows[i].Panels, new SKRect(left, y, RightEdge, y + h));
                y += h + Gap;
            }
            return placed;
        }

        private static void PlaceRow(Dictionary<Visualiser, SKRect> placed, (Visualiser Panel, float Width)[] panels, SKRect area)
        {
            float weights = panels.Sum(p => p.Width);
            float space = area.Width - Gap * (panels.Length - 1);
            float x = area.Left;
            for (int i = 0; i < panels.Length; i++)
            {
                float w = i == panels.Length - 1 ? area.Right - x : MathF.Round(space * panels[i].Width / weights);
                placed[panels[i].Panel] = new SKRect(x, area.Top, x + w, area.Bottom);
                x += w + Gap;
            }
        }
    }
}
