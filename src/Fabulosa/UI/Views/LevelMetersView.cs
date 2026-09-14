using System;
using Fabulosa.Model;
using SkiaSharp;

namespace Fabulosa.UI.Views
{
    /// <summary>Two analogue VU meters with true ballistics plus 30-segment peak / RMS LED ladders.</summary>
    public sealed class LevelMetersView : View
    {
        private static readonly SKRect BezelL = new SKRect(1130, 143, 1380, 293);
        private static readonly SKRect BezelR = new SKRect(1390, 143, 1640, 293);
        private static readonly SKRect Readout = new SKRect(1550, 309, 1640, 360);
        private const float LedX0 = 1196f, LedPitch = 11.5f, LedR = 4.2f, LedYPeak = 322f, LedYRms = 348f;
        private const int LedCount = 30;
        private const float PivotDrop = 70f, Radius = 148f, Sweep = 38f;

        /// <summary>The meters are drawn for this 542 x 274 panel and scaled uniformly into whatever space the layout gives them.</summary>
        private static readonly SKRect Natural = new SKRect(1114, 100, 1656, 374);
        private SKMatrix _toBounds = SKMatrix.Identity;

        public override Visualiser? PanelId => Visualiser.LevelMeters;

        protected override void OnBoundsChanged()
        {
            float s = Math.Min(Bounds.Width / Natural.Width, Bounds.Height / Natural.Height);
            _toBounds = SKMatrix.CreateScaleTranslation(s, s, Bounds.MidX - Natural.MidX * s, Bounds.MidY - Natural.MidY * s);
        }

        /// <summary>The little power indicator stays in the panel's top-right corner whatever the panel's size.</summary>
        private SKPoint PowerLed => new SKPoint(Bounds.Right - 26f, Bounds.Top + 19f);

        private static SKRect Face(SKRect bezel) => new SKRect(bezel.Left + 10, bezel.Top + 9, bezel.Right - 10, bezel.Top + 113);
        private static SKPoint Pivot(SKRect face) => new SKPoint(face.MidX, face.Bottom + PivotDrop);

        /// <summary>Needle angle in degrees from vertical for a linear deflection (1.0 = 0 VU).</summary>
        private static float AngleFor(float deflection) => -Sweep + 2 * Sweep * Math.Clamp(deflection, 0f, 1.6f) / 1.413f;
        private static float AngleForVu(float vu) => AngleFor(MathF.Pow(10f, vu / 20f));

        public override void DrawStatic(SKCanvas c, FrameContext f)
        {
            Theme.Panel(c, Bounds, "LEVEL METERS");

            var led = PowerLed;
            using (var ring = new SKPaint { IsAntialias = true, Shader = SKShader.CreateRadialGradient(new SKPoint(led.X - 1, led.Y - 1), 6f, new[] { new SKColor(0x3a, 0x40, 0x40), new SKColor(0x10, 0x14, 0x14) }, null, SKShaderTileMode.Clamp) })
                c.DrawCircle(led, 5.5f, ring);
            using (var rimp = Theme.Stroke(new SKColor(0x55, 0x5c, 0x5c), 0.8f)) c.DrawCircle(led, 5.5f, rimp);

            c.Save();
            c.Concat(ref _toBounds);
            DrawMeterStatic(c, BezelL, "LEFT");
            DrawMeterStatic(c, BezelR, "RIGHT");

            Theme.Text(c, "PEAK", 1132f, 327f, 13f, new SKColor(0xd5, 0xd5, 0xd5), Theme.SansBold, SKTextAlign.Left, 1f);
            Theme.Text(c, "RMS", 1132f, 353f, 13f, new SKColor(0xd5, 0xd5, 0xd5), Theme.SansBold, SKTextAlign.Left, 1f);

            // LED strip background
            using (var strip = Theme.Fill(new SKColor(0x05, 0x09, 0x0a)))
            {
                c.DrawRoundRect(new SKRect(LedX0 - 10, LedYPeak - 9, LedX0 + LedPitch * (LedCount - 1) + 10, LedYPeak + 9), 4, 4, strip);
                c.DrawRoundRect(new SKRect(LedX0 - 10, LedYRms - 9, LedX0 + LedPitch * (LedCount - 1) + 10, LedYRms + 9), 4, 4, strip);
            }

            // readout box
            using (var rb = Theme.Fill(new SKColor(0x05, 0x0a, 0x0b))) c.DrawRoundRect(Readout, 3, 3, rb);
            using (var re = Theme.Stroke(new SKColor(0x2c, 0x35, 0x36), 1f)) c.DrawRoundRect(Readout, 3, 3, re);
            using (var div = Theme.Fill(new SKColor(0x2c, 0x35, 0x36))) c.DrawRect(new SKRect(Readout.Left + 4, 334.5f, Readout.Right - 4, 335.5f), div);
            c.Restore();
        }

        private static void DrawMeterStatic(SKCanvas c, SKRect bezel, string label)
        {
            // bezel: chrome edge + dark body
            using (var shadow = Theme.Glow(SKColors.Black, 3f, 140)) c.DrawRoundRect(SKRect.Create(bezel.Left, bezel.Top + 2, bezel.Width, bezel.Height), 6, 6, shadow);
            using (var chrome = Theme.DiagonalGradient(bezel, new[] { new SKColor(0xc4, 0xbc, 0xa4), new SKColor(0x62, 0x5e, 0x4c), new SKColor(0x9a, 0x94, 0x7a), new SKColor(0x40, 0x3e, 0x30) }, new[] { 0f, 0.35f, 0.7f, 1f }))
                c.DrawRoundRect(bezel, 6, 6, chrome);
            var body = SKRect.Inflate(bezel, -2.5f, -2.5f);
            using (var bp = Theme.VerticalGradient(body, new SKColor(0x15, 0x1b, 0x1c), new SKColor(0x0a, 0x0e, 0x0f))) c.DrawRoundRect(body, 4, 4, bp);

            var face = Face(bezel);
            using (var fp = new SKPaint { IsAntialias = true, Shader = SKShader.CreateRadialGradient(new SKPoint(face.MidX, face.Top + 40), 165f, new[] { new SKColor(0xff, 0xe4, 0xa4), new SKColor(0xf4, 0xd0, 0x86), new SKColor(0xc6, 0x9c, 0x58) }, new[] { 0f, 0.55f, 1f }, SKShaderTileMode.Clamp) })
                c.DrawRoundRect(face, 3, 3, fp);
            c.Save();
            c.ClipRoundRect(new SKRoundRect(face, 3, 3), SKClipOperation.Intersect, true);
            using (var inner = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 6f, Color = new SKColor(0x40, 0x28, 0x08, 110), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3f) })
                c.DrawRoundRect(SKRect.Inflate(face, 1.5f, 1.5f), 3, 3, inner);

            var pivot = Pivot(face);
            var oval = new SKRect(pivot.X - Radius, pivot.Y - Radius, pivot.X + Radius, pivot.Y + Radius);
            float aStart = AngleForVu(-20f), aZero = AngleForVu(0f), aEnd = AngleForVu(3f);
            using (var arcBlack = Theme.Stroke(new SKColor(0x1a, 0x14, 0x08), 1.6f))
                c.DrawArc(oval, aStart - 90f, aZero - aStart, false, arcBlack);
            using (var arcRed = Theme.Stroke(new SKColor(0xd8, 0x1c, 0x1c), 4f))
                c.DrawArc(oval, aZero - 90f, aEnd - aZero, false, arcRed);

            void Tick(float vu, float len, SKColor col, float width)
            {
                float a = AngleForVu(vu) * MathF.PI / 180f;
                float sx = pivot.X + Radius * MathF.Sin(a), sy = pivot.Y - Radius * MathF.Cos(a);
                float ex = pivot.X + (Radius - len) * MathF.Sin(a), ey = pivot.Y - (Radius - len) * MathF.Cos(a);
                using var tp = Theme.Stroke(col, width);
                c.DrawLine(sx, sy, ex, ey, tp);
            }
            var black = new SKColor(0x1a, 0x14, 0x08);
            var red = new SKColor(0xd8, 0x1c, 0x1c);
            foreach (float vu in new[] { -20f, -10f, -7f, -5f, -3f, 0f }) Tick(vu, 9f, black, 1.6f);
            foreach (float vu in new[] { -15f, -8f, -6f, -4f, -2f, -1f }) Tick(vu, 5f, black, 1.1f);
            foreach (float vu in new[] { 1f, 2f }) Tick(vu, 6f, red, 1.4f);
            Tick(3f, 9f, red, 1.8f);

            void Label(string text, float vu, SKColor col, float size = 12.5f)
            {
                float a = AngleForVu(vu) * MathF.PI / 180f;
                float lx = pivot.X + (Radius + 13f) * MathF.Sin(a), ly = pivot.Y - (Radius + 13f) * MathF.Cos(a);
                Theme.Text(c, text, lx, ly + 4.5f, size, col, Theme.SansBold, SKTextAlign.Center);
            }
            Label("-20", -20, black); Label("-10", -10, black); Label("-7", -7, black); Label("-5", -5, black); Label("-3", -3, black);
            Label("0", 0, red); Label("+3", 3, red);
            Theme.Text(c, "dB", face.MidX, face.Top + 88f, 16f, black, Theme.SansBold, SKTextAlign.Center);
            c.Restore();

            // label strip
            var strip = new SKRect(face.Left, 259, face.Right, 285);
            using (var sp = Theme.Fill(new SKColor(0x0d, 0x16, 0x17))) c.DrawRoundRect(strip, 2, 2, sp);
            using (var se = Theme.Stroke(new SKColor(0x2a, 0x33, 0x34), 1f)) c.DrawRoundRect(strip, 2, 2, se);
            Theme.Text(c, label, strip.MidX, 278f, 16f, SKColors.White, Theme.Sans, SKTextAlign.Center, 0.5f);
        }

        public override void Draw(SKCanvas c, FrameContext f)
        {
            var a = f.Analyzer;
            c.Save();
            c.Concat(ref _toBounds);
            DrawNeedle(c, BezelL, a.VuLeft);
            DrawNeedle(c, BezelR, a.VuRight);

            DrawLeds(c, LedYPeak, a.PeakDb);
            DrawLeds(c, LedYRms, a.RmsDb);

            Theme.Text(c, FormatDb(a.PeakDb), 1628f, 328.5f, 14f, new SKColor(0xd9, 0xd9, 0xd9), Theme.Sans, SKTextAlign.Right, 0.5f);
            Theme.Text(c, FormatDb(a.RmsDb), 1628f, 354.5f, 14f, new SKColor(0xd9, 0xd9, 0xd9), Theme.Sans, SKTextAlign.Right, 0.5f);
            c.Restore();

            // power LED glows when audio is flowing
            bool live = f.Capture != null && f.Capture.IsRunning;
            using var led = Theme.Fill(live ? new SKColor(0x4a, 0xff, 0x6a) : new SKColor(0x3a, 0x40, 0x40));
            c.DrawCircle(PowerLed, 2.4f, led);
            if (live && a.SignalPresent)
            {
                using var glow = Theme.Glow(new SKColor(0x4a, 0xff, 0x6a), 3f, 140);
                c.DrawCircle(PowerLed, 3f, glow);
            }
        }

        private static string FormatDb(float db)
        {
            if (db < -60f) return "-60.0 dB";
            if (db > 12f) db = 12f;
            return db.ToString("0.0") + " dB";
        }

        private static void DrawNeedle(SKCanvas c, SKRect bezel, float deflection)
        {
            var face = Face(bezel);
            var pivot = Pivot(face);
            float a = AngleFor(deflection);
            if (a > Sweep + 3f) a = Sweep + 3f;
            float rad = a * MathF.PI / 180f;
            float tipX = pivot.X + (Radius + 12f) * MathF.Sin(rad), tipY = pivot.Y - (Radius + 12f) * MathF.Cos(rad);
            c.Save();
            c.ClipRoundRect(new SKRoundRect(face, 3, 3), SKClipOperation.Intersect, true);
            using (var shadow = Theme.GlowStroke(SKColors.Black, 2.2f, 1.5f, 70)) c.DrawLine(pivot.X + 2, pivot.Y + 2, tipX + 2, tipY + 2.5f, shadow);
            using (var needle = Theme.Stroke(new SKColor(0x17, 0x12, 0x0c), 1.9f)) { needle.StrokeCap = SKStrokeCap.Round; c.DrawLine(pivot.X, pivot.Y, tipX, tipY, needle); }
            // glass highlight
            using (var glass = new SKPaint { IsAntialias = true, Shader = SKShader.CreateLinearGradient(new SKPoint(face.Left, face.Top), new SKPoint(face.Left + 60, face.Top + 90), new[] { new SKColor(255, 255, 255, 60), new SKColor(255, 255, 255, 10), new SKColor(255, 255, 255, 0) }, new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp) })
                c.DrawRect(face, glass);
            c.Restore();
        }

        private static readonly SKColor[] OnColors = { new SKColor(0x2c, 0xff, 0x4a), new SKColor(0xff, 0xe2, 0x3a), new SKColor(0xff, 0x9a, 0x2a), new SKColor(0xff, 0x2f, 0x2f) };
        private static readonly SKColor[] OffColors = { new SKColor(0x0d, 0x38, 0x16), new SKColor(0x3a, 0x36, 0x0e), new SKColor(0x3c, 0x26, 0x0e), new SKColor(0x3a, 0x10, 0x10) };

        private static int Zone(int k) => k < 14 ? 0 : k < 22 ? 1 : k < 26 ? 2 : 3;
        private static float Threshold(int k) => -45f + 1.5f * k;

        // LEDs are pre-rendered once (on/off x 4 zones) at 4x and blitted: 60 gradient+glow draws per frame would cost more than the rest of the panel.
        private const float LedTile = 16f;
        private const int LedTilePx = 64;
        private static readonly SKImage[] LedImages = new SKImage[8];
        private static readonly SKPaint LedPaint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };

        private static SKImage LedImage(int zone, bool on)
        {
            int idx = zone * 2 + (on ? 1 : 0);
            if (LedImages[idx] != null) return LedImages[idx];
            using var surface = SKSurface.Create(new SKImageInfo(LedTilePx, LedTilePx, SKColorType.Rgba8888, SKAlphaType.Premul));
            var c = surface.Canvas;
            c.Clear(SKColors.Transparent);
            float s = LedTilePx / LedTile;
            c.Scale(s);
            float cx = LedTile / 2, cy = LedTile / 2;
            if (on)
            {
                using var glow = Theme.Glow(OnColors[zone], 2.6f, 120);
                c.DrawCircle(cx, cy, LedR + 1.2f, glow);
            }
            using var fill = new SKPaint
            {
                IsAntialias = true,
                Shader = SKShader.CreateRadialGradient(new SKPoint(cx - 1.2f, cy - 1.2f), LedR * 1.3f,
                    on ? new[] { Theme.Lighten(OnColors[zone], 0.55f), OnColors[zone], Theme.Darken(OnColors[zone], 0.2f) } : new[] { Theme.Lighten(OffColors[zone], 0.12f), OffColors[zone], Theme.Darken(OffColors[zone], 0.4f) },
                    new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp),
            };
            c.DrawCircle(cx, cy, LedR, fill);
            using var rim = Theme.Stroke(new SKColor(0, 0, 0, 120), 0.8f);
            c.DrawCircle(cx, cy, LedR, rim);
            LedImages[idx] = surface.Snapshot();
            return LedImages[idx];
        }

        private static void DrawLeds(SKCanvas c, float y, float levelDb)
        {
            for (int k = 0; k < LedCount; k++)
            {
                float x = LedX0 + k * LedPitch;
                bool on = levelDb >= Threshold(k);
                var img = LedImage(Zone(k), on);
                c.DrawImage(img, new SKRect(x - LedTile / 2, y - LedTile / 2, x + LedTile / 2, y + LedTile / 2), LedPaint);
            }
        }
    }
}
