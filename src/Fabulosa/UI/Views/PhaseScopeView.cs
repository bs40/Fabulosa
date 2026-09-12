using System;
using SkiaSharp;

namespace Fabulosa.UI.Views
{
    /// <summary>Lissajous / goniometer (X = left, Y = right) with phosphor persistence and a fitted stereo ellipse.</summary>
    public sealed class PhaseScopeView : View
    {
        private static readonly SKRect Screen = new SKRect(713, 414, 1004, 582);
        private static readonly SKColor Bg = new SKColor(0x04, 0x14, 0x10);
        private SKSurface _persist;
        private int _pw, _ph;
        private SKPoint[] _pts = new SKPoint[2048];

        public PhaseScopeView() : base(Layout.Phase) { }

        public override void DrawStatic(SKCanvas c, FrameContext f)
        {
            Theme.Panel(c, Bounds, "PHASE SCOPE");
            Theme.Screen(c, Screen, Bg);
            Theme.Text(c, "L", 728f, 503f, 14f, new SKColor(0xc8, 0xd0, 0xc8), Theme.Sans);
            Theme.Text(c, "R", 990f, 503f, 14f, new SKColor(0xc8, 0xd0, 0xc8), Theme.Sans, SKTextAlign.Right);
        }

        public override void Draw(SKCanvas c, FrameContext f)
        {
            int pw = Math.Max(1, (int)MathF.Round(Screen.Width * f.Scale));
            int ph = Math.Max(1, (int)MathF.Round(Screen.Height * f.Scale));
            if (_persist == null || _pw != pw || _ph != ph)
            {
                _persist?.Dispose();
                _persist = SKSurface.Create(new SKImageInfo(pw, ph, SKColorType.Rgba8888, SKAlphaType.Premul));
                _persist.Canvas.Clear(Bg);
                _pw = pw; _ph = ph;
            }

            var pc = _persist.Canvas;
            pc.ResetMatrix();
            using (var fade = Theme.Fill(Bg.WithAlpha(58))) pc.DrawRect(new SKRect(0, 0, pw, ph), fade);
            pc.Scale(f.Scale);

            float cx = Screen.Width / 2, cy = Screen.Height / 2;
            const float unit = 82f;
            // crosshair (redrawn each frame so it survives the fade)
            using (var cross = Theme.Stroke(new SKColor(0x1d, 0x5a, 0x3a), 1f))
            {
                pc.DrawLine(cx + 0.5f, 6, cx + 0.5f, Screen.Height - 6, cross);
                pc.DrawLine(6, cy + 0.5f, Screen.Width - 6, cy + 0.5f, cross);
            }

            var a = f.Analyzer;
            int n = a.PhaseCount;
            if (n > 0)
            {
                if (_pts.Length < n) _pts = new SKPoint[n];
                for (int i = 0; i < n; i++) _pts[i] = new SKPoint(cx + a.PhaseX[i] * unit, cy - a.PhaseY[i] * unit);
                var span = new ReadOnlySpan<SKPoint>(_pts, 0, n).ToArray();
                // soft halo on every other sample, crisp dot on all of them (square caps rasterise far cheaper than round)
                var half = new SKPoint[(n + 1) / 2];
                for (int i = 0; i < half.Length; i++) half[i] = _pts[i * 2];
                using (var soft = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3.6f, StrokeCap = SKStrokeCap.Square, Color = new SKColor(0x2c, 0xff, 0x7a, 22), BlendMode = SKBlendMode.Plus })
                    pc.DrawPoints(SKPointMode.Points, half, soft);
                using (var sharp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, StrokeCap = SKStrokeCap.Square, Color = new SKColor(0x6c, 0xff, 0x9a, 54), BlendMode = SKBlendMode.Plus })
                    pc.DrawPoints(SKPointMode.Points, span, sharp);
            }

            using (var img = _persist.Snapshot())
                c.DrawImage(img, Screen);

            // fitted ellipse (2.2 sigma) - the "ring" of the reference
            if (a.SignalPresent && a.PhaseEllipseA > 0.02f)
            {
                c.Save();
                c.ClipRoundRect(new SKRoundRect(Screen, 4, 4), SKClipOperation.Intersect, true);
                c.Translate(Screen.MidX, Screen.MidY);
                c.RotateDegrees(-a.PhaseEllipseAngle * 180f / MathF.PI);
                float ra = Math.Min(a.PhaseEllipseA * unit, 400f), rb = Math.Min(Math.Max(a.PhaseEllipseB * unit, 1.5f), 400f);
                using (var glow = Theme.GlowStroke(Theme.Green, 3f, 3f, 70)) c.DrawOval(0, 0, ra, rb, glow);
                using (var ring = Theme.Stroke(new SKColor(0x8c, 0xff, 0xb4, 170), 1.2f)) c.DrawOval(0, 0, ra, rb, ring);
                c.Restore();
            }
        }

        public override void OnSizeChanged(FrameContext f)
        {
            _persist?.Dispose();
            _persist = null;
        }
    }
}
