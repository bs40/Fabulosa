using System;
using Fabulosa.Model;
using SkiaSharp;

namespace Fabulosa.UI.Views
{
    /// <summary>17-band graphic equaliser with draggable caps and the response curve threaded through them.</summary>
    public sealed class EqualizerView : View
    {
        private const float SlotTop = 641f, SlotBottom = 809f, ZeroY = 725f, PxPerDb = 140f / 24f;
        private const float FirstX = 310f, Pitch = 45.7f;
        private const float CapW = 27f, CapH = 30f;
        private int _drag = -1;
        private float _dragOffset;

        public EqualizerView() : base(Layout.Equalizer) { }

        private static float SliderX(int i) => FirstX + i * Pitch;
        private static float YForGain(float g) => ZeroY - g * PxPerDb;

        public override void DrawStatic(SKCanvas c, FrameContext f)
        {
            Theme.Panel(c, Bounds, "GRAPHIC EQUALISER");
            var inset = new SKRect(258, 632, 1158, 842);
            using (var bg = Theme.VerticalGradient(inset, new SKColor(0x0a, 0x0f, 0x10), new SKColor(0x06, 0x0a, 0x0b))) c.DrawRoundRect(inset, 4, 4, bg);
            using (var edge = Theme.Stroke(new SKColor(0x1e, 0x26, 0x27), 1f)) c.DrawRoundRect(inset, 4, 4, edge);

            // faint dotted level lines across the panel
            using (var dots = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = new SKColor(0x2a, 0x33, 0x33, 120), PathEffect = SKPathEffect.CreateDash(new[] { 2f, 3f }, 0) })
                for (int g = -12; g <= 12; g += 3)
                {
                    float y = MathF.Round(YForGain(g)) + 0.5f;
                    c.DrawLine(292, y, 1150, y, dots);
                }

            for (int i = 0; i < AppState.EqBandCount; i++)
            {
                float x = SliderX(i);
                var slot = new SKRect(x - 6, SlotTop, x + 6, SlotBottom);
                using (var sf = Theme.Fill(new SKColor(0x03, 0x05, 0x05))) c.DrawRoundRect(slot, 5, 5, sf);
                c.Save();
                c.ClipRoundRect(new SKRoundRect(slot, 5, 5), SKClipOperation.Intersect, true);
                using (var inner = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4f, Color = new SKColor(0, 0, 0, 200), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2f) })
                    c.DrawRoundRect(SKRect.Inflate(slot, 1, 1), 5, 5, inner);
                c.Restore();
                using (var se = Theme.Stroke(new SKColor(0x28, 0x30, 0x31), 1f)) c.DrawRoundRect(slot, 5, 5, se);

                // tick marks either side of the slot
                using (var tick = Theme.Stroke(new SKColor(0x46, 0x52, 0x50), 1f))
                    for (int g = -12; g <= 12; g += 3)
                    {
                        float y = MathF.Round(YForGain(g)) + 0.5f;
                        float len = g % 6 == 0 ? 6f : 3.5f;
                        c.DrawLine(x - 9 - len, y, x - 9, y, tick);
                        c.DrawLine(x + 9, y, x + 9 + len, y, tick);
                    }
                Theme.Text(c, AppState.EqLabels[i], x, 833f, 12.5f, new SKColor(0xd0, 0xd0, 0xd0), Theme.Sans, SKTextAlign.Center);
            }

            foreach (int g in new[] { 12, 6, 0, -6, -12 })
                Theme.Text(c, g > 0 ? "+" + g : g.ToString(), 276f, YForGain(g) + 4.5f, 12.5f, new SKColor(0xd0, 0xd0, 0xd0), Theme.SansBold, SKTextAlign.Right);
        }

        public override void Draw(SKCanvas c, FrameContext f)
        {
            var gains = f.State.EqGains;
            int n = AppState.EqBandCount;

            // response curve (Catmull-Rom through the cap centres)
            using (var path = new SKPath())
            {
                var pts = new SKPoint[n];
                for (int i = 0; i < n; i++) pts[i] = new SKPoint(SliderX(i), YForGain(gains[i]));
                path.MoveTo(pts[0]);
                for (int i = 0; i < n - 1; i++)
                {
                    var p0 = pts[Math.Max(i - 1, 0)];
                    var p1 = pts[i];
                    var p2 = pts[i + 1];
                    var p3 = pts[Math.Min(i + 2, n - 1)];
                    var c1 = new SKPoint(p1.X + (p2.X - p0.X) / 6f, p1.Y + (p2.Y - p0.Y) / 6f);
                    var c2 = new SKPoint(p2.X - (p3.X - p1.X) / 6f, p2.Y - (p3.Y - p1.Y) / 6f);
                    path.CubicTo(c1, c2, p2);
                }
                using (var glow = Theme.GlowStroke(Theme.Red, 5f, 4f, 110)) c.DrawPath(path, glow);
                using (var line = Theme.Stroke(new SKColor(0xff, 0x3b, 0x2b), 2f)) { line.StrokeJoin = SKStrokeJoin.Round; c.DrawPath(path, line); }
            }

            for (int i = 0; i < n; i++)
            {
                float x = SliderX(i), y = YForGain(gains[i]);
                var cap = new SKRect(x - CapW / 2, y - CapH / 2, x + CapW / 2, y + CapH / 2);
                bool hover = _drag == i || f.Hover(SKRect.Inflate(cap, 2, 2));
                using (var shadow = Theme.Glow(SKColors.Black, 2.5f, 150)) c.DrawRoundRect(SKRect.Create(cap.Left, cap.Top + 2, cap.Width, cap.Height), 3, 3, shadow);
                using (var body = Theme.VerticalGradient(cap, hover ? new SKColor(0x8c, 0x8c, 0x8c) : new SKColor(0x7a, 0x7a, 0x7a), new SKColor(0x4a, 0x4a, 0x4a), new SKColor(0x2c, 0x2c, 0x2c)))
                    c.DrawRoundRect(cap, 3, 3, body);
                using (var edge = Theme.Stroke(new SKColor(0x12, 0x12, 0x12), 1f)) c.DrawRoundRect(cap, 3, 3, edge);
                using (var hi = Theme.Stroke(new SKColor(0xa4, 0xa4, 0xa4, 180), 1f)) c.DrawLine(cap.Left + 3, cap.Top + 1.5f, cap.Right - 3, cap.Top + 1.5f, hi);
                using (var grip = Theme.Stroke(new SKColor(0x26, 0x26, 0x26, 200), 1f))
                {
                    c.DrawLine(cap.Left + 5, cap.Top + 7, cap.Right - 5, cap.Top + 7, grip);
                    c.DrawLine(cap.Left + 5, cap.Bottom - 7, cap.Right - 5, cap.Bottom - 7, grip);
                }
                var led = new SKRect(x - 10, y - 1.8f, x + 10, y + 1.8f);
                using (var glow = Theme.Glow(Theme.Red, 3f, 170)) c.DrawRoundRect(SKRect.Inflate(led, 1, 1), 2, 2, glow);
                using (var lp = Theme.Fill(new SKColor(0xff, 0x46, 0x36))) c.DrawRoundRect(led, 1.5f, 1.5f, lp);
                using (var core = Theme.Fill(new SKColor(0xff, 0xd8, 0xc8, 200))) c.DrawRoundRect(new SKRect(x - 8, y - 0.6f, x + 8, y + 0.6f), 1, 1, core);
            }
        }

        private static int HitCap(SKPoint p, float[] gains)
        {
            for (int i = 0; i < AppState.EqBandCount; i++)
            {
                float x = SliderX(i), y = YForGain(gains[i]);
                if (new SKRect(x - CapW / 2 - 3, y - CapH / 2 - 3, x + CapW / 2 + 3, y + CapH / 2 + 3).Contains(p)) return i;
            }
            return -1;
        }

        private static int HitSlot(SKPoint p)
        {
            if (p.Y < SlotTop - 6 || p.Y > SlotBottom + 6) return -1;
            for (int i = 0; i < AppState.EqBandCount; i++)
                if (MathF.Abs(p.X - SliderX(i)) <= Pitch / 2) return i;
            return -1;
        }

        public override bool OnMouseDown(SKPoint p, FrameContext f)
        {
            int cap = HitCap(p, f.State.EqGains);
            if (cap >= 0)
            {
                _drag = cap;
                _dragOffset = YForGain(f.State.EqGains[cap]) - p.Y;
                return true;
            }
            int slot = HitSlot(p);
            if (slot >= 0)
            {
                _drag = slot;
                _dragOffset = 0;
                SetFromY(slot, p.Y, f);
                return true;
            }
            return false;
        }

        private static void SetFromY(int i, float y, FrameContext f)
        {
            float g = (ZeroY - y) / PxPerDb;
            g = Math.Clamp(MathF.Round(g * 4f) / 4f, -12f, 12f);
            if (f.State.EqGains[i] != g) { f.State.EqGains[i] = g; f.State.EqDirty = true; }
        }

        public override void OnMouseMove(SKPoint p, FrameContext f)
        {
            if (_drag >= 0) SetFromY(_drag, p.Y + _dragOffset, f);
        }

        public override void OnMouseUp(SKPoint p, FrameContext f)
        {
            if (_drag >= 0) f.PersistSettings?.Invoke();
            _drag = -1;
        }

        public override bool OnDoubleClick(SKPoint p, FrameContext f)
        {
            int i = HitCap(p, f.State.EqGains);
            if (i < 0) i = HitSlot(p);
            if (i < 0) return false;
            f.State.EqGains[i] = 0f;
            f.State.EqDirty = true;
            _drag = -1;
            f.PersistSettings?.Invoke();
            return true;
        }

        public override bool OnWheel(SKPoint p, int delta, FrameContext f)
        {
            int i = HitSlot(p);
            if (i < 0) return false;
            f.State.EqGains[i] = Math.Clamp(f.State.EqGains[i] + Math.Sign(delta) * 0.5f, -12f, 12f);
            f.State.EqDirty = true;
            f.PersistSettings?.Invoke();
            return true;
        }
    }
}
