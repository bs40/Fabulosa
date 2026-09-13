using System;
using SkiaSharp;
using Windows.Media;

namespace Fabulosa.UI.Views
{
    /// <summary>Footer strip: brand stripes, shuffle / repeat, status display and clock.</summary>
    public sealed class BottomBarView : View
    {
        private int _pressed = -1;
        public string Status;
        public float StatusUntil;

        public BottomBarView() : base(Layout.BottomBar) { }

        public override void DrawStatic(SKCanvas c, FrameContext f)
        {
            Theme.Panel(c, Bounds);
            using (var div = Theme.Fill(new SKColor(0x3c, 0x40, 0x3c))) c.DrawRect(new SKRect(300, 866, 301, 922), div);

            // brand stripes
            SKColor[] stripes = { new SKColor(0xff, 0x2d, 0x8a), new SKColor(0xff, 0xd2, 0x3c), new SKColor(0x33, 0xc4, 0xff) };
            for (int i = 0; i < 3; i++)
            {
                using var path = new SKPath();
                float x = 46 + i * 11;
                path.MoveTo(x + 7, 883); path.LineTo(x + 14, 883); path.LineTo(x + 7, 905); path.LineTo(x, 905); path.Close();
                using var p = Theme.Fill(stripes[i]);
                c.DrawPath(path, p);
            }

            foreach (var box in new[] { Layout.ShuffleBox, Layout.RepeatBox, Layout.TickerBox, Layout.ClockBox })
                Theme.Screen(c, box, new SKColor(0x08, 0x10, 0x11), 3f);

            Theme.Text(c, "SHUFFLE", 822f, 900f, 12.5f, new SKColor(0xd8, 0xd8, 0xd8), Theme.Sans, SKTextAlign.Left, 1.5f);
            Theme.Text(c, "REPEAT", 1032f, 900f, 12.5f, new SKColor(0xd8, 0xd8, 0xd8), Theme.Sans, SKTextAlign.Left, 1.5f);
        }

        public override void Draw(SKCanvas c, FrameContext f)
        {
            var snap = f.Media?.Snapshot;
            bool shuffle = snap != null && snap.HasSession && snap.ShuffleActive;
            bool repeat = snap != null && snap.HasSession && snap.RepeatMode != MediaPlaybackAutoRepeatMode.None;
            bool repeatOne = repeat && snap.RepeatMode == MediaPlaybackAutoRepeatMode.Track;

            DrawHoverBox(c, Layout.ShuffleBox, f, _pressed == 0);
            DrawHoverBox(c, Layout.RepeatBox, f, _pressed == 1);
            DrawShuffleIcon(c, 782, 895, shuffle ? Theme.CyanText : new SKColor(0xd0, 0xd0, 0xd0));
            DrawRepeatIcon(c, 1004, 895, repeat ? Theme.CyanText : new SKColor(0xd0, 0xd0, 0xd0), repeatOne);

            string clock = DateTime.Now.ToString("HH:mm");
            Theme.Text(c, clock, Layout.ClockBox.MidX, 901f, 16f, new SKColor(0xdc, 0xdc, 0xdc), Theme.Sans, SKTextAlign.Center, 2.2f);

            // transient status messages live in the display box
            if (!string.IsNullOrEmpty(Status) && f.Time < StatusUntil)
                Theme.Text(c, Status, Layout.TickerBox.MidX, 900f, 12.5f, new SKColor(0xf0, 0xe8, 0xc0), Theme.Sans, SKTextAlign.Center, 1f);
        }

        private static void DrawHoverBox(SKCanvas c, SKRect box, FrameContext f, bool pressed)
        {
            if (!f.Hover(box) && !pressed) return;
            using var hl = Theme.Fill(new SKColor(0x1a, 0x2a, 0x2c, pressed ? (byte)200 : (byte)120));
            c.DrawRoundRect(box, 3, 3, hl);
        }

        private static void DrawShuffleIcon(SKCanvas c, float x, float y, SKColor col)
        {
            using var p = Theme.Stroke(col, 1.8f);
            p.StrokeCap = SKStrokeCap.Round; p.StrokeJoin = SKStrokeJoin.Round;
            using var path = new SKPath();
            path.MoveTo(x - 12, y - 6); path.LineTo(x - 6, y - 6); path.CubicTo(x - 1, y - 6, x + 1, y + 6, x + 6, y + 6); path.LineTo(x + 11, y + 6);
            path.MoveTo(x - 12, y + 6); path.LineTo(x - 6, y + 6); path.CubicTo(x - 1, y + 6, x + 1, y - 6, x + 6, y - 6); path.LineTo(x + 11, y - 6);
            c.DrawPath(path, p);
            using var head = Theme.Fill(col);
            using var tri = new SKPath();
            tri.MoveTo(x + 9, y - 9.5f); tri.LineTo(x + 14, y - 6); tri.LineTo(x + 9, y - 2.5f); tri.Close();
            tri.MoveTo(x + 9, y + 2.5f); tri.LineTo(x + 14, y + 6); tri.LineTo(x + 9, y + 9.5f); tri.Close();
            c.DrawPath(tri, head);
        }

        private static void DrawRepeatIcon(SKCanvas c, float x, float y, SKColor col, bool one)
        {
            using var p = Theme.Stroke(col, 1.8f);
            p.StrokeCap = SKStrokeCap.Round;
            var oval = new SKRect(x - 11, y - 7, x + 11, y + 7);
            c.DrawArc(oval, 200f, 140f, false, p);
            c.DrawArc(oval, 20f, 140f, false, p);
            using var head = Theme.Fill(col);
            using var tri = new SKPath();
            tri.MoveTo(x + 6, y - 11); tri.LineTo(x + 11.5f, y - 7); tri.LineTo(x + 6, y - 3); tri.Close();
            tri.MoveTo(x - 6, y + 11); tri.LineTo(x - 11.5f, y + 7); tri.LineTo(x - 6, y + 3); tri.Close();
            c.DrawPath(tri, head);
            if (one) Theme.Text(c, "1", x, y + 3.5f, 9f, col, Theme.SansBold, SKTextAlign.Center);
        }

        public override bool OnMouseDown(SKPoint p, FrameContext f)
        {
            if (Layout.ShuffleBox.Contains(p)) { _pressed = 0; f.Media?.ToggleShuffle(); return true; }
            if (Layout.RepeatBox.Contains(p)) { _pressed = 1; f.Media?.CycleRepeat(); return true; }
            return false;
        }

        public override void OnMouseUp(SKPoint p, FrameContext f) { _pressed = -1; }
    }
}
