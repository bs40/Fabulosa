using System;
using Fabulosa.Model;

namespace Fabulosa.Audio
{
    public enum SpectrumChannel { Both, Left, Right }

    /// <summary>
    /// Per-frame analysis of the captured audio: log-spaced band spectrum with smoothing and
    /// peak hold, VU ballistics, peak/RMS meters, oscilloscope trace, phase-scope points, the
    /// 3D waterfall and radial histories and the beat tracker. Everything is computed on the UI
    /// thread from a snapshot of the ring buffer, so views can read the arrays directly.
    /// </summary>
    public sealed class Analyzer
    {
        public const int BandCount = 64;
        public const int HistoryRows = 32;
        public const int RadialRows = 28;
        public const int ScopePoints = 440;
        public const int PhasePoints = 1000;
        public const float MinDb = -60f;
        /// <summary>Band levels are power sums, which sit well below a full-scale sine for real music; lift them so a loud mix reaches the top of the scale.</summary>
        public const float DisplayOffsetDb = 10f;
        /// <summary>0 VU reference in dBFS (sine). -14 dBFS suits streaming-normalised material.</summary>
        public const float VuReferenceDbfs = -14f;
        public const float RadialRowSeconds = 0.04f;
        /// <summary>A short window for the radial view (46 ms at 44.1 kHz), about one row long, so successive rows stay independent and spikes stay sharp.</summary>
        private const int RadialFftSize = 2048;

        private readonly StereoRingBuffer _buffer;
        private readonly AppState _state;
        private Fft _fft;
        private readonly Fft _radialFft = new Fft(RadialFftSize);
        private float[] _left = new float[16384];
        private float[] _right = new float[16384];
        private float[] _mono = new float[16384];
        private float[] _power = new float[8192];
        private readonly float[] _radialWindow = new float[RadialFftSize];
        private readonly float[] _radialPower = new float[RadialFftSize / 2];
        private readonly float[] _bandLo = new float[BandCount];
        private readonly float[] _bandHi = new float[BandCount];
        private readonly float[] _bandCentre = new float[BandCount];
        private readonly float[] _peakHoldTimer = new float[BandCount];
        private double _historyClock, _radialClock;
        private float _radialLoudest = 0.4f;
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
            RadialHistory = new float[RadialRows][];
            for (int i = 0; i < RadialRows; i++) RadialHistory[i] = new float[BandCount * 2];
            Array.Fill(Bands, MinDb);
            Array.Fill(BandPeaks, MinDb);
            ConfigureBands();
        }

        public int SampleRate { get; set; } = 48000;
        public int FftSize { get; set; } = 8192;
        public BeatTracker Beat { get; } = new BeatTracker();

        /// <summary>The radial view's per-channel spectra cost two extra FFTs, so they are only worked out while it is shown.</summary>
        public bool RadialEnabled { get; set; }

        // ---- outputs -------------------------------------------------------------------
        /// <summary>Smoothed band levels in dB (MinDb..0+).</summary>
        public float[] Bands { get; } = new float[BandCount];
        /// <summary>Peak-hold levels per band in dB.</summary>
        public float[] BandPeaks { get; } = new float[BandCount];
        /// <summary>Waterfall rows, index 0 = newest.</summary>
        public float[][] History { get; }
        public int HistoryHead { get; private set; }
        /// <summary>Radial spike heights (0..1), row 0 = newest: the left channel's bands, then the right channel's.</summary>
        public float[][] RadialHistory { get; }
        public int RadialHead { get; private set; }
        /// <summary>How far the newest radial row has travelled towards the next (0..1), for smooth outward motion.</summary>
        public float RadialPhase { get; private set; }
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

            // --- meters (use the newest 150 ms / 100 ms) ---
            int vuWindow = Math.Min(need, (int)(sr * 0.15f));
            int rmsWindow = Math.Min(need, (int)(sr * 0.1f));
            int peakWindow = Math.Max(Math.Min(need, freshFrames), Math.Min(need, sr / 50));
            double sumL = 0, sumR = 0, sumL2 = 0, sumR2 = 0;
            float pkL = 0, pkR = 0;
            int startVu = need - vuWindow, startRms = need - rmsWindow, startPk = need - peakWindow;
            for (int i = Math.Min(startVu, Math.Min(startRms, startPk)); i < need; i++)
            {
                float l = _left[i], r = _right[i];
                if (i >= startVu) { sumL += l * l; sumR += r * r; }
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
                float value = 10f * MathF.Log10(BandPower(b, binHz, bins) + 1e-12f) + DisplayOffsetDb;
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

            // --- radial spectrum: each channel's band heights, a new row every 40 ms ---
            if (RadialEnabled)
            {
                // The newest row is worked out every frame, so the spikes at the inner edge are as live as the
                // frame rate allows; every 40 ms that row freezes into the trail and a new one starts.
                _radialClock += dt;
                if (_radialClock >= RadialRowSeconds)
                {
                    _radialClock = Math.Min(_radialClock - RadialRowSeconds, RadialRowSeconds);
                    RadialHead = (RadialHead + RadialRows - 1) % RadialRows;
                }
                RadialPhase = (float)Math.Clamp(_radialClock / RadialRowSeconds, 0.0, 1.0);
                var row = RadialHistory[RadialHead];
                int start = need - RadialFftSize;
                float loudest = Math.Max(ChannelLevels(_left, start, sr, row, 0), ChannelLevels(_right, start, sr, row, BandCount));
                // gentle automatic gain: the loudest band of the last second or two stands at full height
                _radialLoudest = Math.Max(loudest, _radialLoudest * MathF.Exp(-dt / 1.5f));
                float gain = 1f / Math.Max(_radialLoudest, 0.4f);
                for (int i = 0; i < row.Length; i++)
                {
                    float t = Math.Min(1f, row[i] * gain);
                    row[i] = t * t * t * t;   // to the fourth power: quiet bands lie flat and the loudest stand up as spikes
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

        /// <summary>Power in band b from the last FFT: the sum over its bins, or interpolated when the band is narrower than a bin.</summary>
        private float BandPower(int b, float binHz, int bins)
        {
            float lo = _bandLo[b] / binHz, hi = _bandHi[b] / binHz;
            int iLo = (int)MathF.Ceiling(lo), iHi = (int)MathF.Floor(hi - 1e-4f);
            if (iHi < iLo)
            {
                float c = _bandCentre[b] / binHz;
                int i0 = Math.Clamp((int)c, 1, bins - 2);
                float t = c - i0;
                return _power[i0] * (1 - t) + _power[i0 + 1] * t;
            }
            float p = 0f;
            iLo = Math.Clamp(iLo, 1, bins - 1);
            iHi = Math.Clamp(iHi, 1, bins - 1);
            for (int i = iLo; i <= iHi; i++) p += _power[i];
            return p;
        }

        /// <summary>
        /// One channel's band levels for the radial view on a 0..1 dB scale, taking each band's loudest bin so
        /// neighbouring bands differ and read as separate spikes. Returns the loudest band's level.
        /// </summary>
        private float ChannelLevels(float[] samples, int start, int sr, float[] row, int offset)
        {
            Array.Copy(samples, start, _radialWindow, 0, RadialFftSize);
            _radialFft.Power(_radialWindow, _radialPower);
            float binHz = (float)sr / RadialFftSize;
            int bins = RadialFftSize / 2;
            float loudest = 0f;
            for (int b = 0; b < BandCount; b++)
            {
                float lo = _bandLo[b] / binHz, hi = _bandHi[b] / binHz;
                int iLo = (int)MathF.Ceiling(lo), iHi = (int)MathF.Floor(hi - 1e-4f);
                float p = 0f;
                if (iHi < iLo)
                {
                    float c = _bandCentre[b] / binHz;
                    int i0 = Math.Clamp((int)c, 1, bins - 2);
                    float t = c - i0;
                    p = _radialPower[i0] * (1 - t) + _radialPower[i0 + 1] * t;
                }
                else
                {
                    iLo = Math.Clamp(iLo, 1, bins - 1);
                    iHi = Math.Clamp(iHi, 1, bins - 1);
                    for (int i = iLo; i <= iHi; i++) if (_radialPower[i] > p) p = _radialPower[i];
                }
                float db = 10f * MathF.Log10(p + 1e-12f) + DisplayOffsetDb;
                float level = Math.Clamp((db - MinDb) / -MinDb, 0f, 1f);
                row[offset + b] = level;
                if (level > loudest) loudest = level;
            }
            return loudest;
        }

        private void UpdateVu(float target, ref float vel, float dt, bool left)
        {
            // Second-order needle at twice the speed of classic VU ballistics (150 ms window instead of 300 ms), with a hint of overshoot.
            const float omega = 38f, zeta = 0.75f;
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
