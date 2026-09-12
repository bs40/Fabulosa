using Fabulosa.Model;
using SkiaSharp;

namespace Fabulosa.UI.Views
{
    /// <summary>Six latching effect switches with red LED indicators.</summary>
    public sealed class EffectsView : View
    {
        private const float FirstY = 640f, Pitch = 33.5f, H = 26f;
        private int _pressed = -1;

        public EffectsView() : base(Layout.Effects) { }

        private static SKRect ButtonRect(int i) => new SKRect(1188, FirstY + i * Pitch, 1332, FirstY + i * Pitch + H);

        public override void DrawStatic(SKCanvas c, FrameContext f)
        {
            Theme.Panel(c, Bounds, "EFFECTS");
        }

        public override void Draw(SKCanvas c, FrameContext f)
        {
            for (int i = 0; i < AppState.EffectCount; i++)
            {
                var r = ButtonRect(i);
                bool on = f.State.Effects[i];
                Theme.Button(c, r, f.Hover(r), _pressed == i && f.MouseDown, 3f);
                var led = new SKRect(r.Left + 7, r.Top + 4, r.Left + 25, r.Bottom - 4);
                if (on)
                {
                    using var glow = Theme.Glow(Theme.Red, 4f, 150);
                    c.DrawRoundRect(SKRect.Inflate(led, 1.5f, 1.5f), 3, 3, glow);
                    using var fill = Theme.VerticalGradient(led, new SKColor(0xff, 0x5a, 0x4a), new SKColor(0xe8, 0x1a, 0x1a));
                    c.DrawRoundRect(led, 2, 2, fill);
                    using var hi = Theme.Fill(new SKColor(0xff, 0xc8, 0xc0, 170));
                    c.DrawRoundRect(new SKRect(led.Left + 4, led.Top + 3, led.Right - 4, led.Top + 6), 1.5f, 1.5f, hi);
                }
                else
                {
                    using var fill = Theme.VerticalGradient(led, new SKColor(0x3a, 0x40, 0x41), new SKColor(0x24, 0x29, 0x2a));
                    c.DrawRoundRect(led, 2, 2, fill);
                    using var dot = Theme.Fill(new SKColor(0x6a, 0x72, 0x72));
                    c.DrawCircle(led.MidX, led.MidY, 3.2f, dot);
                }
                using (var edge = Theme.Stroke(new SKColor(0x08, 0x0a, 0x0a), 1f)) c.DrawRoundRect(led, 2, 2, edge);
                Theme.Text(c, AppState.EffectNames[i], r.Left + 41, r.MidY + 4.5f, 12.5f, on ? SKColors.White : new SKColor(0xd8, 0xd8, 0xd8), Theme.Sans, SKTextAlign.Left, 1.3f);
            }
        }

        public override bool OnMouseDown(SKPoint p, FrameContext f)
        {
            for (int i = 0; i < AppState.EffectCount; i++)
            {
                if (!ButtonRect(i).Contains(p)) continue;
                _pressed = i;
                f.State.Effects[i] = !f.State.Effects[i];
                f.State.EqDirty = true;
                f.PersistSettings?.Invoke();
                return true;
            }
            return false;
        }

        public override void OnMouseUp(SKPoint p, FrameContext f) { _pressed = -1; }
    }
}
