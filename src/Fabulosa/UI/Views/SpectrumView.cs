using System;
using Fabulosa.Audio;
using Fabulosa.Model;
using SkiaSharp;

namespace Fabulosa.UI.Views
{
    /// <summary>64-band segmented rainbow spectrum, 20 Hz - 20 kHz, 0 to -60 dB.</summary>
    public sealed class SpectrumView : View
    {
        private const int Bars = Analyzer.BandCount;
        private float _plotLeft, _plotRight, _plotTop, _plotBottom, _pitch, _barW;
        private SKRect _screen, _modeRect;

        private readonly SKColor[] _colors = new SKColor[Bars];
        private readonly SKPaint[] _barPaints = new SKPaint[Bars];
        private readonly SKPaint[] _glowPaints = new SKPaint[Bars];
        private readonly SKPaint[] _peakPaints = new SKPaint[Bars];
        private readonly SKPaint _cutPaint = Theme.Stroke(Theme.ScreenBg, 1.3f);
        private SKPoint[] _segmentLines = Array.Empty<SKPoint>();

        private static readonly (float pos, SKColor color)[] Stops =
        {
            (0.00f, new SKColor(0xff, 0x38, 0x4c)),
            (0.10f, new SKColor(0xff, 0x52, 0x34)),
            (0.22f, new SKColor(0xff, 0x9a, 0x2c)),
            (0.34f, new SKColor(0xff, 0xd6, 0x2e)),
            (0.44f, new SKColor(0xe8, 0xff, 0x30)),
            (0.54f, new SKColor(0x4e, 0xff, 0x5e)),
            (0.64f, new SKColor(0x14, 0xe6, 0xe0)),
            (0.74f, new SKColor(0x2a, 0x84, 0xff)),
            (0.86f, new SKColor(0x9c, 0x40, 0xff)),
            (1.00f, new SKColor(0xe4, 0x3c, 0xf0)),
        };

        public SpectrumView()
        {
            for (int i = 0; i < Bars; i++)
            {
                _colors[i] = Sample(i / (float)(Bars - 1));
                _glowPaints[i] = Theme.Glow(_colors[i], 3.5f, 75);
                _peakPaints[i] = Theme.Fill(Theme.Lighten(_colors[i], 0.45f));
            }
        }

        public override Visualiser? PanelId => Visualiser.Spectrum;

        private static SKColor Sample(float t)
        {
            for (int i = 1; i < Stops.Length; i++)
            {
                if (t <= Stops[i].pos)
                {
                    float u = (t - Stops[i - 1].pos) / (Stops[i].pos - Stops[i - 1].pos);
                    return Theme.Mix(Stops[i - 1].color, Stops[i].color, u);
                }
            }
            return Stops[^1].color;
        }

        protected override void OnBoundsChanged()
        {
            var b = Bounds;
            _plotLeft = b.Left + 54f;
            _plotRight = b.Right - 54f;
            _plotTop = b.Top + 50f;
            _plotBottom = b.Bottom - 44f;
            _screen = new SKRect(b.Left + 14f, b.Top + 36f, b.Right - 14f, b.Bottom - 28f);
            _modeRect = new SKRect(b.Right - 74f, b.Top + 16f, b.Right - 12f, b.Top + 42f);
            _pitch = (_plotRight - _plotLeft) / Bars;
            _barW = Math.Max(2f, MathF.Round(_pitch * 0.77f));

            for (int i = 0; i < Bars; i++)
            {
                var col = _colors[i];
                _barPaints[i]?.Dispose();
                _barPaints[i] = new SKPaint
                {
                    IsAntialias = false,
                    Style = SKPaintStyle.Fill,
                    Shader = SKShader.CreateLinearGradient(new SKPoint(0, _plotTop), new SKPoint(0, _plotBottom), new[] { Theme.Lighten(col, 0.18f), col, Theme.Darken(col, 0.32f) }, new[] { 0f, 0.45f, 1f }, SKShaderTileMode.Clamp),
                };
            }
            int lines = Math.Max(0, (int)((_plotBottom - _plotTop) / 6f));
            _segmentLines = new SKPoint[lines * 2];
            for (int i = 0; i < lines; i++)
            {
                float y = _plotBottom - 6f * (i + 1) + 0.5f;
                _segmentLines[i * 2] = new SKPoint(_plotLeft - 2, y);
                _segmentLines[i * 2 + 1] = new SKPoint(_plotRight + 2, y);
            }
        }

        private float XForHz(float hz) => _plotLeft + (_plotRight - _plotLeft) * MathF.Log(hz / 20f) / MathF.Log(1000f);

        private float YForDb(float db) => _plotBottom - (db - Analyzer.MinDb) / (0 - Analyzer.MinDb) * (_plotBottom - _plotTop);

        public override void DrawStatic(SKCanvas c, FrameContext f)
        {
            Theme.Panel(c, Bounds, "SPECTRUM ANALYSER");
            Theme.Screen(c, _screen, Theme.ScreenBg);

            // LED-matrix background grid
            using (var minor = Theme.Stroke(new SKColor(0x0c, 0x14, 0x10), 1f))
            {
                for (float y = _plotBottom; y >= _plotTop; y -= 6f) c.DrawLine(_plotLeft - 6, y + 0.5f, _plotRight + 6, y + 0.5f, minor);
            }
            using (var vminor = Theme.Stroke(new SKColor(0x14, 0x20, 0x1a), 1f))
            using (var vmajor = Theme.Stroke(new SKColor(0x24, 0x36, 0x2c), 1f))
            {
                for (int decade = 10; decade <= 10000; decade *= 10)
                    for (int m = 2; m <= 9; m++)
                    {
                        float hz = decade * m;
                        if (hz < 20 || hz > 20000) continue;
                        float x = MathF.Round(XForHz(hz)) + 0.5f;
                        c.DrawLine(x, _plotTop - 6, x, _plotBottom + 4, vminor);
                    }
                foreach (float hz in new[] { 100f, 1000f, 10000f })
                {
                    float x = MathF.Round(XForHz(hz)) + 0.5f;
                    c.DrawLine(x, _plotTop - 6, x, _plotBottom + 4, vmajor);
                }
            }
            using (var major = Theme.Stroke(new SKColor(0x1c, 0x2c, 0x20), 1f))
                for (int db = 0; db >= -60; db -= 10)
                {
                    float y = MathF.Round(YForDb(db)) + 0.5f;
                    c.DrawLine(_plotLeft - 6, y, _plotRight + 6, y, major);
                }

            // scale labels (frequency labels that would collide in a narrow panel are left out)
            var labelColour = new SKColor(0xd0, 0xd0, 0xd0);
            for (int db = 0; db >= -60; db -= 10)
                Theme.Text(c, db.ToString(), _plotLeft - 8f, YForDb(db) + 4.5f, 13f, labelColour, Theme.Sans, SKTextAlign.Right);
            (float hz, string label)[] marks = { (20, "20"), (50, "50"), (100, "100"), (200, "200"), (500, "500"), (1000, "1K"), (2000, "2K"), (5000, "5K"), (10000, "10K"), (20000, "20K") };
            float lastRight = float.MinValue;
            foreach (var (hz, label) in marks)
            {
                float x = XForHz(hz), w = Theme.Measure(label, 13f, Theme.Sans);
                if (x - w / 2 < lastRight + 6f) continue;
                Theme.Text(c, label, x, Bounds.Bottom - 17f, 13f, labelColour, Theme.Sans, SKTextAlign.Center);
                lastRight = x + w / 2;
            }
            Theme.Text(c, "Hz", Bounds.Right - 26f, Bounds.Bottom - 17f, 13f, labelColour, Theme.Sans, SKTextAlign.Center);
        }

        public override void Draw(SKCanvas c, FrameContext f)
        {
            var a = f.Analyzer;
            c.Save();
            c.ClipRect(new SKRect(_plotLeft - 4, _plotTop - 12, _plotRight + 4, _plotBottom + 1));
            for (int i = 0; i < Bars; i++)
            {
                float db = Math.Clamp(a.Bands[i], Analyzer.MinDb, 6f);
                float top = YForDb(db);
                if (top < _plotTop - 10) top = _plotTop - 10;
                float x = _plotLeft + i * _pitch + (_pitch - _barW) / 2f;
                if (_plotBottom - top < 1.5f) continue;
                var r = new SKRect(x, top, x + _barW, _plotBottom);
                c.DrawRect(SKRect.Inflate(r, 2, 1), _glowPaints[i]);
                c.DrawRect(r, _barPaints[i]);
            }
            c.DrawPoints(SKPointMode.Lines, _segmentLines, _cutPaint);
            if (f.State.PeakHoldEnabled)
            {
                for (int i = 0; i < Bars; i++)
                {
                    float pk = Math.Clamp(a.BandPeaks[i], Analyzer.MinDb, 6f);
                    if (pk <= Analyzer.MinDb + 0.5f) continue;
                    float y = YForDb(pk);
                    float x = _plotLeft + i * _pitch + (_pitch - _barW) / 2f;
                    c.DrawRect(new SKRect(x, y - 1.5f, x + _barW, y + 0.5f), _peakPaints[i]);
                }
            }
            c.Restore();

            string mode = f.State.SpectrumChannel switch { SpectrumChannel.Left => "L", SpectrumChannel.Right => "R", _ => "L+R" };
            bool hover = f.Hover(_modeRect);
            Theme.Text(c, mode, Bounds.Right - 26f, Bounds.Top + 31f, 13.5f, hover ? new SKColor(0xff, 0x70, 0x70) : new SKColor(0xff, 0x3b, 0x3b), Theme.SansBold, SKTextAlign.Right, 1f);
        }

        public override bool OnMouseDown(SKPoint p, FrameContext f)
        {
            if (_modeRect.Contains(p))
            {
                f.State.SpectrumChannel = (SpectrumChannel)(((int)f.State.SpectrumChannel + 1) % 3);
                f.PersistSettings?.Invoke();
                return true;
            }
            return false;
        }
    }
}
