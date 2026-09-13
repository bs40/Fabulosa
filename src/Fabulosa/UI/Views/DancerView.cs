using System;
using System.IO;
using Fabulosa.Audio;
using SkiaSharp;

namespace Fabulosa.UI.Views
{
    /// <summary>Neon stick figure that dances through its 48 poses in time with the beat.</summary>
    public sealed class DancerView : View
    {
        private const int Cols = 12, Rows = 4, Frames = 48;

        /// <summary>
        /// Part of each 148 x 222 card that is shown, in card-local pixels: inside the card outline and
        /// tall enough for the highest raised hand (frame 6 reaches y 33).
        /// </summary>
        private static readonly SKRectI Crop = new SKRectI(4, 26, 144, 219);

        /// <summary>Top-left corner of the crop that still holds the bottom of the frame number (card y up to 31).</summary>
        private static readonly SKRectI NumberCorner = new SKRectI(0, 0, 32, 9);

        private const float Soft = 3f;   // px over which the number corner and the tile edges fade into the backdrop

        /// <summary>The figures are drawn for black; the screen uses the same colour so the tiles have no visible edge.</summary>
        private static readonly SKColor Backdrop = SKColors.Black;

        private static SKImage[] _tiles;
        private static bool _tilesTried;

        private readonly SKRect _screen;
        private readonly SKRect _dest;
        private readonly BeatSequencer _sequencer = new BeatSequencer(Frames);
        private readonly SKPaint _tilePaint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
        private float _glow;

        public DancerView() : base(Layout.Dancer)
        {
            _screen = Layout.ScreenOf(Bounds);
            var inner = SKRect.Inflate(_screen, -6f, -6f);   // stay inside the screen's edge line
            float s = Math.Min(inner.Width / Crop.Width, inner.Height / Crop.Height);
            float w = Crop.Width * s, h = Crop.Height * s;
            _dest = new SKRect(inner.MidX - w / 2, inner.MidY - h / 2, inner.MidX + w / 2, inner.MidY + h / 2);
        }

        /// <summary>One opaque image per frame: the card flattened onto the backdrop, cropped, number removed.</summary>
        private static SKImage[] Tiles()
        {
            if (_tilesTried) return _tiles;
            _tilesTried = true;
            try
            {
                using var sheet = Theme.LoadImage("Fabulosa.Images.StickAnim");
                if (sheet == null) return null;
                float fw = sheet.Width / (float)Cols, fh = sheet.Height / (float)Rows;   // 147.83 x 221.75: the cards sit on a fractional grid
                var tiles = new SKImage[Frames];
                using var copy = new SKPaint { FilterQuality = SKFilterQuality.None };
                for (int i = 0; i < Frames; i++)
                {
                    int ox = (int)MathF.Round(i % Cols * fw), oy = (int)MathF.Round(i / Cols * fh);
                    using var surface = SKSurface.Create(new SKImageInfo(Crop.Width, Crop.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
                    var c = surface.Canvas;
                    c.Clear(Backdrop);
                    c.DrawImage(sheet, SKRect.Create(ox + Crop.Left, oy + Crop.Top, Crop.Width, Crop.Height), SKRect.Create(0, 0, Crop.Width, Crop.Height), copy);
                    FadeToBackdrop(c);
                    tiles[i] = surface.Snapshot();
                }
                _tiles = tiles;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("sprite sheet failed: " + ex.Message); }
            return _tiles;
        }

        /// <summary>
        /// Blacks out the number corner and fades the tile edges into the backdrop. Only background
        /// haze lives in these bands; the figures stay well clear of them.
        /// </summary>
        private static void FadeToBackdrop(SKCanvas c)
        {
            float w = Crop.Width, h = Crop.Height, r = NumberCorner.Right, b = NumberCorner.Bottom;
            using (var solid = new SKPaint { Color = Backdrop }) c.DrawRect(new SKRect(0, 0, r, b), solid);
            Fade(c, new SKRect(r, 0, r + Soft, b), new SKPoint(r, 0), new SKPoint(r + Soft, 0));
            Fade(c, new SKRect(0, b, r + Soft, b + Soft), new SKPoint(0, b), new SKPoint(0, b + Soft));
            Fade(c, new SKRect(0, 0, w, Soft), new SKPoint(0, 0), new SKPoint(0, Soft));
            Fade(c, new SKRect(0, 0, Soft, h), new SKPoint(0, 0), new SKPoint(Soft, 0));
            Fade(c, new SKRect(w - Soft, 0, w, h), new SKPoint(w, 0), new SKPoint(w - Soft, 0));
            Fade(c, new SKRect(0, h - Soft, w, h), new SKPoint(0, h), new SKPoint(0, h - Soft));
        }

        private static void Fade(SKCanvas c, SKRect area, SKPoint solidAt, SKPoint clearAt)
        {
            using var shader = SKShader.CreateLinearGradient(solidAt, clearAt, new[] { Backdrop, Backdrop.WithAlpha(0) }, null, SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader };
            c.DrawRect(area, paint);
        }

        /// <summary>Writes every frame as the panel shows it into one image (self-test aid); returns the frame count.</summary>
        public static int WriteContactSheet(string path)
        {
            var tiles = Tiles();
            if (tiles == null) return 0;
            const int gap = 6;
            int w = Crop.Width, h = Crop.Height;
            using var surface = SKSurface.Create(new SKImageInfo(Cols * (w + gap) + gap, Rows * (h + gap) + gap, SKColorType.Rgba8888, SKAlphaType.Premul));
            surface.Canvas.Clear(new SKColor(0x30, 0x34, 0x30));
            for (int i = 0; i < tiles.Length; i++)
                surface.Canvas.DrawImage(tiles[i], gap + i % Cols * (w + gap), gap + i / Cols * (h + gap));
            using var data = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(path, data.ToArray());
            return tiles.Length;
        }

        public override void DrawStatic(SKCanvas c, FrameContext f)
        {
            Theme.Panel(c, Bounds, "DANCER");
            Theme.Screen(c, _screen, Backdrop);
        }

        public override void Draw(SKCanvas c, FrameContext f)
        {
            var bt = f.Analyzer.Beat;
            int frame = _sequencer.Update(bt, f.State.DancerFramesPerBeat);
            var tiles = Tiles();
            if (tiles != null) c.DrawImage(tiles[frame], _dest, _tilePaint);
            else Theme.Text(c, "stickanim.png missing", _screen.MidX, _screen.MidY, 13f, Theme.TextDim, Theme.Sans, SKTextAlign.Center);

            // tempo readout in the header row, with a dot that lights on every beat
            float pulse = bt.LastBeatTime >= 0 ? bt.Pulse : 0f;
            _glow += (pulse - _glow) * (pulse > _glow ? 0.7f : 0.12f);
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
