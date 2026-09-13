using System;
using Fabulosa.Model;

namespace Fabulosa.Audio
{
    public enum SpectrumChannel { Both, Left, Right }

    /// <summary>
    /// Per-frame analysis of the captured audio: log-spaced band spectrum with smoothing and
    /// peak hold, VU ballistics, peak/RMS meters, oscilloscope trace, phase-scope points, the
    /// 3D waterfall history and the beat tracker. Everything is computed on the UI thread from
    /// a snapshot of the ring buffer, so views can read the arrays directly.
    /// </summary>
    public sealed class Analyzer
    {
        public const int BandCount = 64;
        public const int HistoryRows = 32;
        public const int ScopePoints = 440;
        public const int PhasePoints = 1000;
        public const float MinDb = -60f;
        /// <summary>Band levels are power sums, which sit well below a full-scale sine for real music; lift them so a loud mix reaches the top of the scale.</summary>
        public const float DisplayOffsetDb = 10f;
        /// <summary>0 VU reference in dBFS (sine). -14 dBFS suits streaming-normalised material.</summary>
        public const float VuReferenceDbfs = -14f;

        private readonly StereoRingBuffer _buffer;
        private readonly AppState _state;
        private Fft _fft;
        private float[] _left = new float[16384];
        private float[] _right = new float[16384];
        private float[] _mono = new float[16384];
        private float[] _power = new float[8192];
        private readonly float[] _bandLo = new float[BandCount];
        private readonly float[] _bandHi = new float[BandCount];
        private readonly float[] _bandCentre = new float[BandCount];
        private readonly float[] _peakHoldTimer = new float[BandCount];
        private double _historyClock;
        private long _lastTotalWritten;
        private float _peakHoldTimerL, _peakHoldTimerR;
        private float _phaseScale = 100f;
        private float _vuVelL, _vuVelR;
        private float _peakLinL, _peakLinR;

        public Analyzer(StereoRingBuffer buffer, AppState state)
        {
            _buffer = buffer;
            _state = state;
            History = new float[HistoryRows][];
            for (int i = 0; i < HistoryRows; i++) { History[i] = new float[BandCount]; Array.Fill(History[i], MinDb); }
            Array.Fill(Bands, MinDb);
            Array.Fill(BandPeaks, MinDb);
            ConfigureBands();
        }

        public int SampleRate { get; set; } = 48000;
        public int FftSize { get; set; } = 8192;
        public BeatTracker Beat { get; } = new BeatTracker();

        // ---- outputs -------------------------------------------------------------------
        /// <summary>Smoothed band levels in dB (MinDb..0+).</summary>
        public float[] Bands { get; } = new float[BandCount];
        /// <summary>Peak-hold levels per band in dB.</summary>
        public float[] BandPeaks { get; } = new float[BandCount];
        /// <summary>Waterfall rows, index 0 = newest.</summary>
        public float[][] History { get; }
        public int HistoryHead { get; private set; }
        /// <summary>VU deflection (linear, 1.0 = 0 VU) per channel after ballistics.</summary>
        public float VuLeft { get; private set; }
        public float VuRight { get; private set; }
        /// <summary>Sample peak (dBFS) with hold + decay, and short-term RMS (dBFS, sine-referenced).</summary>
        public float PeakDbL { get; private set; } = -90f;
        public float PeakDbR { get; private set; } = -90f;
        public float RmsDbL { get; private set; } = -90f;
        public float RmsDbR { get; private set; } = -90f;
        public float PeakDb => Math.Max(PeakDbL, PeakDbR);
        public float RmsDb => Math.Max(RmsDbL, RmsDbR);
        public float[] Scope { get; } = new float[ScopePoints];
        public float[] PhaseX { get; } = new float[PhasePoints];
        public float[] PhaseY { get; } = new float[PhasePoints];
        public int PhaseCount { get; private set; }
        /// <summary>Covariance ellipse of the phase-scope cloud in normalised units.</summary>
        public float PhaseEllipseA { get; private set; }
        public float PhaseEllipseB { get; private set; }
        public float PhaseEllipseAngle { get; private set; }
        public bool SignalPresent { get; private set; }

        public float BandCentreHz(int i) => _bandCentre[i];

        private void ConfigureBands()
        {
            const double fMin = 20, fMax = 20000;
            for (int i = 0; i < BandCount; i++)
            {
                _bandLo[i] = (float)(fMin * Math.Pow(fMax / fMin, (double)i / BandCount));
                _bandHi[i] = (float)(fMin * Math.Pow(fMax / fMin, (double)(i + 1) / BandCount));
                _bandCentre[i] = MathF.Sqrt(_bandLo[i] * _bandHi[i]);
            }
        }

        public void Update(float dt)
        {
            if (dt <= 0 || dt > 0.25f) dt = 1f / 60f;
            int sr = Math.Max(8000, SampleRate);
            int n = FftSize;
            if (_fft == null || _fft.Size != n) { _fft = new Fft(n); _power = new float[n / 2]; }
            int need = Math.Max(n, (int)(sr * 0.4f));
            need = Math.Min(need, _buffer.Capacity);
            if (_left.Length < need) { _left = new float[need]; _right = new float[need]; _mono = new float[need]; }
            _buffer.CopyLatest(_left, _right, need);

            long total = _buffer.TotalWritten;
            int freshFrames = (int)Math.Clamp(total - _lastTotalWritten, 0, need);
            _lastTotalWritten = total;

            // --- beat tracking consumes exactly the samples that arrived since the last frame ---
            if (Beat.SampleRate != sr) Beat.Reset(sr);
            if (freshFrames > 0) Beat.Process(_left, _right, need - freshFrames, freshFrames);

            // --- meters (use the newest 300 ms / 100 ms) ---
            int vuWindow = Math.Min(need, (int)(sr * 0.3f));
            int rmsWindow = Math.Min(need, (int)(sr * 0.1f));
            int peakWindow = Math.Max(Math.Min(need, freshFrames), Math.Min(need, sr / 50));
            double sumL = 0, sumR = 0, sumL2 = 0, sumR2 = 0;
            float pkL = 0, pkR = 0;
            int startVu = need - vuWindow, startRms = need - rmsWindow, startPk = need - peakWindow;
            for (int i = startVu; i < need; i++)
            {
                float l = _left[i], r = _right[i];
                sumL += l * l; sumR += r * r;
                if (i >= startRms) { sumL2 += l * l; sumR2 += r * r; }
                if (i >= startPk)
                {
                    float al = MathF.Abs(l), ar = MathF.Abs(r);
                    if (al > pkL) pkL = al;
                    if (ar > pkR) pkR = ar;
                }
            }
            float rmsVuL = (float)Math.Sqrt(sumL / Math.Max(1, vuWindow));
            float rmsVuR = (float)Math.Sqrt(sumR / Math.Max(1, vuWindow));
            float rmsL = (float)Math.Sqrt(sumL2 / Math.Max(1, rmsWindow));
            float rmsR = (float)Math.Sqrt(sumR2 / Math.Max(1, rmsWindow));

            // 0 VU reference: a sine at VuReferenceDbfs (rms = 10^(ref/20) / sqrt2).
            float vuRefRms = MathF.Pow(10f, VuReferenceDbfs / 20f) / 1.41421356f;
            UpdateVu(rmsVuL / vuRefRms, ref _vuVelL, dt, true);
            UpdateVu(rmsVuR / vuRefRms, ref _vuVelR, dt, false);

            RmsDbL = ToDb(rmsL * 1.41421356f);
            RmsDbR = ToDb(rmsR * 1.41421356f);
            UpdatePeak(pkL, ref _peakLinL, ref _peakHoldTimerL, dt);
            UpdatePeak(pkR, ref _peakLinR, ref _peakHoldTimerR, dt);
            PeakDbL = ToDb(_peakLinL);
            PeakDbR = ToDb(_peakLinR);
            SignalPresent = Math.Max(pkL, pkR) > 0.0005f;

            // --- spectrum ---
            var channel = _state.SpectrumChannel;
            int offset = need - n;
            for (int i = 0; i < n; i++)
            {
                float l = _left[offset + i], r = _right[offset + i];
                _mono[i] = channel == SpectrumChannel.Left ? l : channel == SpectrumChannel.Right ? r : 0.5f * (l + r);
            }
            _fft.Power(_mono, _power);

            float binHz = (float)sr / n;
            int bins = n / 2;
            float attack = 1f - MathF.Exp(-dt / _state.SpectrumAttack);
            float release = _state.SpectrumReleaseDbPerSec * dt;
            for (int b = 0; b < BandCount; b++)
            {
                float lo = _bandLo[b] / binHz, hi = _bandHi[b] / binHz;
                float p;
                int iLo = (int)MathF.Ceiling(lo), iHi = (int)MathF.Floor(hi - 1e-4f);
                if (iHi < iLo)
                {
                    // narrower than a bin: interpolate the power of the two neighbouring bins
                    float c = _bandCentre[b] / binHz;
                    int i0 = Math.Clamp((int)c, 1, bins - 2);
                    float t = c - i0;
                    p = _power[i0] * (1 - t) + _power[i0 + 1] * t;
                }
                else
                {
                    // sum the power of every bin in the band
                    p = 0f;
                    iLo = Math.Clamp(iLo, 1, bins - 1);
                    iHi = Math.Clamp(iHi, 1, bins - 1);
                    for (int i = iLo; i <= iHi; i++) p += _power[i];
                }
                float value = 10f * MathF.Log10(p + 1e-12f) + DisplayOffsetDb;
                if (value < MinDb) value = MinDb;
                if (value > 6f) value = 6f;

                float cur = Bands[b];
                if (value > cur) cur += (value - cur) * attack;
                else cur = Math.Max(value, cur - release);
                Bands[b] = cur;

                if (cur >= BandPeaks[b]) { BandPeaks[b] = cur; _peakHoldTimer[b] = _state.PeakHoldSeconds; }
                else
                {
                    _peakHoldTimer[b] -= dt;
                    if (_peakHoldTimer[b] < 0) BandPeaks[b] = Math.Max(cur, BandPeaks[b] - 24f * dt);
                }
            }

            // --- waterfall history (new row every ~38 ms; 32 rows span ~1.2 s) ---
            _historyClock += dt;
            if (_historyClock >= 0.038)
            {
                _historyClock = 0;
                HistoryHead = (HistoryHead + HistoryRows - 1) % HistoryRows;
                Array.Copy(Bands, History[HistoryHead], BandCount);
            }

            // --- oscilloscope: 50 ms window with rising-edge trigger ---
            int scopeSamples = Math.Min(need, (int)(sr * 0.05f));
            int search = Math.Min(need - scopeSamples, (int)(sr * 0.02f));
            int scopeStart = need - scopeSamples;
            int from = need - scopeSamples - search;
            for (int i = from + 1; i < need - scopeSamples; i++)
            {
                float a = 0.5f * (_left[i - 1] + _right[i - 1]), bsample = 0.5f * (_left[i] + _right[i]);
                if (a < 0 && bsample >= 0 && bsample - a > 0.0005f) { scopeStart = i; break; }
            }
            for (int p = 0; p < ScopePoints; p++)
            {
                int s0 = scopeStart + (int)((long)p * scopeSamples / ScopePoints);
                int s1 = scopeStart + (int)((long)(p + 1) * scopeSamples / ScopePoints);
                if (s1 <= s0) s1 = s0 + 1;
                float acc = 0;
                int cnt = 0;
                for (int i = s0; i < s1 && i < need; i++) { acc += 0.5f * (_left[i] + _right[i]); cnt++; }
                Scope[p] = cnt > 0 ? acc / cnt : 0;
            }

            // --- phase scope: newest samples, auto-ranged ---
            int phaseAvailable = Math.Min(need, PhasePoints);
            float pkNow = Math.Max(pkL, pkR);
            float wantScale = pkNow > 0.002f ? Math.Clamp(0.9f / pkNow, 0.7f, 40f) : _phaseScale;
            _phaseScale += (wantScale - _phaseScale) * (wantScale < _phaseScale ? 0.25f : 0.03f);
            double sxx = 0, syy = 0, sxy = 0;
            int count = 0;
            for (int i = need - phaseAvailable; i < need; i++)
            {
                float x = _left[i] * _phaseScale;
                float y = _right[i] * _phaseScale;
                PhaseX[count] = x; PhaseY[count] = y;
                sxx += x * x; syy += y * y; sxy += x * y;
                count++;
            }
            PhaseCount = count;
            if (count > 8)
            {
                sxx /= count; syy /= count; sxy /= count;
                double tr = sxx + syy, det = sxx * syy - sxy * sxy;
                double disc = Math.Sqrt(Math.Max(0, tr * tr / 4 - det));
                double l1 = tr / 2 + disc, l2 = Math.Max(0, tr / 2 - disc);
                PhaseEllipseA = (float)(2.2 * Math.Sqrt(l1));
                PhaseEllipseB = (float)(2.2 * Math.Sqrt(l2));
                PhaseEllipseAngle = (float)(0.5 * Math.Atan2(2 * sxy, sxx - syy));
            }
        }

        private void UpdateVu(float target, ref float vel, float dt, bool left)
        {
            // Second-order needle: ~300 ms to reach 99 % with a hint of overshoot (VU ballistics).
            const float omega = 19f, zeta = 0.75f;
            float pos = left ? VuLeft : VuRight;
            target = Math.Min(target, 1.6f);
            int steps = Math.Max(1, (int)(dt / 0.004f));
            float h = dt / steps;
            for (int s = 0; s < steps; s++)
            {
                float acc = omega * omega * (target - pos) - 2f * zeta * omega * vel;
                vel += acc * h;
                pos += vel * h;
                if (pos < 0) { pos = 0; vel = 0; }
            }
            if (left) VuLeft = pos; else VuRight = pos;
        }

        private static void UpdatePeak(float sample, ref float held, ref float timer, float dt)
        {
            if (sample >= held) { held = sample; timer = 0.6f; }
            else
            {
                timer -= dt;
                if (timer < 0) held *= MathF.Pow(10f, -20f * dt / 20f); // 20 dB/s
                if (held < sample) held = sample;
            }
        }

        private static float ToDb(float lin) => lin <= 1e-6f ? -120f : 20f * MathF.Log10(lin);
    }
}
