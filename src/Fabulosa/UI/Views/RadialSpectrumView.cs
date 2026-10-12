using System;
using Fabulosa.Audio;
using Fabulosa.Model;
using SkiaSharp;

namespace Fabulosa.UI.Views
{
    /// <summary>
    /// A tilted ring of spokes, one per band: the right channel runs clockwise from the bass at the back to the
    /// treble at the front and the left channel comes back up the other side. Along each spoke the last second of
    /// its band flows outward as spikes. The ring pulses on the beat and turns slowly clockwise.
    /// </summary>
    public sealed class RadialSpectrumView : View
    {
        private const int Bands = Analyzer.BandCount;
        private const int Spokes = Bands * 2;
        private const float Tilt = 0.4f;              // vertical squash of the disc: 0 is edge-on, 1 straight down
        private const float Hole = 0.17f;             // radii as fractions of the ring radius
        private const float HistoryStart = 0.2f;
        private const float HistoryEnd = 0.68f;
        private const float SpikeHeight = 0.5f;       // tallest spike, in ring radii
        private const float PulseLift = 0.25f;        // spikes grow by this much on the beat
        private const float OuterRadius = 1.5f;       // flat spokes carry on past the ring and fade out
        private const float DegreesPerSecond = 6f;    // a turn a minute
        private static readonly SKColor Background = SKColors.Black;

        private SKRect _screen;
        private SKPoint _centre;
        private float _radius;
        private readonly SKColor[] _colours = new SKColor[Spokes];
        private readonly SKColor[] _lineColours = new SKColor[Spokes];
        private readonly float[] _depth = new float[Spokes];
        private readonly int[] _order = new int[Spokes];
        private readonly float[] _sin = new float[Spokes];
        private readonly float[] _cos = new float[Spokes];
        private readonly SKPath _spike = new SKPath();
        private readonly SKPaint _fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly SKPaint _line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.3f };
        private readonly SKPaint _outline = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, StrokeJoin = SKStrokeJoin.Bevel };
        private readonly SKPaint _edge = Theme.Stroke(new SKColor(0x2a, 0x34, 0x33, 190), 1f);
        private SKPaint _fade;
        private float _pulse;

        public RadialSpectrumView()
        {
            for (int k = 0; k < Spokes; k++)
            {
                // hue follows the angle round the ring: red at one o'clock, then yellow, green, cyan, blue and magenta clockwise
                float hue = (360f * (k + 0.5f) / Spokes + 330f) % 360f;
                _colours[k] = SKColor.FromHsv(hue, 95f, 100f);
                _lineColours[k] = SKColor.FromHsv(hue, 90f, 82f);
            }
        }

        public override Visualiser? PanelId => Visualiser.Radial;

        /// <summary>How far the ring has turned, in degrees clockwise seen from above.</summary>
        public float RotationDegrees { get; private set; }

        /// <summary>Current pulse: 1 on a beat, decaying between beats.</summary>
        public float Pulse => _pulse;

        protected override void OnBoundsChanged()
        {
            _screen = Layout.ScreenOf(Bounds);
            float above = Math.Max(Tilt, Tilt * HistoryEnd + SpikeHeight);   // highest point over the centre, per unit radius
            float usable = _screen.Height - 12f;
            _radius = Math.Max(10f, Math.Min((_screen.Width - 24f) / 2f, usable / (above + Tilt)));
            float spare = usable - (above + Tilt) * _radius;
            _centre = new SKPoint(_screen.MidX, _screen.Top + 6f + spare / 2f + above * _radius);
            _line.StrokeWidth = Math.Max(1f, _radius / 190f);

            // past the ring the flat spokes fade into the background, so the screen edge never cuts them off sharply
            _fade?.Dispose();
            _fade = new SKPaint
            {
                Shader = SKShader.CreateRadialGradient(_centre, OuterRadius * _radius,
                    new[] { Background.WithAlpha(0), Background.WithAlpha(0), Background },
                    new[] { 0f, 0.98f / OuterRadius, 1f }, SKShaderTileMode.Clamp,
                    SKMatrix.CreateScale(1f, Tilt, _centre.X, _centre.Y)),
            };
        }

        public override void DrawStatic(SKCanvas c, FrameContext f)
        {
            Theme.Panel(c, Bounds, "RADIAL SPECTRUM");
            Theme.Screen(c, _screen, Background);
        }

        public override void Draw(SKCanvas c, FrameContext f)
        {
            var a = f.Analyzer;
            var beat = a.Beat;
            RotationDegrees = (RotationDegrees + DegreesPerSecond * f.Dt) % 360f;

            // every drum hit pulses it, with the beat clock keeping time between hits
            float target = beat.LastBeatTime >= 0 ? beat.Pulse : 0f;
            if (beat.LastOnsetTime >= 0)
                target = Math.Max(target, (float)Math.Exp(-(beat.Now - beat.LastOnsetTime) / 0.1) * Math.Clamp(beat.LastOnsetStrength / 5f, 0f, 1f));
            _pulse += (target - _pulse) * (target > _pulse ? 0.85f : 0.12f);

            float radius = _radius * (1f + 0.035f * _pulse);
            float lift = SpikeHeight * radius * (1f + PulseLift * _pulse);
            float turn = RotationDegrees * MathF.PI / 180f;
            for (int k = 0; k < Spokes; k++)
            {
                float angle = 2f * MathF.PI * (k + 0.5f) / Spokes + turn;   // 0 is the far side of the ring, increasing clockwise
                _sin[k] = MathF.Sin(angle);
                _cos[k] = MathF.Cos(angle);
                _depth[k] = -_cos[k];
                _order[k] = k;
            }
            Array.Sort(_depth, _order);   // far spokes first, so nearer spikes cover them

            float cx = _centre.X, cy = _centre.Y;
            c.Save();
            c.ClipRoundRect(new SKRoundRect(_screen, 4, 4), SKClipOperation.Intersect, true);

            for (int k = 0; k < Spokes; k++)
            {
                float dx = _sin[k] * radius, dy = -_cos[k] * Tilt * radius;
                _line.Color = Theme.Lighten(_lineColours[k], 0.2f * _pulse);
                c.DrawLine(cx + dx * Hole, cy + dy * Hole, cx + dx * OuterRadius, cy + dy * OuterRadius, _line);
            }
            c.DrawRect(_screen, _fade);

            // a short trail keeps the eye on what is happening now; the ripples then travel out and die quickly
            int rows = Math.Clamp((int)MathF.Round(f.State.RadialTrailSeconds / Analyzer.RadialRowSeconds), 2, Analyzer.RadialRows);
            bool lines = f.State.RadialLines;
            float step = (HistoryEnd - HistoryStart) / (rows - 1);
            float phase = a.RadialPhase;
            foreach (int k in _order)
            {
                float dx = _sin[k] * radius, dy = -_cos[k] * Tilt * radius;
                int column = k < Bands ? Bands + k : Spokes - 1 - k;   // right channel down one side, left back up the other
                _spike.Reset();
                _spike.MoveTo(cx + dx * Hole, cy + dy * Hole);
                for (int j = 0; j < rows; j++)
                {
                    float age = (j + phase) / rows;   // spikes shrink as they travel outward
                    float r = Math.Min(HistoryEnd, HistoryStart + (j + phase) * step);
                    float h = a.RadialHistory[(a.RadialHead + j) % Analyzer.RadialRows][column] * lift * (1f - age);
                    _spike.LineTo(cx + dx * r, cy + dy * r - h);
                }
                if (lines)
                {
                    _outline.Color = Theme.Lighten(_colours[k], 0.2f * _pulse);
                    c.DrawPath(_spike, _outline);
                }
                else
                {
                    _spike.LineTo(cx + dx * HistoryEnd, cy + dy * HistoryEnd);
                    _spike.Close();
                    _fill.Color = Theme.Lighten(_colours[k], 0.15f * _pulse).WithAlpha(245);
                    c.DrawPath(_spike, _fill);
                }
            }
            c.Restore();
            c.DrawRoundRect(_screen, 4, 4, _edge);
        }
    }
}
