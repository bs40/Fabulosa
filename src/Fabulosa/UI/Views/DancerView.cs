using System;
using System.IO;
using Fabulosa.Audio;
using Fabulosa.Media;
using SkiaSharp;

namespace Fabulosa.UI.Views
{
    /// <summary>Neon stick figure that dances through its 48 poses in time with the beat, or the dancer clip once unlocked.</summary>
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

        private const float Soft = 3f;          // px over which the number corner and the tile edges fade into the backdrop
        private const float ClipTopFade = 28f;  // px over which the clip's top edge melts into its wall colour

        /// <summary>The figures are drawn for black; the screen uses the same colour so the tiles have no visible edge.</summary>
        private static readonly SKColor Backdrop = SKColors.Black;

        private static SKImage[] _tiles;
        private static bool _tilesTried;

        private readonly SKRect _screen;
        private readonly SKRect _dest;
        private readonly BeatSequencer _sequencer = new BeatSequencer(Frames);
        private readonly ClipClock _clipClock = new ClipClock();
        private readonly SKPaint _tilePaint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
        private readonly SKPaint _clipPaint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.Medium };
        private readonly SKPaint _screenEdge = Theme.Stroke(new SKColor(0x2a, 0x34, 0x33, 190), 1f);
        private readonly (SKRect Area, SKPaint Paint)[] _screenShade;
        private float _glow;

        public DancerView() : base(Layout.Dancer)
        {
            _screen = Layout.ScreenOf(Bounds);
            var inner = SKRect.Inflate(_screen, -6f, -6f);   // stay inside the screen's edge line
            float s = Math.Min(inner.Width / Crop.Width, inner.Height / Crop.Height);
            float w = Crop.Width * s, h = Crop.Height * s;
            _dest = new SKRect(inner.MidX - w / 2, inner.MidY - h / 2, inner.MidX + w / 2, inner.MidY + h / 2);
            _screenShade = BuildScreenShade(_screen);
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

        /// <summary>Gradients that recreate the screen's inset shadow on top of the clip, which covers the static one.</summary>
        private static (SKRect, SKPaint)[] BuildScreenShade(SKRect r)
        {
            const float depth = 7f;
            var dark = new SKColor(0, 0, 0, 110);
            (SKRect, SKPaint) Band(SKRect area, SKPoint from, SKPoint to) =>
                (area, new SKPaint { Shader = SKShader.CreateLinearGradient(from, to, new[] { dark, dark.WithAlpha(0) }, null, SKShaderTileMode.Clamp) });
            return new[]
            {
                Band(new SKRect(r.Left, r.Top, r.Right, r.Top + depth), new SKPoint(0, r.Top), new SKPoint(0, r.Top + depth)),
                Band(new SKRect(r.Left, r.Bottom - depth, r.Right, r.Bottom), new SKPoint(0, r.Bottom), new SKPoint(0, r.Bottom - depth)),
                Band(new SKRect(r.Left, r.Top, r.Left + depth, r.Bottom), new SKPoint(r.Left, 0), new SKPoint(r.Left + depth, 0)),
                Band(new SKRect(r.Right - depth, r.Top, r.Right, r.Bottom), new SKPoint(r.Right, 0), new SKPoint(r.Right - depth, 0)),
            };
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
            int frame = _sequencer.Update(bt, f.State.DancerFramesPerBeat);   // keeps counting under the clip, so the figure carries on when it returns
            var clip = f.DancerClip;
            f.DancerClipFrame = -1;
            if (f.State.ShowDancerClip && clip != null)
            {
                clip.Preload();
                if (clip.IsReady) f.DancerClipFrame = DrawClip(c, clip, bt);
            }
            if (f.DancerClipFrame < 0)
            {
                var tiles = Tiles();
                if (tiles != null) c.DrawImage(tiles[frame], _dest, _tilePaint);
                else Theme.Text(c, "stickanim.png missing", _screen.MidX, _screen.MidY, 13f, Theme.TextDim, Theme.Sans, SKTextAlign.Center);
            }

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

        /// <summary>
        /// The clip fills the screen width with its bottom edge on the screen's bottom. Above it the wall
        /// colour carries on, and the clip's top edge fades into it so hair and hands leave the frame softly.
        /// Returns the loop frame drawn.
        /// </summary>
        private int DrawClip(SKCanvas c, DancerClip clip, BeatTracker beat)
        {
            int index = _clipClock.Update(beat, clip.LoopFrames, clip.FramesPerBeat, clip.PhaseFrames);
            var wall = clip.WallColour(index);
            float s = Math.Min(_screen.Width / clip.Width, _screen.Height / clip.Height);
            float w = clip.Width * s, h = clip.Height * s;
            var dest = new SKRect(_screen.MidX - w / 2, _screen.Bottom - h, _screen.MidX + w / 2, _screen.Bottom);

            c.Save();
            c.ClipRoundRect(new SKRoundRect(_screen, 4, 4), SKClipOperation.Intersect, true);
            using (var fill = new SKPaint { Color = wall }) c.DrawRect(_screen, fill);
            c.DrawImage(clip.Frame(index), dest, _clipPaint);
            using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, dest.Top), new SKPoint(0, dest.Top + ClipTopFade), new[] { wall, wall.WithAlpha(0) }, null, SKShaderTileMode.Clamp))
            using (var fade = new SKPaint { Shader = shader })
                c.DrawRect(new SKRect(_screen.Left, dest.Top, _screen.Right, dest.Top + ClipTopFade), fade);
            foreach (var (area, paint) in _screenShade) c.DrawRect(area, paint);
            c.Restore();
            c.DrawRoundRect(_screen, 4, 4, _screenEdge);
            return index;
        }
    }
}
