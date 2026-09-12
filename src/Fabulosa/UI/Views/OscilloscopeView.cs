using System;
using Fabulosa.Audio;
using SkiaSharp;

namespace Fabulosa.UI.Views
{
    /// <summary>Triggered oscilloscope, 5 ms per division over ten divisions.</summary>
    public sealed class OscilloscopeView : View
    {
        private static readonly SKRect Screen = new SKRect(251, 414, 689, 582);
        private readonly SKPoint[] _trace = new SKPoint[Analyzer.ScopePoints];
        private readonly SKPath _path = new SKPath();
        private float _agcPeak = 0.3f;
        private float _gain = 110f;

        public OscilloscopeView() : base(Layout.Scope) { }

        public override void DrawStatic(SKCanvas c, FrameContext f)
        {
            Theme.Panel(c, Bounds, "OSCILLOSCOPE");
            Theme.Screen(c, Screen, new SKColor(0x04, 0x12, 0x10));

            c.Save();
            c.ClipRoundRect(new SKRoundRect(Screen, 4, 4), SKClipOperation.Intersect, true);
            const int cols = 20, rows = 8;
            float cw = Screen.Width / cols, rh = Screen.Height / rows;
            using (var minor = Theme.Stroke(new SKColor(0x0d, 0x38, 0x32), 1f))
            using (var major = Theme.Stroke(new SKColor(0x15, 0x52, 0x4a), 1f))
            {
                for (int i = 1; i < cols; i++)
                {
                    float x = MathF.Round(Screen.Left + i * cw) + 0.5f;
                    c.DrawLine(x, Screen.Top, x, Screen.Bottom, i % 2 == 0 ? major : minor);
                }
                for (int j = 1; j < rows; j++)
                {
                    float y = MathF.Round(Screen.Top + j * rh) + 0.5f;
                    c.DrawLine(Screen.Left, y, Screen.Right, y, j % 2 == 0 ? major : minor);
                }
            }
            using (var centre = Theme.Stroke(new SKColor(0x1e, 0x74, 0x66), 1.2f))
                c.DrawLine(Screen.Left, Screen.MidY + 0.5f, Screen.Right, Screen.MidY + 0.5f, centre);
            // phosphor vignette
            using (var vig = new SKPaint { Shader = SKShader.CreateRadialGradient(new SKPoint(Screen.MidX, Screen.MidY), Screen.Width * 0.6f, new[] { new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 90) }, null, SKShaderTileMode.Clamp) })
                c.DrawRect(Screen, vig);
            c.Restore();

            float labelRight = Screen.Right - 6f;
            float w = Theme.Measure("5 ms/div", 11f, Theme.Sans);
            Theme.Text(c, "5 ms/div", labelRight, 431f, 11f, Theme.Cyan, Theme.Sans, SKTextAlign.Right);
            Theme.Text(c, "TIME", labelRight - w - 6f, 431f, 11f, new SKColor(0x8f, 0xa0, 0x9a), Theme.Sans, SKTextAlign.Right, 0.5f);
        }

        public override void Draw(SKCanvas c, FrameContext f)
        {
            var scope = f.Analyzer.Scope;
            int n = scope.Length;
            float cy = Screen.MidY;
            float limit = Screen.Height / 2 - 3;

            // gentle automatic gain so quiet and loud material both fill the screen
            float peakNow = 0f;
            for (int i = 0; i < n; i++) { float a = MathF.Abs(scope[i]); if (a > peakNow) peakNow = a; }
            _agcPeak = Math.Max(peakNow, _agcPeak * MathF.Exp(-f.Dt * 0.6f));
            float target = Math.Clamp(limit * 0.78f / Math.Max(_agcPeak, 0.02f), 90f, 900f);
            _gain += (target - _gain) * (target < _gain ? 0.3f : 0.08f);

            for (int i = 0; i < n; i++)
            {
                float x = Screen.Left + 2 + (Screen.Width - 4) * i / (n - 1);
                float y = cy - Math.Clamp(scope[i] * _gain, -limit, limit);
                _trace[i] = new SKPoint(x, y);
            }
            _path.Reset();
            _path.MoveTo(_trace[0]);
            for (int i = 1; i < n; i++) _path.LineTo(_trace[i]);

            c.Save();
            c.ClipRoundRect(new SKRoundRect(Screen, 4, 4), SKClipOperation.Intersect, true);
            using (var glow = Theme.GlowStroke(Theme.Cyan, 6f, 4f, 70)) c.DrawPath(_path, glow);
            using (var line = Theme.Stroke(new SKColor(0x22, 0xf2, 0xea), 2.2f)) { line.StrokeJoin = SKStrokeJoin.Round; line.StrokeCap = SKStrokeCap.Round; c.DrawPath(_path, line); }
            using (var core = Theme.Stroke(new SKColor(0xd8, 0xff, 0xfc, 140), 0.8f)) c.DrawPath(_path, core);
            c.Restore();
        }
    }
}
