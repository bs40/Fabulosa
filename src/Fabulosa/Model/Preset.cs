using System;
using System.Collections.Generic;

namespace Fabulosa.Model
{
    /// <summary>A named snapshot of the EQ, effects and knob settings.</summary>
    public sealed class Preset
    {
        public string Name { get; set; } = "Untitled";
        public float[] EqGains { get; set; } = new float[AppState.EqBandCount];
        public bool[] Effects { get; set; } = new bool[AppState.EffectCount];
        public float Loudness { get; set; } = 0.5f;
        public float Balance { get; set; } = 0f;
        public float StereoWidth { get; set; } = 1f;
        public float Master { get; set; } = 0.8f;

        public Preset Clone()
        {
            return new Preset
            {
                Name = Name,
                EqGains = (float[])EqGains.Clone(),
                Effects = (bool[])Effects.Clone(),
                Loudness = Loudness,
                Balance = Balance,
                StereoWidth = StereoWidth,
                Master = Master,
            };
        }

        public static Preset Make(string name, float[] eq, bool[] fx = null, float loudness = 0.5f, float balance = 0f, float width = 1f, float master = 0.8f)
        {
            var p = new Preset { Name = name, Loudness = loudness, Balance = balance, StereoWidth = width, Master = master };
            Array.Copy(eq, p.EqGains, Math.Min(eq.Length, p.EqGains.Length));
            if (fx != null) Array.Copy(fx, p.Effects, Math.Min(fx.Length, p.Effects.Length));
            return p;
        }

        /// <summary>The ten factory presets shipped with the app (the mock-up's list).</summary>
        public static List<Preset> Defaults()
        {
            // Bands: 20 31 50 80 125 200 315 500 800 1K 1.6K 2.5K 4K 6.3K 10K 16K 20K
            return new List<Preset>
            {
                Make("Retro Boost",   new float[] { 3, 3, 1, -1, -3, -4, -5, -3, -2, 0, 1, 1, 1, -1, 0, 4, 5 }, new[] { true, true, false, false, false, false }),
                Make("Deep & Warm",   new float[] { 6, 6, 5, 4, 3, 1, 0, 0, -1, -1, -2, -2, -3, -3, -4, -5, -6 }, new[] { true, false, false, false, false, false }, 0.6f),
                Make("Crystal Clear", new float[] { -2, -2, -1, 0, 0, 0, 0, 1, 1, 2, 3, 4, 5, 5, 5, 4, 3 }, null, 0.5f, 0f, 1.2f),
                Make("Vinyl Vibes",   new float[] { 2, 3, 2, 1, 0, 0, 1, 1, 0, 0, -1, -1, -2, -3, -5, -8, -10 }, new[] { false, false, false, false, false, true }, 0.55f, 0f, 0.8f),
                Make("Night Drive",   new float[] { 5, 5, 4, 2, 0, -1, -2, -2, -1, 0, 1, 2, 3, 3, 2, 1, 0 }, new[] { true, true, true, false, false, false }, 0.65f, 0f, 1.4f),
                Make("Vocal Lift",    new float[] { -3, -3, -2, -1, 0, 1, 2, 3, 4, 4, 3, 2, 1, 0, 0, -1, -2 }, new[] { true, false, false, false, false, false }),
                Make("Drum Punch",    new float[] { 2, 4, 6, 5, 2, -1, -2, -1, 0, 1, 2, 3, 2, 1, 1, 0, 0 }, new[] { false, false, false, false, false, false }, 0.7f),
                Make("Chill Out",     new float[] { 3, 3, 2, 1, 0, -1, -2, -2, -2, -1, 0, 0, 0, -1, -1, -2, -3 }, new[] { true, false, true, false, false, false }, 0.45f, 0f, 1.3f),
                Make("80s Synth",     new float[] { 1, 2, 3, 3, 1, 0, -1, 0, 2, 3, 4, 4, 4, 3, 3, 3, 2 }, new[] { true, true, true, false, true, false }, 0.6f, 0f, 1.6f),
                Make("Bass Monster",  new float[] { 10, 11, 12, 10, 7, 4, 1, -1, -2, -2, -2, -2, -2, -2, -2, -3, -3 }, new[] { false, false, false, false, false, true }, 0.8f),
            };
        }
    }
}
