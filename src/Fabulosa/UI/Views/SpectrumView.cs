using System;
using Fabulosa.Audio;
using SkiaSharp;

namespace Fabulosa.UI.Views
{
    /// <summary>64-band segmented rainbow spectrum, 20 Hz - 20 kHz, 0 to -60 dB.</summary>
    public sealed class SpectrumView : View
    {
        private const float PlotLeft = 380f, PlotRight = 1050f, PlotTop = 150f, PlotBottom = 330f;
        private const float BarW = 8f;
        private static readonly SKRect Screen = new SKRect(340, 136, 1090, 346);
        private static readonly SKRect ModeRect = new SKRect(1030, 116, 1092, 142);
        private const int Bars = Analyzer.BandCount;
        private readonly float _pitch = (PlotRight - PlotLeft) / Bars;

        private readonly SKColor[] _colors = new SKColor[Bars];
        private readonly SKPaint[] _barPaints = new SKPaint[Bars];
        private readonly SKPaint[] _glowPaints = new SKPaint[Bars];
        private readonly SKPaint[] _peakPaints = new SKPaint[Bars];
        private readonly SKPoint[] _segmentLines;

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

        public SpectrumView() : base(Layout.Spectrum)
        {
            for (int i = 0; i < Bars; i++)
            {
                float t = i / (float)(Bars - 1);
                _colors[i] = Sample(t);
                var col = _colors[i];
                _barPaints[i] = new SKPaint
                {
                    IsAntialias = false,
                    Style = SKPaintStyle.Fill,
                    Shader = SKShader.CreateLinearGradient(new SKPoint(0, PlotTop), new SKPoint(0, PlotBottom), new[] { Theme.Lighten(col, 0.18f), col, Theme.Darken(col, 0.32f) }, new[] { 0f, 0.45f, 1f }, SKShaderTileMode.Clamp),
                };
                _glowPaints[i] = Theme.Glow(col, 3.5f, 75);
                _peakPaints[i] = Theme.Fill(Theme.Lighten(col, 0.45f));
            }
            int lines = (int)((PlotBottom - PlotTop) / 6f);
            _segmentLines = new SKPoint[lines * 2];
            for (int i = 0; i < lines; i++)
            {
                float y = PlotBottom - 6f * (i + 1) + 0.5f;
                _segmentLines[i * 2] = new SKPoint(PlotLeft - 2, y);
                _segmentLines[i * 2 + 1] = new SKPoint(PlotRight + 2, y);
            }
        }

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

        private static float XForHz(float hz) => PlotLeft + (PlotRight - PlotLeft) * MathF.Log(hz / 20f) / MathF.Log(1000f);

        public override void DrawStatic(SKCanvas c, FrameContext f)
        {
            Theme.Panel(c, Bounds, "SPECTRUM ANALYSER");
            Theme.Screen(c, Screen, Theme.ScreenBg);

            // LED-matrix background grid
            using (var minor = Theme.Stroke(new SKColor(0x0c, 0x14, 0x10), 1f))
            {
                for (float y = PlotBottom; y >= PlotTop; y -= 6f) c.DrawLine(PlotLeft - 6, y + 0.5f, PlotRight + 6, y + 0.5f, minor);
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
                        c.DrawLine(x, PlotTop - 6, x, PlotBottom + 4, hz == 20 || m == 1 ? vmajor : vminor);
                    }
                foreach (float hz in new[] { 100f, 1000f, 10000f })
                {
                    float x = MathF.Round(XForHz(hz)) + 0.5f;
                    c.DrawLine(x, PlotTop - 6, x, PlotBottom + 4, vmajor);
                }
            }
            using (var major = Theme.Stroke(new SKColor(0x1c, 0x2c, 0x20), 1f))
                for (int db = 0; db >= -60; db -= 10)
                {
                    float y = MathF.Round(YForDb(db)) + 0.5f;
                    c.DrawLine(PlotLeft - 6, y, PlotRight + 6, y, major);
                }

            // scale labels
            for (int db = 0; db >= -60; db -= 10)
                Theme.Text(c, db.ToString(), 372f, YForDb(db) + 4.5f, 13f, new SKColor(0xd0, 0xd0, 0xd0), Theme.Sans, SKTextAlign.Right);
            (float hz, string label)[] marks = { (20, "20"), (50, "50"), (100, "100"), (200, "200"), (500, "500"), (1000, "1K"), (2000, "2K"), (5000, "5K"), (10000, "10K"), (20000, "20K") };
            foreach (var (hz, label) in marks)
                Theme.Text(c, label, XForHz(hz), 357f, 13f, new SKColor(0xd0, 0xd0, 0xd0), Theme.Sans, SKTextAlign.Center);
            Theme.Text(c, "Hz", 1078f, 357f, 13f, new SKColor(0xd0, 0xd0, 0xd0), Theme.Sans, SKTextAlign.Center);
        }

        private static float YForDb(float db) => PlotBottom - (db - Analyzer.MinDb) / (0 - Analyzer.MinDb) * (PlotBottom - PlotTop);

        public override void Draw(SKCanvas c, FrameContext f)
        {
            var a = f.Analyzer;
            c.Save();
            c.ClipRect(new SKRect(PlotLeft - 4, PlotTop - 12, PlotRight + 4, PlotBottom + 1));
            for (int i = 0; i < Bars; i++)
            {
                float db = Math.Clamp(a.Bands[i], Analyzer.MinDb, 6f);
                float top = YForDb(db);
                if (top < PlotTop - 10) top = PlotTop - 10;
                float x = PlotLeft + i * _pitch + (_pitch - BarW) / 2f;
                if (PlotBottom - top < 1.5f) continue;
                var r = new SKRect(x, top, x + BarW, PlotBottom);
                c.DrawRect(SKRect.Inflate(r, 2, 1), _glowPaints[i]);
                c.DrawRect(r, _barPaints[i]);
            }
            using (var cut = Theme.Stroke(Theme.ScreenBg, 1.3f)) c.DrawPoints(SKPointMode.Lines, _segmentLines, cut);
            if (f.State.PeakHoldEnabled)
            {
                for (int i = 0; i < Bars; i++)
                {
                    float pk = Math.Clamp(a.BandPeaks[i], Analyzer.MinDb, 6f);
                    if (pk <= Analyzer.MinDb + 0.5f) continue;
                    float y = YForDb(pk);
                    float x = PlotLeft + i * _pitch + (_pitch - BarW) / 2f;
                    c.DrawRect(new SKRect(x, y - 1.5f, x + BarW, y + 0.5f), _peakPaints[i]);
                }
            }
            c.Restore();

            string mode = f.State.SpectrumChannel switch { SpectrumChannel.Left => "L", SpectrumChannel.Right => "R", _ => "L+R" };
            bool hover = f.Hover(ModeRect);
            Theme.Text(c, mode, 1078f, 131f, 13.5f, hover ? new SKColor(0xff, 0x70, 0x70) : new SKColor(0xff, 0x3b, 0x3b), Theme.SansBold, SKTextAlign.Right, 1f);
        }

        public override bool OnMouseDown(SKPoint p, FrameContext f)
        {
            if (ModeRect.Contains(p))
            {
                f.State.SpectrumChannel = (SpectrumChannel)(((int)f.State.SpectrumChannel + 1) % 3);
                f.PersistSettings?.Invoke();
                return true;
            }
            return false;
        }
    }
}
