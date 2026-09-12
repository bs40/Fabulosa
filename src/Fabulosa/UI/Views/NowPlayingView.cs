using System;
using Fabulosa.Media;
using SkiaSharp;

namespace Fabulosa.UI.Views
{
    /// <summary>Album art, track metadata, progress bar and transport controls driven by the Windows media session.</summary>
    public sealed class NowPlayingView : View
    {
        private static readonly SKRect Art = new SKRect(36, 142, 156, 262);
        private static readonly SKRect Progress = new SKRect(36, 282, 296, 288);
        private static readonly SKRect[] Buttons = { new SKRect(36, 323, 110, 362), new SKRect(128, 323, 202, 362), new SKRect(222, 323, 296, 362) };

        private SKImage _placeholder;
        private SKImage _thumb;
        private int _thumbVersion = -1;
        private int _pressed = -1;

        public NowPlayingView() : base(Layout.NowPlaying) { }

        public override void DrawStatic(SKCanvas c, FrameContext f)
        {
            Theme.Panel(c, Bounds, "NOW PLAYING");
            using (var frame = Theme.Stroke(new SKColor(0x3a, 0x42, 0x44), 1f)) c.DrawRoundRect(SKRect.Inflate(Art, 1, 1), 4, 4, frame);
            using (var track = Theme.Fill(new SKColor(0x1a, 0x23, 0x25))) c.DrawRoundRect(Progress, 3, 3, track);
        }

        public override void Draw(SKCanvas c, FrameContext f)
        {
            var snap = f.Media?.Snapshot;
            bool hasMedia = snap != null && snap.HasSession && (!string.IsNullOrWhiteSpace(snap.Title) || !string.IsNullOrWhiteSpace(snap.Artist));

            SKImage img = null;
            if (hasMedia && snap.Thumbnail != null)
            {
                if (_thumbVersion != snap.ThumbnailVersion)
                {
                    _thumb?.Dispose();
                    _thumb = null;
                    try { _thumb = SKImage.FromEncodedData(snap.Thumbnail); } catch { _thumb = null; }
                    _thumbVersion = snap.ThumbnailVersion;
                }
                img = _thumb;
            }
            img ??= _placeholder ??= BuildPlaceholder();

            c.Save();
            c.ClipRoundRect(new SKRoundRect(Art, 4, 4), SKClipOperation.Intersect, true);
            DrawCover(c, img, Art);
            c.Restore();

            string title = hasMedia ? (string.IsNullOrWhiteSpace(snap.Title) ? "Unknown title" : snap.Title) : "Nothing playing";
            string artist = hasMedia ? snap.Artist : (f.Capture?.DeviceName ?? "");
            string album = hasMedia ? snap.Album : "System audio";
            string format = f.Capture != null ? f.Capture.FormatDescription : "";
            const float maxW = 134f;
            Theme.Text(c, Theme.Ellipsize(title, maxW, 16f, Theme.SansBold), 168f, 164f, 16f, SKColors.White, Theme.SansBold);
            Theme.Text(c, Theme.Ellipsize(artist, maxW, 16f), 168f, 187f, 16f, new SKColor(0xcf, 0xcf, 0xcf), Theme.Sans);
            Theme.Text(c, Theme.Ellipsize(album, maxW, 16f), 168f, 209f, 16f, new SKColor(0xcf, 0xcf, 0xcf), Theme.Sans);
            Theme.Text(c, Theme.Ellipsize(format, maxW, 13.5f), 168f, 257f, 13.5f, new SKColor(0xcf, 0xcf, 0xcf), Theme.Sans);

            // progress
            TimeSpan pos = TimeSpan.Zero, dur = TimeSpan.Zero;
            if (hasMedia) { pos = snap.CurrentPosition; dur = snap.Duration; }
            float frac = dur.TotalSeconds > 0 ? (float)Math.Clamp(pos.TotalSeconds / dur.TotalSeconds, 0, 1) : 0f;
            if (frac > 0)
            {
                var fillRect = new SKRect(Progress.Left, Progress.Top, Progress.Left + Progress.Width * frac, Progress.Bottom);
                using (var glow = Theme.Glow(Theme.Blue, 3f, 90)) c.DrawRoundRect(fillRect, 3, 3, glow);
                using (var fp = Theme.VerticalGradient(fillRect, new SKColor(0x4a, 0xb0, 0xff), new SKColor(0x10, 0x6c, 0xe0))) c.DrawRoundRect(fillRect, 3, 3, fp);
            }
            else if (!hasMedia && f.Analyzer != null && f.Analyzer.SignalPresent)
            {
                // no timeline: show a gentle activity pulse instead of a dead bar
                float t = (f.Time * 0.35f) % 1f;
                float w = 60f;
                float x = Progress.Left + (Progress.Width + w) * t - w;
                c.Save();
                c.ClipRoundRect(new SKRoundRect(Progress, 3, 3), SKClipOperation.Intersect, true);
                using var pulse = new SKPaint { IsAntialias = true, Shader = SKShader.CreateLinearGradient(new SKPoint(x, 0), new SKPoint(x + w, 0), new[] { Theme.Blue.WithAlpha(0), Theme.Blue.WithAlpha(200), Theme.Blue.WithAlpha(0) }, null, SKShaderTileMode.Clamp) };
                c.DrawRect(new SKRect(x, Progress.Top, x + w, Progress.Bottom), pulse);
                c.Restore();
            }
            Theme.Text(c, FormatTime(pos), 36f, 305f, 14f, new SKColor(0xd0, 0xd0, 0xd0), Theme.Sans);
            Theme.Text(c, FormatTime(dur), 296f, 305f, 14f, new SKColor(0xd0, 0xd0, 0xd0), Theme.Sans, SKTextAlign.Right);

            // transport buttons
            bool playing = hasMedia && snap.State == PlayState.Playing;
            for (int i = 0; i < 3; i++)
            {
                var r = Buttons[i];
                bool hover = f.Hover(r);
                Theme.Button(c, r, hover, _pressed == i && f.MouseDown);
                using var ink = Theme.Fill(hover ? SKColors.White : new SKColor(0xdc, 0xdc, 0xdc));
                float cx = r.MidX, cy = r.MidY;
                using var path = new SKPath();
                switch (i)
                {
                    case 0:
                        Triangle(path, cx - 1, cy, -1); Triangle(path, cx + 11, cy, -1); break;
                    case 1:
                        if (playing) { path.AddRect(new SKRect(cx - 6.5f, cy - 8, cx - 2.5f, cy + 8)); path.AddRect(new SKRect(cx + 2.5f, cy - 8, cx + 6.5f, cy + 8)); }
                        else Triangle(path, cx - 5, cy, 1);
                        break;
                    case 2:
                        Triangle(path, cx - 11, cy, 1); Triangle(path, cx + 1, cy, 1); break;
                }
                c.DrawPath(path, ink);
            }
        }

        private static void Triangle(SKPath p, float x, float cy, int dir)
        {
            // x is the flat side's x; the apex is 11 px away in the given direction
            p.MoveTo(x, cy - 7.5f);
            p.LineTo(x, cy + 7.5f);
            p.LineTo(x + 11 * dir, cy);
            p.Close();
        }

        private static string FormatTime(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        }

        private static void DrawCover(SKCanvas c, SKImage img, SKRect dst)
        {
            float s = Math.Max(dst.Width / img.Width, dst.Height / img.Height);
            float w = img.Width * s, h = img.Height * s;
            var target = new SKRect(dst.MidX - w / 2, dst.MidY - h / 2, dst.MidX + w / 2, dst.MidY + h / 2);
            using var p = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
            c.DrawImage(img, target, p);
        }

        /// <summary>Procedural synth-wave sunset used when the session has no artwork.</summary>
        private static SKImage BuildPlaceholder()
        {
            const int size = 240;
            using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
            var c = surface.Canvas;
            var full = new SKRect(0, 0, size, size);
            using (var sky = Theme.VerticalGradient(full, new SKColor(0x12, 0x07, 0x33), new SKColor(0x4a, 0x12, 0x62), new SKColor(0xc7, 0x2c, 0x6e), new SKColor(0xff, 0x7a, 0x3a)))
                c.DrawRect(full, sky);

            var rnd = new Random(7);
            using (var star = Theme.Fill(new SKColor(255, 255, 255, 170)))
                for (int i = 0; i < 40; i++) c.DrawCircle(rnd.Next(size), rnd.Next(size / 2), rnd.Next(10, 20) / 10f * 0.6f, star);

            float sunY = 128, sunR = 62;
            using (var glow = Theme.Glow(new SKColor(0xff, 0x6a, 0x5a), 16f, 150)) c.DrawCircle(120, sunY, sunR + 4, glow);
            using (var sun = new SKPaint { IsAntialias = true, Shader = SKShader.CreateLinearGradient(new SKPoint(0, sunY - sunR), new SKPoint(0, sunY + sunR), new[] { new SKColor(0xff, 0xe9, 0x6a), new SKColor(0xff, 0x8c, 0x4a), new SKColor(0xff, 0x2e, 0x7a) }, null, SKShaderTileMode.Clamp) })
                c.DrawCircle(120, sunY, sunR, sun);
            using (var stripe = Theme.Fill(new SKColor(0x8a, 0x1e, 0x5c)))
            {
                float[] ys = { 8, 20, 34, 50 };
                float[] hs = { 2, 3.5f, 5, 7 };
                for (int i = 0; i < ys.Length; i++) c.DrawRect(new SKRect(120 - sunR - 2, sunY + ys[i], 120 + sunR + 2, sunY + ys[i] + hs[i]), stripe);
            }

            // mountains
            using (var mtn = new SKPath())
            {
                mtn.MoveTo(0, 175); mtn.LineTo(30, 130); mtn.LineTo(60, 160); mtn.LineTo(95, 118); mtn.LineTo(130, 152); mtn.LineTo(165, 112);
                mtn.LineTo(200, 150); mtn.LineTo(240, 128); mtn.LineTo(240, 240); mtn.LineTo(0, 240); mtn.Close();
                using var fill = Theme.VerticalGradient(new SKRect(0, 110, 0, 240), new SKColor(0x1b, 0x1e, 0x66), new SKColor(0x08, 0x0a, 0x2a));
                c.DrawPath(mtn, fill);
                using var wire = Theme.Stroke(new SKColor(0x4f, 0xd3, 0xff, 190), 1.4f);
                c.DrawPath(mtn, wire);
            }

            // grid floor
            using (var grid = Theme.Stroke(new SKColor(0x6a, 0x9c, 0xff, 200), 1.2f))
            {
                float horizon = 170f;
                for (int i = 0; i < 9; i++)
                {
                    float t = i / 8f;
                    float y = horizon + (size - horizon) * t * t;
                    c.DrawLine(0, y, size, y, grid);
                }
                for (int i = -6; i <= 6; i++)
                {
                    float x0 = 120 + i * 14f;
                    float x1 = 120 + i * 60f;
                    c.DrawLine(x0, horizon, x1, size, grid);
                }
            }
            return surface.Snapshot();
        }

        public override bool OnMouseDown(SKPoint p, FrameContext f)
        {
            for (int i = 0; i < 3; i++)
            {
                if (Buttons[i].Contains(p))
                {
                    _pressed = i;
                    switch (i)
                    {
                        case 0: f.Media?.Previous(); break;
                        case 1: f.Media?.TogglePlayPause(); break;
                        case 2: f.Media?.Next(); break;
                    }
                    return true;
                }
            }
            var hit = SKRect.Inflate(Progress, 0, 5);
            if (hit.Contains(p))
            {
                var snap = f.Media?.Snapshot;
                if (snap != null && snap.HasSession && snap.Duration > TimeSpan.Zero)
                {
                    float frac = Math.Clamp((p.X - Progress.Left) / Progress.Width, 0f, 1f);
                    f.Media.Seek(TimeSpan.FromSeconds(snap.Duration.TotalSeconds * frac));
                }
                return true;
            }
            return false;
        }

        public override void OnMouseUp(SKPoint p, FrameContext f) { _pressed = -1; }
    }
}
