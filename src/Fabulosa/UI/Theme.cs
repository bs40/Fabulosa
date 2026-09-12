using System;
using System.IO;
using System.Reflection;
using SkiaSharp;

namespace Fabulosa.UI
{
    /// <summary>Colours, typefaces and the small set of drawing primitives every panel is built from.</summary>
    public static class Theme
    {
        // ---- palette (sampled from the reference render) ----
        public static readonly SKColor Chassis = new SKColor(0x2b, 0x30, 0x2b);
        public static readonly SKColor ChassisDark = new SKColor(0x1f, 0x24, 0x20);
        public static readonly SKColor Letterbox = new SKColor(0x08, 0x0a, 0x08);
        public static readonly SKColor TitleBg = new SKColor(0x0c, 0x13, 0x15);
        public static readonly SKColor MenuBg = new SKColor(0x0c, 0x15, 0x15);
        public static readonly SKColor PanelTop = new SKColor(0x0d, 0x13, 0x14);
        public static readonly SKColor PanelBottom = new SKColor(0x05, 0x09, 0x0a);
        public static readonly SKColor ScreenBg = new SKColor(0x03, 0x06, 0x07);
        public static readonly SKColor HeaderGold = new SKColor(0xe4, 0xd8, 0xa6);
        public static readonly SKColor TextLight = new SKColor(0xdc, 0xdc, 0xdc);
        public static readonly SKColor TextMid = new SKColor(0xc4, 0xc8, 0xc4);
        public static readonly SKColor TextDim = new SKColor(0x9a, 0xa4, 0x9e);
        public static readonly SKColor BrassHi = new SKColor(0xc9, 0xbe, 0x96);
        public static readonly SKColor BrassMid = new SKColor(0x7d, 0x77, 0x5c);
        public static readonly SKColor BrassLo = new SKColor(0x3e, 0x3c, 0x2e);
        public static readonly SKColor Cyan = new SKColor(0x2e, 0xf0, 0xe6);
        public static readonly SKColor CyanText = new SKColor(0x33, 0xe6, 0xff);
        public static readonly SKColor Red = new SKColor(0xff, 0x3a, 0x2e);
        public static readonly SKColor Pink = new SKColor(0xff, 0x2f, 0x7e);
        public static readonly SKColor Blue = new SKColor(0x1e, 0x8c, 0xf0);
        public static readonly SKColor Green = new SKColor(0x3c, 0xff, 0x7a);

        // ---- typefaces ----
        public static SKTypeface Sans { get; private set; }
        public static SKTypeface SansBold { get; private set; }
        public static SKTypeface SansItalic { get; private set; }
        public static SKTypeface Logo { get; private set; }
        public static SKTypeface Script { get; private set; }

        private static bool _initialised;

        public static void Init()
        {
            if (_initialised) return;
            _initialised = true;
            Sans = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;
            SansBold = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? Sans;
            SansItalic = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic) ?? Sans;
            Logo = LoadEmbedded("Fabulosa.Fonts.Michroma") ?? SansBold;
            Script = LoadEmbedded("Fabulosa.Fonts.Yellowtail") ?? SansItalic;
        }

        private static SKTypeface LoadEmbedded(string name)
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
                if (stream == null) return null;
                var ms = new MemoryStream();
                stream.CopyTo(ms);
                ms.Position = 0;
                return SKTypeface.FromStream(ms);
            }
            catch { return null; }
        }

        // ---- text ----
        public static SKPaint TextPaint(float size, SKColor color, SKTypeface face = null)
        {
            return new SKPaint
            {
                IsAntialias = true,
                SubpixelText = true,
                LcdRenderText = false,
                TextSize = size,
                Color = color,
                Typeface = face ?? Sans,
                TextAlign = SKTextAlign.Left,
            };
        }

        public static float Measure(string text, float size, SKTypeface face = null, float tracking = 0)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            using var p = TextPaint(size, SKColors.White, face);
            return Measure(text, p, tracking);
        }

        public static float Measure(string text, SKPaint paint, float tracking)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            if (tracking == 0) return paint.MeasureText(text);
            float w = 0;
            for (int i = 0; i < text.Length; i++) w += paint.MeasureText(text[i].ToString());
            return w + tracking * (text.Length - 1);
        }

        /// <summary>Draws text at a baseline with optional letter tracking; returns the advance width.</summary>
        public static float Text(SKCanvas c, string text, float x, float y, float size, SKColor color, SKTypeface face = null, SKTextAlign align = SKTextAlign.Left, float tracking = 0, SKPaint custom = null)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            SKPaint p = custom ?? TextPaint(size, color, face);
            try
            {
                if (custom != null) { p.TextSize = size; p.Color = color; p.Typeface = face ?? p.Typeface ?? Sans; }
                float width = Measure(text, p, tracking);
                float start = align == SKTextAlign.Center ? x - width / 2 : align == SKTextAlign.Right ? x - width : x;
                p.TextAlign = SKTextAlign.Left;
                if (tracking == 0)
                {
                    c.DrawText(text, start, y, p);
                }
                else
                {
                    float cx = start;
                    for (int i = 0; i < text.Length; i++)
                    {
                        string g = text[i].ToString();
                        c.DrawText(g, cx, y, p);
                        cx += p.MeasureText(g) + tracking;
                    }
                }
                return width;
            }
            finally
            {
                if (custom == null) p.Dispose();
            }
        }

        public static string Ellipsize(string text, float maxWidth, float size, SKTypeface face = null)
        {
            if (string.IsNullOrEmpty(text)) return "";
            using var p = TextPaint(size, SKColors.White, face);
            if (p.MeasureText(text) <= maxWidth) return text;
            const string dots = "…";
            float dotsW = p.MeasureText(dots);
            int n = text.Length;
            while (n > 0 && p.MeasureText(text.Substring(0, n)) + dotsW > maxWidth) n--;
            return text.Substring(0, Math.Max(0, n)).TrimEnd() + dots;
        }

        // ---- gradients / paints ----
        public static SKPaint Fill(SKColor color) => new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = color };
        public static SKPaint Stroke(SKColor color, float width) => new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = color, StrokeWidth = width };

        public static SKPaint VerticalGradient(SKRect r, params SKColor[] colors)
        {
            return new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Shader = SKShader.CreateLinearGradient(new SKPoint(r.Left, r.Top), new SKPoint(r.Left, r.Bottom), colors, null, SKShaderTileMode.Clamp),
            };
        }

        public static SKPaint DiagonalGradient(SKRect r, SKColor[] colors, float[] stops = null)
        {
            return new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Shader = SKShader.CreateLinearGradient(new SKPoint(r.Left, r.Top), new SKPoint(r.Right, r.Bottom), colors, stops, SKShaderTileMode.Clamp),
            };
        }

        public static SKColor Mix(SKColor a, SKColor b, float t)
        {
            t = Math.Clamp(t, 0, 1);
            return new SKColor(
                (byte)(a.Red + (b.Red - a.Red) * t),
                (byte)(a.Green + (b.Green - a.Green) * t),
                (byte)(a.Blue + (b.Blue - a.Blue) * t),
                (byte)(a.Alpha + (b.Alpha - a.Alpha) * t));
        }

        public static SKColor Darken(SKColor c, float amount) => Mix(c, SKColors.Black, amount);
        public static SKColor Lighten(SKColor c, float amount) => Mix(c, SKColors.White, amount);

        // ---- chrome primitives ----

        /// <summary>A bezelled panel with a brass edge and (optionally) a tracked gold header label.</summary>
        public static void Panel(SKCanvas c, SKRect r, string header = null)
        {
            const float radius = 7f;
            using (var shadow = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 110), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f) })
                c.DrawRoundRect(SKRect.Create(r.Left, r.Top + 2, r.Width, r.Height), radius, radius, shadow);

            using (var brass = DiagonalGradient(r, new[] { new SKColor(0xa6, 0x9f, 0x7f), new SKColor(0x5e, 0x5a, 0x46), new SKColor(0x8a, 0x84, 0x66), new SKColor(0x3c, 0x3a, 0x2c) }, new[] { 0f, 0.35f, 0.7f, 1f }))
                c.DrawRoundRect(r, radius, radius, brass);

            var inner = SKRect.Inflate(r, -2.2f, -2.2f);
            using (var fill = VerticalGradient(inner, PanelTop, PanelBottom))
                c.DrawRoundRect(inner, radius - 2, radius - 2, fill);

            using (var edge = Stroke(new SKColor(0x22, 0x2a, 0x29, 200), 1f))
                c.DrawRoundRect(SKRect.Inflate(inner, -1f, -1f), radius - 3, radius - 3, edge);

            if (!string.IsNullOrEmpty(header))
            {
                using var shadowText = TextPaint(13.5f, new SKColor(0, 0, 0, 160), SansBold);
                Text(c, header, r.Left + 18 + 0.6f, r.Top + 25.2f, 13.5f, new SKColor(0, 0, 0, 160), SansBold, SKTextAlign.Left, 3.4f, shadowText);
                Text(c, header, r.Left + 18, r.Top + 24.5f, 13.5f, HeaderGold, SansBold, SKTextAlign.Left, 3.4f);
            }
        }

        /// <summary>Inset display surface (the dark "glass" inside a panel).</summary>
        public static void Screen(SKCanvas c, SKRect r, SKColor fill, float radius = 4f)
        {
            using (var p = Fill(fill)) c.DrawRoundRect(r, radius, radius, p);
            c.Save();
            c.ClipRoundRect(new SKRoundRect(r, radius, radius), SKClipOperation.Intersect, true);
            using (var inner = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 7f, Color = new SKColor(0, 0, 0, 150), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3.5f) })
                c.DrawRoundRect(SKRect.Inflate(r, 1.5f, 1.5f), radius, radius, inner);
            c.Restore();
            using (var edge = Stroke(new SKColor(0x2a, 0x34, 0x33, 190), 1f))
                c.DrawRoundRect(r, radius, radius, edge);
        }

        /// <summary>Dark bevelled push button.</summary>
        public static void Button(SKCanvas c, SKRect r, bool hover = false, bool pressed = false, float radius = 4f)
        {
            SKColor top = pressed ? new SKColor(0x14, 0x19, 0x1a) : hover ? new SKColor(0x38, 0x40, 0x42) : new SKColor(0x2c, 0x33, 0x35);
            SKColor bottom = pressed ? new SKColor(0x1e, 0x24, 0x25) : hover ? new SKColor(0x1c, 0x22, 0x23) : new SKColor(0x14, 0x19, 0x1a);
            using (var shadow = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 120), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2f) })
                c.DrawRoundRect(SKRect.Create(r.Left, r.Top + 1.5f, r.Width, r.Height), radius, radius, shadow);
            using (var fill = VerticalGradient(r, top, bottom)) c.DrawRoundRect(r, radius, radius, fill);
            using (var edgeDark = Stroke(new SKColor(0x06, 0x08, 0x09), 1f)) c.DrawRoundRect(r, radius, radius, edgeDark);
            using (var edgeHi = Stroke(new SKColor(0x4a, 0x54, 0x56, pressed ? (byte)60 : (byte)140), 1f))
                c.DrawRoundRect(SKRect.Inflate(r, -1f, -1f), radius - 1, radius - 1, edgeHi);
        }

        /// <summary>Soft glow behind a shape: draws the given action with a blurred, translucent paint.</summary>
        public static SKPaint Glow(SKColor color, float sigma, byte alpha)
        {
            return new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = color.WithAlpha(alpha),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma),
            };
        }

        public static SKPaint GlowStroke(SKColor color, float width, float sigma, byte alpha)
        {
            return new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = width,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                Color = color.WithAlpha(alpha),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma),
            };
        }

        private static SKShader _brushed;

        /// <summary>Tiling brushed-metal texture used at low opacity over the chassis.</summary>
        public static SKShader BrushedTexture()
        {
            if (_brushed != null) return _brushed;
            const int w = 512, h = 128;
            var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
            var rnd = new Random(1234);
            for (int y = 0; y < h; y++)
            {
                int rowOffset = rnd.Next(-14, 15);
                int streak = rnd.Next(0, 100) < 10 ? rnd.Next(-30, 31) : 0;
                for (int x = 0; x < w; x++)
                {
                    int v = 128 + rowOffset + streak + rnd.Next(-9, 10);
                    v = Math.Clamp(v, 0, 255);
                    bmp.SetPixel(x, y, new SKColor((byte)v, (byte)v, (byte)v));
                }
            }
            _brushed = SKShader.CreateBitmap(bmp, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
            return _brushed;
        }
    }
}
