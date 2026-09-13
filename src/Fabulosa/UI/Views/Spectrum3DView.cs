using System;
using Fabulosa.Audio;
using SkiaSharp;

namespace Fabulosa.UI.Views
{
    /// <summary>Waterfall: 32 time slices of the 64-band spectrum drawn as an oblique wireframe, coloured by level.</summary>
    public sealed class Spectrum3DView : View
    {
        private readonly SKRect _screen;
        private readonly SKPoint _origin;     // front-left corner (20 Hz, newest)
        private readonly SKPoint _freqAxis;   // 20 Hz -> 20 kHz
        private readonly SKPoint _timeAxis;   // newest -> oldest
        private readonly float _heightPx;     // -60 dB -> 0 dB
        private readonly float _slope;

        // Colour by level; the floor (-60 dB) fades to almost nothing so silence reads as a dark grid, not a sheet.
        private static readonly SKColor[] Rainbow =
        {
            new SKColor(0x3a, 0x12, 0x8a, 40), new SKColor(0x6a, 0x1c, 0xff), new SKColor(0x1f, 0x5b, 0xff), new SKColor(0x16, 0xe6, 0xff),
            new SKColor(0x37, 0xff, 0x5a), new SKColor(0xf4, 0xff, 0x2a), new SKColor(0xff, 0x8a, 0x1e), new SKColor(0xff, 0x2a, 0x2a),
        };
        private static readonly float[] RainbowPos = { 0f, 0.07f, 0.2f, 0.37f, 0.53f, 0.68f, 0.84f, 1f };

        private readonly SKPoint[] _ridge = new SKPoint[Analyzer.BandCount];
        private readonly SKPoint[] _prev = new SKPoint[Analyzer.BandCount];
        private readonly SKPoint[] _connectors = new SKPoint[Analyzer.BandCount * 2];
        private readonly SKPath _fill = new SKPath();
        private readonly SKPath _line = new SKPath();
        // The occlusion "curtain" under each ridge is the biggest fill on screen; its edge is hidden by the ridge line, so it skips anti-aliasing.
        private readonly SKPaint _fillPaint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill, Color = new SKColor(0x04, 0x07, 0x0b, 236) };
        private readonly SKPaint _linePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.05f, StrokeJoin = SKStrokeJoin.Bevel };
        private readonly SKPaint _connPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.8f };

        public Spectrum3DView() : base(Layout.Spectrum3D)
        {
            _screen = Layout.ScreenOf(Bounds);
            float w = _screen.Width, h = _screen.Height;
            _origin = new SKPoint(_screen.Left + 0.024f * w, _screen.Bottom - 0.30f * h);
            _freqAxis = new SKPoint(0.68f * w, 0.22f * h);
            _timeAxis = new SKPoint(0.21f * w, -0.41f * h);
            _heightPx = 0.48f * h;
            _slope = _freqAxis.Y / _freqAxis.X;
        }

        private SKPoint Project(float freqT, float timeT, float heightT)
        {
            return new SKPoint(
                _origin.X + _freqAxis.X * freqT + _timeAxis.X * timeT,
                _origin.Y + _freqAxis.Y * freqT + _timeAxis.Y * timeT - _heightPx * heightT);
        }

        private static float FreqT(float hz) => MathF.Log(hz / 20f) / MathF.Log(1000f);

        public override void DrawStatic(SKCanvas c, FrameContext f)
        {
            Theme.Panel(c, Bounds, "3D SPECTRUM");
            Theme.Screen(c, _screen, new SKColor(0x03, 0x06, 0x08));

            using (var grid = Theme.Stroke(new SKColor(0x12, 0x1c, 0x22), 1f))
            {
                foreach (float hz in new[] { 20f, 50f, 100f, 200f, 500f, 1000f, 2000f, 5000f, 10000f, 20000f })
                {
                    var p0 = Project(FreqT(hz), 0, 0);
                    var p1 = Project(FreqT(hz), 1, 0);
                    c.DrawLine(p0, p1, grid);
                }
                for (int i = 0; i <= 4; i++)
                {
                    var p0 = Project(0, i / 4f, 0);
                    var p1 = Project(1, i / 4f, 0);
                    c.DrawLine(p0, p1, grid);
                }
            }
            using (var axis = Theme.Stroke(new SKColor(0x4e, 0x58, 0x5c), 1.2f))
            {
                c.DrawLine(Project(0, 0, 0), Project(1, 0, 0), axis);
                c.DrawLine(Project(1, 0, 0), Project(1, 1, 0), axis);
                var corner = Project(1, 1, 0);
                c.DrawLine(corner, Project(1, 1, 1), axis);
                for (int db = 0; db >= -60; db -= 20)
                {
                    float ht = (db + 60) / 60f;
                    var p = Project(1, 1, ht);
                    c.DrawLine(p.X, p.Y, p.X + 5, p.Y, axis);
                }
            }
            var dbTop = Project(1, 1, 1);
            Theme.Text(c, "dB", dbTop.X + 10, dbTop.Y + 4, 12.5f, new SKColor(0xd0, 0xd0, 0xd0), Theme.Sans);
            for (int db = -20; db >= -60; db -= 20)
            {
                var p = Project(1, 1, (db + 60) / 60f);
                Theme.Text(c, db.ToString(), p.X + 10, p.Y + 4, 12.5f, new SKColor(0xd0, 0xd0, 0xd0), Theme.Sans);
            }
            (float hz, string label)[] marks = { (20, "20"), (50, "50"), (100, "100"), (200, "200"), (500, "500"), (1000, "1K"), (2000, "2K"), (5000, "5K"), (10000, "10K"), (20000, "20K") };
            foreach (var (hz, label) in marks)
            {
                var p = Project(FreqT(hz), 0, 0);
                Theme.Text(c, label, p.X, p.Y + 15, 12.5f, new SKColor(0xd0, 0xd0, 0xd0), Theme.Sans, SKTextAlign.Center);
            }
            var end = Project(1.11f, 0, 0);
            Theme.Text(c, "Hz", end.X, end.Y + 15, 12.5f, new SKColor(0xd0, 0xd0, 0xd0), Theme.Sans, SKTextAlign.Center);
            Theme.Text(c, "TIME  →", _screen.Right - 48f, _screen.Bottom - 16f, 11.5f, new SKColor(0xd0, 0xd0, 0xd0), Theme.SansBold, SKTextAlign.Right, 1.5f);
        }

        public override void Draw(SKCanvas c, FrameContext f)
        {
            var a = f.Analyzer;
            int rows = Analyzer.HistoryRows;
            int bands = Analyzer.BandCount;
            bool mesh = f.State.MeshLines;
            var skew = SKMatrix.CreateSkew(0, _slope);

            c.Save();
            c.ClipRoundRect(new SKRoundRect(_screen, 4, 4), SKClipOperation.Intersect, true);
            bool havePrev = false;
            for (int k = rows - 1; k >= 0; k--)
            {
                float timeT = k / (float)(rows - 1);
                var row = a.History[(a.HistoryHead + k) % rows];
                for (int b = 0; b < bands; b++)
                {
                    float ht = Math.Clamp((row[b] - Analyzer.MinDb) / (0 - Analyzer.MinDb), 0f, 1.08f);
                    _ridge[b] = Project(b / (float)(bands - 1), timeT, ht);
                }
                var baseLeft = Project(0, timeT, 0);
                var baseRight = Project(1, timeT, 0);
                float ys = baseLeft.Y - _slope * baseLeft.X;   // baseline in skewed shader space
                using var shader = SKShader.CreateLinearGradient(new SKPoint(0, ys), new SKPoint(0, ys - _heightPx), Rainbow, RainbowPos, SKShaderTileMode.Clamp, skew);

                if (mesh && havePrev)
                {
                    int n = 0;
                    for (int b = 0; b < bands; b += 3) { _connectors[n++] = _prev[b]; _connectors[n++] = _ridge[b]; }
                    _connPaint.Shader = shader;
                    _connPaint.Color = new SKColor(255, 255, 255, 150);
                    c.DrawPoints(SKPointMode.Lines, new ReadOnlySpan<SKPoint>(_connectors, 0, n).ToArray(), _connPaint);
                }

                _fill.Reset();
                _fill.MoveTo(baseLeft);
                for (int b = 0; b < bands; b++) _fill.LineTo(_ridge[b]);
                _fill.LineTo(baseRight);
                _fill.Close();
                c.DrawPath(_fill, _fillPaint);

                _line.Reset();
                _line.MoveTo(_ridge[0]);
                for (int b = 1; b < bands; b++) _line.LineTo(_ridge[b]);
                _linePaint.Shader = shader;
                _linePaint.Color = SKColors.White;
                c.DrawPath(_line, _linePaint);

                Array.Copy(_ridge, _prev, bands);
                havePrev = true;
            }
            _linePaint.Shader = null;
            _connPaint.Shader = null;
            c.Restore();
        }
    }
}
