using System;
using Fabulosa.Model;
using SkiaSharp;

namespace Fabulosa.UI.Views
{
    /// <summary>A brushed-steel rotary control with a coloured arc, 270° of travel.</summary>
    public sealed class KnobView : View
    {
        private readonly string _title, _leftLabel, _rightLabel;
        private readonly SKColor[] _arcColors;
        private readonly Func<AppState, float> _get;
        private readonly Action<AppState, float> _set;
        private readonly float _default;
        private readonly SKPoint _centre;
        private readonly float _radius;
        private bool _dragging;
        private float _dragStartY, _dragStartValue;

        public KnobView(SKRect bounds, string title, string left, string right, SKColor[] arcColors, Func<AppState, float> get, Action<AppState, float> set, float defaultValue, float radius = 27f)
            : base(bounds)
        {
            _title = title; _leftLabel = left; _rightLabel = right; _arcColors = arcColors;
            _get = get; _set = set; _default = defaultValue;
            _radius = radius;
            // the upper pair of panels is 4 px taller; keep the knobs vertically centred in each
            _centre = new SKPoint(bounds.MidX, bounds.Top + (bounds.Height > 122 ? 70 : 66));
        }

        private float ArcRadius => _radius + 11f;

        public override void DrawStatic(SKCanvas c, FrameContext f)
        {
            Theme.Panel(c, Bounds);
            Theme.Text(c, _title, Bounds.MidX, Bounds.Top + 19f, 12f, new SKColor(0xdc, 0xdc, 0xdc), Theme.SansBold, SKTextAlign.Center, 1.6f);

            float ar = ArcRadius;
            var oval = new SKRect(_centre.X - ar, _centre.Y - ar, _centre.X + ar, _centre.Y + ar);
            using (var arc = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, StrokeCap = SKStrokeCap.Round, Shader = SKShader.CreateSweepGradient(_centre, _arcColors, null, SKShaderTileMode.Clamp, 135f, 405f) })
                c.DrawArc(oval, 135f, 270f, false, arc);
            using (var tick = Theme.Stroke(new SKColor(0xc8, 0xc8, 0xc8), 1.3f))
                for (int i = 0; i <= 4; i++)
                {
                    float ang = (135f + 67.5f * i) * MathF.PI / 180f;
                    float r0 = ar + 4, r1 = ar + (i == 2 ? 7 : 9);
                    c.DrawLine(_centre.X + r0 * MathF.Cos(ang), _centre.Y + r0 * MathF.Sin(ang), _centre.X + r1 * MathF.Cos(ang), _centre.Y + r1 * MathF.Sin(ang), tick);
                }
            float labelY = Bounds.Bottom - 12f;
            Theme.Text(c, _leftLabel, Bounds.Left + 34f, labelY, 11f, new SKColor(0xd0, 0xd0, 0xd0), Theme.Sans, SKTextAlign.Center, 1f);
            Theme.Text(c, _rightLabel, Bounds.Right - 34f, labelY, 11f, new SKColor(0xd0, 0xd0, 0xd0), Theme.Sans, SKTextAlign.Center, 1f);
        }

        public override void Draw(SKCanvas c, FrameContext f)
        {
            float t = Math.Clamp(_get(f.State), 0f, 1f);
            float r = _radius;
            bool hover = _dragging || f.Hover(new SKRect(_centre.X - r - 4, _centre.Y - r - 4, _centre.X + r + 4, _centre.Y + r + 4));

            using (var shadow = Theme.Glow(SKColors.Black, 4f, 170)) c.DrawCircle(_centre.X + 1, _centre.Y + 3, r + 2, shadow);
            using (var ring = new SKPaint { IsAntialias = true, Shader = SKShader.CreateLinearGradient(new SKPoint(_centre.X, _centre.Y - r - 3), new SKPoint(_centre.X, _centre.Y + r + 3), new[] { new SKColor(0x3a, 0x3a, 0x3a), new SKColor(0x0e, 0x0e, 0x0e) }, null, SKShaderTileMode.Clamp) })
                c.DrawCircle(_centre, r + 3, ring);
            using (var body = new SKPaint { IsAntialias = true, Shader = SKShader.CreateLinearGradient(new SKPoint(_centre.X - r, _centre.Y - r), new SKPoint(_centre.X + r, _centre.Y + r), new[] { hover ? new SKColor(0xc4, 0xc4, 0xc4) : new SKColor(0xb4, 0xb4, 0xb4), new SKColor(0x7c, 0x7c, 0x7c), new SKColor(0x46, 0x46, 0x46) }, new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp) })
                c.DrawCircle(_centre, r, body);
            // brushed sweep
            using (var brush = new SKPaint { IsAntialias = true, Shader = SKShader.CreateSweepGradient(_centre, new[] { new SKColor(255, 255, 255, 0), new SKColor(255, 255, 255, 40), new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 50), new SKColor(255, 255, 255, 0), new SKColor(255, 255, 255, 36), new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 44), new SKColor(255, 255, 255, 0) }, null) })
                c.DrawCircle(_centre, r, brush);
            using (var cap = new SKPaint { IsAntialias = true, Shader = SKShader.CreateRadialGradient(new SKPoint(_centre.X - 3, _centre.Y - 3), r * 0.5f, new[] { new SKColor(0xa0, 0xa0, 0xa0), new SKColor(0x60, 0x60, 0x60) }, null, SKShaderTileMode.Clamp) })
                c.DrawCircle(_centre, r * 0.36f, cap);
            using (var edge = Theme.Stroke(new SKColor(0x20, 0x20, 0x20), 1f)) c.DrawCircle(_centre, r, edge);

            float ang = (135f + 270f * t) * MathF.PI / 180f;
            float px0 = _centre.X + (r * 0.42f) * MathF.Cos(ang), py0 = _centre.Y + (r * 0.42f) * MathF.Sin(ang);
            float px1 = _centre.X + (r - 3f) * MathF.Cos(ang), py1 = _centre.Y + (r - 3f) * MathF.Sin(ang);
            using (var glow = Theme.GlowStroke(Theme.Red, 4f, 2.5f, 150)) c.DrawLine(px0, py0, px1, py1, glow);
            using (var pointer = Theme.Stroke(new SKColor(0xff, 0x2e, 0x2e), 3f)) { pointer.StrokeCap = SKStrokeCap.Round; c.DrawLine(px0, py0, px1, py1, pointer); }
        }

        public override bool OnMouseDown(SKPoint p, FrameContext f)
        {
            float r = _radius + 8;
            if (SKPoint.Distance(p, _centre) > r) return false;
            _dragging = true;
            _dragStartY = p.Y;
            _dragStartValue = _get(f.State);
            return true;
        }

        public override void OnMouseMove(SKPoint p, FrameContext f)
        {
            if (!_dragging) return;
            float v = Math.Clamp(_dragStartValue + (_dragStartY - p.Y) / 160f, 0f, 1f);
            _set(f.State, v);
            f.State.EqDirty = true;
        }

        public override void OnMouseUp(SKPoint p, FrameContext f)
        {
            if (_dragging) f.PersistSettings?.Invoke();
            _dragging = false;
        }

        public override bool OnDoubleClick(SKPoint p, FrameContext f)
        {
            if (SKPoint.Distance(p, _centre) > _radius + 8) return false;
            _set(f.State, _default);
            _dragging = false;
            f.State.EqDirty = true;
            f.PersistSettings?.Invoke();
            return true;
        }

        public override bool OnWheel(SKPoint p, int delta, FrameContext f)
        {
            if (!Bounds.Contains(p)) return false;
            _set(f.State, Math.Clamp(_get(f.State) + Math.Sign(delta) * 0.025f, 0f, 1f));
            f.State.EqDirty = true;
            f.PersistSettings?.Invoke();
            return true;
        }
    }
}
