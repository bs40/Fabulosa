using System;
using SkiaSharp;

namespace Fabulosa.UI.Views
{
    /// <summary>Outer brass frame, chassis, title bar (wordmark, tagline, window buttons) and the menu bar strip.</summary>
    public sealed class ChromeView : View
    {
        public int HoverMenu = -1;
        public int OpenMenu = -1;
        public float Fps;
        public float RenderMs;
        public readonly float[] MenuWidths = new float[Layout.MenuTitles.Length];

        // The wordmark PNG (2172 x 724, chrome letters on black) is cropped to its glow; these are the letters' extents within that crop.
        private static readonly SKRect WordmarkCrop = new SKRect(33, 120, 2172, 491);
        private const float CoreLeft = 0.0252f, CoreRight = 0.9790f, CoreTop = 0.1051f, CoreBottom = 0.9515f;
        private static SKImage _wordmark;
        private static bool _wordmarkLoaded;

        public ChromeView() : base(Layout.Window)
        {
            using var p = Theme.TextPaint(17f, SKColors.White, Theme.Sans);
            for (int i = 0; i < Layout.MenuTitles.Length; i++) MenuWidths[i] = p.MeasureText(Layout.MenuTitles[i]);
        }

        public SKRect MenuRect(int i) => new SKRect(Layout.MenuX[i] - 10, 58, Layout.MenuX[i] + MenuWidths[i] + 10, 86);

        public override void DrawStatic(SKCanvas c, FrameContext f)
        {
            var outer = Layout.Window;
            using (var brass = Theme.VerticalGradient(outer, new SKColor(0xd4, 0xcb, 0xa4), new SKColor(0x8c, 0x86, 0x68), new SKColor(0x5c, 0x59, 0x46), new SKColor(0x3a, 0x39, 0x2c)))
                c.DrawRoundRect(outer, 9, 9, brass);
            using (var hi = Theme.Stroke(new SKColor(0xf2, 0xea, 0xca, 160), 1.2f))
                c.DrawRoundRect(SKRect.Inflate(outer, -0.8f, -0.8f), 8.5f, 8.5f, hi);

            var content = Layout.Content;
            using (var chassis = Theme.VerticalGradient(content, new SKColor(0x35, 0x3b, 0x35), new SKColor(0x2a, 0x2f, 0x2a), new SKColor(0x21, 0x26, 0x21)))
                c.DrawRoundRect(content, 5, 5, chassis);
            using (var tex = new SKPaint { Shader = Theme.BrushedTexture(), BlendMode = SKBlendMode.Overlay, Color = new SKColor(255, 255, 255, 120) })
                c.DrawRoundRect(content, 5, 5, tex);
            c.Save();
            c.ClipRoundRect(new SKRoundRect(content, 5, 5), SKClipOperation.Intersect, true);
            using (var inner = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 8f, Color = new SKColor(0, 0, 0, 170), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f) })
                c.DrawRoundRect(SKRect.Inflate(content, 2, 2), 5, 5, inner);
            c.Restore();

            // title bar + menu strip
            var title = Layout.TitleBar;
            using (var tp = Theme.VerticalGradient(title, new SKColor(0x11, 0x1b, 0x1d), new SKColor(0x0a, 0x11, 0x13))) c.DrawRect(title, tp);
            using (var topLine = Theme.Fill(new SKColor(0x2a, 0x3a, 0x3c, 160))) c.DrawRect(new SKRect(title.Left, title.Top, title.Right, title.Top + 1), topLine);
            using (var black = Theme.Fill(SKColors.Black)) c.DrawRect(new SKRect(12, 49, 1660, 53), black);
            using (var mp = Theme.Fill(Theme.MenuBg)) c.DrawRect(Layout.MenuBar, mp);
            using (var e1 = Theme.Fill(new SKColor(0x6e, 0x6a, 0x52))) c.DrawRect(new SKRect(12, 91, 1660, 92.5f), e1);
            using (var e2 = Theme.Fill(new SKColor(0x08, 0x0a, 0x08))) c.DrawRect(new SKRect(12, 92.5f, 1660, 94.5f), e2);
            using (var div = Theme.Fill(new SKColor(0x26, 0x33, 0x34))) c.DrawRect(new SKRect(303, 58, 304, 86), div);

            DrawScrew(c, 11, 11); DrawScrew(c, 1661, 11); DrawScrew(c, 11, 930); DrawScrew(c, 1661, 930);

            DrawWordmark(c);

            Theme.Text(c, "HEAR MORE  ·  FEEL MORE", 836f, 36.5f, 13f, new SKColor(0xcb, 0xbf, 0x94), Theme.SansItalic, SKTextAlign.Center, 6.5f);
        }

        /// <summary>Chrome wordmark with its black background keyed out by a screen blend; letters 30 px tall on the bar's centre line.</summary>
        private static void DrawWordmark(SKCanvas c)
        {
            if (!_wordmarkLoaded) { _wordmarkLoaded = true; _wordmark = Theme.LoadImage("Fabulosa.Images.Wordmark"); }
            const float letterHeight = 30f, letterCentreY = 30.5f, letterLeft = 30f;
            float versionX;
            if (_wordmark != null)
            {
                float scale = letterHeight / ((CoreBottom - CoreTop) * WordmarkCrop.Height);
                float dw = WordmarkCrop.Width * scale, dh = WordmarkCrop.Height * scale;
                float top = letterCentreY - (CoreTop + CoreBottom) / 2f * dh;
                float left = letterLeft - CoreLeft * dw;
                using var p = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High, BlendMode = SKBlendMode.Screen };
                c.DrawImage(_wordmark, WordmarkCrop, new SKRect(left, top, left + dw, top + dh), p);
                versionX = left + CoreRight * dw + 14f;
            }
            else
            {
                versionX = letterLeft + Theme.Text(c, "FABULOSA", letterLeft, 41f, 26f, new SKColor(0xf0, 0xd8, 0xf0), Theme.SansBold, SKTextAlign.Left, 2f) + 16f;
            }
            Theme.Text(c, "v3.2", versionX, 41f, 14f, new SKColor(0xcf, 0xc7, 0xa6), Theme.Sans);
        }

        private static void DrawScrew(SKCanvas c, float x, float y)
        {
            using (var shadow = Theme.Glow(SKColors.Black, 1.5f, 150)) c.DrawCircle(x + 0.5f, y + 1f, 6.5f, shadow);
            using (var body = new SKPaint { IsAntialias = true, Shader = SKShader.CreateRadialGradient(new SKPoint(x - 2, y - 2), 8f, new[] { new SKColor(0xe2, 0xdc, 0xbe), new SKColor(0x8c, 0x86, 0x6c), new SKColor(0x4a, 0x47, 0x38) }, new[] { 0f, 0.6f, 1f }, SKShaderTileMode.Clamp) })
                c.DrawCircle(x, y, 6.2f, body);
            using (var rim = Theme.Stroke(new SKColor(0x2c, 0x2a, 0x20), 0.8f)) c.DrawCircle(x, y, 6.2f, rim);
            using (var slot = Theme.Stroke(new SKColor(0x2e, 0x2c, 0x22), 1.4f))
            {
                float a = 0.55f;
                c.DrawLine(x - 4.2f * MathF.Cos(a), y - 4.2f * MathF.Sin(a), x + 4.2f * MathF.Cos(a), y + 4.2f * MathF.Sin(a), slot);
                c.DrawLine(x + 4.2f * MathF.Sin(a), y - 4.2f * MathF.Cos(a), x - 4.2f * MathF.Sin(a), y + 4.2f * MathF.Cos(a), slot);
            }
        }

        public override void Draw(SKCanvas c, FrameContext f)
        {
            // menu labels with hover / open highlight
            for (int i = 0; i < Layout.MenuTitles.Length; i++)
            {
                var r = MenuRect(i);
                bool open = OpenMenu == i;
                bool hover = HoverMenu == i;
                if (open || hover)
                {
                    using var hl = Theme.Fill(open ? new SKColor(0x27, 0x3b, 0x3e) : new SKColor(0x1c, 0x2c, 0x2f));
                    c.DrawRoundRect(r, 4, 4, hl);
                }
                Theme.Text(c, Layout.MenuTitles[i], Layout.MenuX[i], 77f, 17f, open ? SKColors.White : new SKColor(0xe6, 0xe6, 0xe6), Theme.Sans);
            }

            DrawWindowButton(c, Layout.BtnMinimize, 0, f.Hover(Layout.BtnMinimize));
            DrawWindowButton(c, Layout.BtnMaximize, f.Host != null && f.Host.IsMaximized ? 3 : 1, f.Hover(Layout.BtnMaximize));
            DrawWindowButton(c, Layout.BtnClose, 2, f.Hover(Layout.BtnClose));

            if (f.State.ShowFps)
                Theme.Text(c, $"{Fps:0} fps   {RenderMs:0.0} ms", 640f, 77f, 12f, Theme.TextDim, Theme.Sans);
        }

        private static void DrawWindowButton(SKCanvas c, SKRect r, int kind, bool hover)
        {
            bool close = kind == 2;
            SKColor fill = hover ? (close ? new SKColor(0x8a, 0x1c, 0x1c) : new SKColor(0x1e, 0x2c, 0x2e)) : new SKColor(0x0a, 0x10, 0x12);
            using (var fp = Theme.Fill(fill)) c.DrawRoundRect(r, 3, 3, fp);
            using (var bp = Theme.Stroke(close ? new SKColor(0xd8, 0x30, 0x30) : new SKColor(0x9c, 0x95, 0x7a), 1.5f)) c.DrawRoundRect(r, 3, 3, bp);
            SKColor ink = close ? (hover ? SKColors.White : new SKColor(0xff, 0x4a, 0x4a)) : new SKColor(0xe8, 0xe0, 0xc0);
            using var ip = Theme.Stroke(ink, 2f);
            ip.StrokeCap = SKStrokeCap.Round;
            float cx = r.MidX, cy = r.MidY;
            switch (kind)
            {
                case 0: c.DrawLine(cx - 6, cy + 5, cx + 6, cy + 5, ip); break;
                case 1: ip.StrokeWidth = 1.7f; c.DrawRect(new SKRect(cx - 5.5f, cy - 5.5f, cx + 5.5f, cy + 5.5f), ip); break;
                case 3:
                    ip.StrokeWidth = 1.5f;
                    c.DrawRect(new SKRect(cx - 6.5f, cy - 3.5f, cx + 2.5f, cy + 5.5f), ip);
                    c.DrawLine(cx - 3.5f, cy - 3.5f, cx - 3.5f, cy - 6.5f, ip);
                    c.DrawLine(cx - 3.5f, cy - 6.5f, cx + 5.5f, cy - 6.5f, ip);
                    c.DrawLine(cx + 5.5f, cy - 6.5f, cx + 5.5f, cy + 2.5f, ip);
                    c.DrawLine(cx + 5.5f, cy + 2.5f, cx + 2.5f, cy + 2.5f, ip);
                    break;
                case 2: c.DrawLine(cx - 5.5f, cy - 5.5f, cx + 5.5f, cy + 5.5f, ip); c.DrawLine(cx + 5.5f, cy - 5.5f, cx - 5.5f, cy + 5.5f, ip); break;
            }
        }
    }
}
