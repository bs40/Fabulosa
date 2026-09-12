using System;
using System.Collections.Generic;
using Fabulosa.Audio;

namespace Fabulosa.Model
{
    /// <summary>
    /// Everything the user can change: EQ, effects, knobs, visualisation options and the
    /// preset list. Plain properties; the renderer reads them every frame.
    /// </summary>
    public sealed class AppState
    {
        public const int EqBandCount = 17;
        public const int EffectCount = 6;

        public static readonly float[] EqFrequencies = { 20, 31, 50, 80, 125, 200, 315, 500, 800, 1000, 1600, 2500, 4000, 6300, 10000, 16000, 20000 };
        public static readonly string[] EqLabels = { "20", "31", "50", "80", "125", "200", "315", "500", "800", "1K", "1.6K", "2.5K", "4K", "6.3K", "10K", "16K", "20K" };
        public static readonly string[] EffectNames = { "REVERB", "CHORUS", "DELAY", "FLANGER", "PHASER", "DISTORTION" };

        private static readonly float[] EqLogFreq = BuildLog(EqFrequencies);
        private static float[] BuildLog(float[] f) { var a = new float[f.Length]; for (int i = 0; i < f.Length; i++) a[i] = MathF.Log(f[i]); return a; }

        // ---- sound shaping (visual only: the app listens, it is not in the audio path) ----
        public float[] EqGains { get; } = new float[EqBandCount];          // dB, -12..+12
        public bool[] Effects { get; } = new bool[EffectCount];
        public float Loudness { get; set; } = 0.5f;                          // 0..1  -> spectrum display gain -12..+12 dB
        public float Balance { get; set; } = 0f;                             // -1..1 -> meter weighting
        public float StereoWidth { get; set; } = 1f;                         // 0..2  -> phase scope side gain
        public float Master { get; set; } = 0.8f;                            // 0..1  -> analysis gain (-inf..+6 dB, 0 dB at 0.8)

        // ---- visualisation options ----
        public SpectrumChannel SpectrumChannel { get; set; } = SpectrumChannel.Both;
        public float PeakHoldSeconds { get; set; } = 0.8f;
        public bool PeakHoldEnabled { get; set; } = true;
        public float SpectrumAttack { get; set; } = 0.025f;
        public float SpectrumReleaseDbPerSec { get; set; } = 42f;
        public int FftSize { get; set; } = 8192;
        public bool MeshLines { get; set; } = true;
        public bool AlwaysOnTop { get; set; } = false;
        public bool ShowFps { get; set; } = false;

        // ---- presets ----
        public List<Preset> Presets { get; set; } = Preset.Defaults();
        public int SelectedPreset { get; set; } = 0;

        public bool EqDirty { get; set; }

        public float LoudnessGainDb => (Loudness - 0.5f) * 24f;

        public float MasterGainDb
        {
            get
            {
                float t = Master;
                if (t <= 0.001f) return -120f;
                if (t >= 0.8f) return (t - 0.8f) * 30f;               // 0 dB .. +6 dB
                float u = t / 0.8f;                                    // 0..1
                return -60f * MathF.Pow(1f - u, 1.6f);                // -60 dB .. 0 dB
            }
        }

        public float MasterGainLinear => MathF.Pow(10f, MasterGainDb / 20f);

        /// <summary>Interpolated EQ curve gain at an arbitrary frequency (dB).</summary>
        public float EqGainAt(float hz)
        {
            if (hz <= EqFrequencies[0]) return EqGains[0];
            if (hz >= EqFrequencies[EqBandCount - 1]) return EqGains[EqBandCount - 1];
            float lf = MathF.Log(hz);
            int i = 0;
            while (i < EqBandCount - 2 && EqLogFreq[i + 1] < lf) i++;
            float t = (lf - EqLogFreq[i]) / (EqLogFreq[i + 1] - EqLogFreq[i]);
            // smoothstep between bands for a rounder curve
            t = t * t * (3f - 2f * t);
            return EqGains[i] + (EqGains[i + 1] - EqGains[i]) * t;
        }

        public void ApplyPreset(Preset p)
        {
            if (p == null) return;
            Array.Copy(p.EqGains, EqGains, EqBandCount);
            Array.Copy(p.Effects, Effects, EffectCount);
            Loudness = p.Loudness;
            Balance = p.Balance;
            StereoWidth = p.StereoWidth;
            Master = p.Master;
            EqDirty = false;
        }

        public Preset CaptureAsPreset(string name)
        {
            return Preset.Make(name, EqGains, Effects, Loudness, Balance, StereoWidth, Master);
        }

        /// <summary>True when the live controls still equal the given preset (nothing to save).</summary>
        public bool MatchesPreset(Preset p)
        {
            if (p == null) return false;
            for (int i = 0; i < EqBandCount; i++) if (MathF.Abs(EqGains[i] - p.EqGains[i]) > 0.01f) return false;
            for (int i = 0; i < EffectCount; i++) if (Effects[i] != p.Effects[i]) return false;
            return MathF.Abs(Loudness - p.Loudness) < 0.005f && MathF.Abs(Balance - p.Balance) < 0.005f
                && MathF.Abs(StereoWidth - p.StereoWidth) < 0.005f && MathF.Abs(Master - p.Master) < 0.005f;
        }

        public void SelectPreset(int index)
        {
            if (index < 0 || index >= Presets.Count) return;
            SelectedPreset = index;
            ApplyPreset(Presets[index]);
        }

        public void ResetEq()
        {
            Array.Clear(EqGains, 0, EqGains.Length);
            EqDirty = true;
        }
    }
}
