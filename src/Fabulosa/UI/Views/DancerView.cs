using System;
using SkiaSharp;

namespace Fabulosa.UI.Views
{
    /// <summary>Neon stick figure that steps through its 48 dance poses on the beat.</summary>
    public sealed class DancerView : View
    {
        private const int Cols = 12, Rows = 4, Frames = 48;
        private const float SourceInset = 2f;   // keeps the neighbouring frame out of the bilinear footprint
        private static SKImage _sheet;
        private static float _fw, _fh;
        private static bool _sheetTried;

        private readonly SKRect _screen;
        private readonly SKRect _sprite;
        private readonly SKRect _floorRect, _lightRect;
        private readonly SKPaint _spritePaint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
        private readonly SKPaint _floorPaint;
        private readonly SKPaint _lightPaint;
        private float _glow;

        public DancerView() : base(Layout.Dancer)
        {
            _screen = Layout.ScreenOf(Bounds);
            EnsureSheet();
            float fw = _fw > 0 ? _fw : 148f, fh = _fh > 0 ? _fh : 222f;
            float s = Math.Min((_screen.Width - 16f) / fw, (_screen.Height - 30f) / fh);
            float w = fw * s, h = fh * s;
            float top = _screen.Top + 10f;
            _sprite = new SKRect(_screen.MidX - w / 2, top, _screen.MidX + w / 2, top + h);
            float floorY = top + h * 0.93f;   // the figures' feet sit at 93 % of the frame height

            float r = w * 0.62f;
            var squash = SKMatrix.CreateScale(1f, 0.2f, _screen.MidX, floorY);
            _floorPaint = new SKPaint
            {
                IsAntialias = true,
                Shader = SKShader.CreateRadialGradient(new SKPoint(_screen.MidX, floorY), r,
                    new[] { new SKColor(0x6a, 0xf0, 0xff), new SKColor(0x3c, 0x7c, 0xff, 120), new SKColor(0x3c, 0x7c, 0xff, 0) },
                    new[] { 0f, 0.4f, 1f }, SKShaderTileMode.Clamp, squash),
            };
            _floorRect = new SKRect(_screen.MidX - r, floorY - r * 0.2f, _screen.MidX + r, floorY + r * 0.2f);
            _lightPaint = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(new SKPoint(0, floorY), new SKPoint(0, _sprite.Top),
                    new[] { new SKColor(0x2a, 0x5c, 0xff, 90), new SKColor(0x2a, 0x5c, 0xff, 0) }, null, SKShaderTileMode.Clamp),
            };
            _lightRect = new SKRect(_screen.Left, _sprite.Top, _screen.Right, floorY);
        }

        private static void EnsureSheet()
        {
            if (_sheet != null || _sheetTried) return;
            _sheetTried = true;
            try
            {
                var bmp = Theme.LoadBitmap("Fabulosa.Images.StickAnim");
                if (bmp == null) return;
                _fw = bmp.Width / (float)Cols;
                _fh = bmp.Height / (float)Rows;
                CleanFrames(bmp);
                _sheet = SKImage.FromBitmap(bmp);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("sprite sheet failed: " + ex.Message); }
        }

        /// <summary>
        /// The sheet has its frame numbers baked into each frame's top-left corner and faint grey grid
        /// lines along the frame edges. Both are grey/white while the figure is saturated neon, so those
        /// regions are keyed by saturation.
        /// </summary>
        private static void CleanFrames(SKBitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            var px = bmp.Pixels;
            int labelW = (int)(_fw * 0.5f), labelH = (int)(_fh * 0.27f);
            const int edge = 5;
            for (int row = 0; row < Rows; row++)
            {
                for (int col = 0; col < Cols; col++)
                {
                    int x0 = (int)MathF.Round(col * _fw), y0 = (int)MathF.Round(row * _fh);
                    int x1 = Math.Min((int)MathF.Round((col + 1) * _fw), w), y1 = Math.Min((int)MathF.Round((row + 1) * _fh), h);
                    for (int y = y0; y < y1; y++)
                    {
                        int ly = y - y0;
                        bool edgeRow = ly < edge || y >= y1 - edge;
                        for (int x = x0; x < x1; x++)
                        {
                            int lx = x - x0;
                            bool inLabel = lx < labelW && ly < labelH;
                            if (!inLabel && !edgeRow && lx >= edge && x < x1 - edge) continue;
                            var c = px[y * w + x];
                            if (c.Alpha == 0) continue;
                            if (inLabel && c.Alpha < 48) { px[y * w + x] = SKColors.Transparent; continue; }
                            int mx = Math.Max(c.Red, Math.Max(c.Green, c.Blue));
                            int mn = Math.Min(c.Red, Math.Min(c.Green, c.Blue));
                            if (mx - mn < 60) px[y * w + x] = SKColors.Transparent;
                        }
                    }
                }
            }
            bmp.Pixels = px;
        }

        public override void DrawStatic(SKCanvas c, FrameContext f)
        {
            Theme.Panel(c, Bounds, "DANCER");
            Theme.Screen(c, _screen, new SKColor(0x05, 0x08, 0x0f));
        }

        public override void Draw(SKCanvas c, FrameContext f)
        {
            var bt = f.Analyzer.Beat;
            bool dancing = bt.LastBeatTime >= 0;
            int frame = 0;
            if (dancing)
            {
                double steps = (bt.BeatCount + Math.Min(bt.Phase, 0.999f)) * f.State.DancerStepsPerBeat;
                frame = (int)(Math.Floor(steps) % Frames);
                if (frame < 0) frame += Frames;
            }
            float pulse = dancing ? bt.Pulse : 0f;
            _glow += (pulse - _glow) * (pulse > _glow ? 0.7f : 0.12f);

            c.Save();
            c.ClipRoundRect(new SKRoundRect(_screen, 4, 4), SKClipOperation.Intersect, true);
            _lightPaint.Color = SKColors.White.WithAlpha((byte)(40 + 180 * _glow));
            c.DrawRect(_lightRect, _lightPaint);
            _floorPaint.Color = SKColors.White.WithAlpha((byte)(60 + 190 * _glow));
            c.DrawRect(_floorRect, _floorPaint);
            if (_sheet != null)
            {
                int col = frame % Cols, row = frame / Cols;
                var src = new SKRect(col * _fw + SourceInset, row * _fh + SourceInset, (col + 1) * _fw - SourceInset, (row + 1) * _fh - SourceInset);
                float ix = SourceInset * _sprite.Width / _fw, iy = SourceInset * _sprite.Height / _fh;
                c.DrawImage(_sheet, src, SKRect.Inflate(_sprite, -ix, -iy), _spritePaint);
            }
            else
            {
                Theme.Text(c, "stickanim.png missing", _screen.MidX, _screen.MidY, 13f, Theme.TextDim, Theme.Sans, SKTextAlign.Center);
            }
            c.Restore();

            // tempo readout in the header row, with a dot that lights on every beat
            bool hasTempo = bt.HasTempo;
            string bpm = hasTempo ? $"{bt.Bpm:0} BPM" : "--- BPM";
            var textColour = hasTempo ? Theme.CyanText : new SKColor(0x6e, 0x7a, 0x7a);
            float tw = Theme.Text(c, bpm, Bounds.Right - 18f, Bounds.Top + 24.5f, 13f, textColour, Theme.SansBold, SKTextAlign.Right, 1f);
            float dotX = Bounds.Right - 18f - tw - 13f, dotY = Bounds.Top + 19.5f;
            if (_glow > 0.05f)
            {
                using var g = Theme.Glow(Theme.CyanText, 4f, (byte)(200 * _glow));
                c.DrawCircle(dotX, dotY, 4.5f, g);
            }
            using var dot = Theme.Fill(Theme.Mix(new SKColor(0x2a, 0x3a, 0x3c), Theme.CyanText, _glow));
            c.DrawCircle(dotX, dotY, 3.5f, dot);
        }
    }
}
