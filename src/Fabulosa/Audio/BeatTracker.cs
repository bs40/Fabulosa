using System;

namespace Fabulosa.Audio
{
    /// <summary>
    /// Real-time beat tracker. Audio is consumed in 512-sample hops; each hop yields an onset
    /// strength (rise in log energy across three bands). Onsets drive a phase-locked beat clock
    /// whose period comes from an autocorrelation of the onset function, so the clock keeps
    /// ticking through breakdowns and re-locks on the next kick. All times are audio time
    /// (seconds of samples consumed), which is immune to render-loop jitter.
    /// </summary>
    public sealed class BeatTracker
    {
        public const int Hop = 512;
        private const double HistorySeconds = 8.0;
        private const double MinPeriod = 0.28, MaxPeriod = 1.0;      // 214 .. 60 BPM
        private const float SilenceDb = -65f;
        private const float LockConfidence = 0.12f;

        private int _sampleRate;
        private double _hopSeconds;
        private Biquad _low, _mid, _high;
        private double _eLow, _eMid, _eHigh;
        private int _hopFill;
        private float _dbLow = -120f, _dbMid = -120f, _dbHigh = -120f;
        private float[] _odf = Array.Empty<float>();
        private float[] _work = Array.Empty<float>();
        private double[] _ac = Array.Empty<double>();
        private int _odfPos;
        private long _hops;
        private readonly float[] _last = new float[5];
        private float _mean = 1f, _var = 1f;
        private int _hopsSinceTempo;
        private double _candidatePeriod;
        private int _candidateHits;
        private int _predictedRun, _offbeatHits;
        private double _silenceSince = -1;
        private bool _lastWasPredicted;

        public BeatTracker() { Reset(48000); }

        public int SampleRate => _sampleRate;
        /// <summary>Audio time of the last processed hop, in seconds.</summary>
        public double Now { get; private set; }
        /// <summary>Beat period in seconds (0 until a tempo has been found).</summary>
        public double Period { get; private set; }
        public float Confidence { get; private set; }
        /// <summary>True while the clock is predicting beats between detected onsets.</summary>
        public bool Locked { get; private set; }
        /// <summary>Total beats emitted; increments exactly once per beat.</summary>
        public int BeatCount { get; private set; }
        /// <summary>Audio time of the last beat, or -1 when no beat phase is established.</summary>
        public double LastBeatTime { get; private set; } = -1;
        public double LastOnsetTime { get; private set; } = -1;
        public float LastOnsetStrength { get; private set; }
        public bool Signal { get; private set; }
        public bool HasTempo => Period > 0 && Confidence >= LockConfidence;
        public float Bpm => Period > 0 ? (float)(60.0 / Period) : 0f;
        /// <summary>Progress through the current beat, 0..1.</summary>
        public float Phase => Period > 0 && LastBeatTime >= 0 ? (float)Math.Clamp((Now - LastBeatTime) / Period, 0.0, 1.0) : 0f;
        /// <summary>1 on the beat, decaying with a 120 ms time constant.</summary>
        public float Pulse => LastBeatTime >= 0 ? (float)Math.Exp(-(Now - LastBeatTime) / 0.12) : 0f;

        /// <summary>Configures for a sample rate and clears the analysis state (the beat count is kept so the dancer does not jump).</summary>
        public void Reset(int sampleRate)
        {
            _sampleRate = Math.Max(8000, sampleRate);
            _hopSeconds = (double)Hop / _sampleRate;
            _low = Biquad.BandPass(_sampleRate, 80, 0.8);
            _mid = Biquad.BandPass(_sampleRate, 300, 0.7);
            _high = Biquad.BandPass(_sampleRate, 4500, 0.6);
            int len = (int)Math.Ceiling(HistorySeconds / _hopSeconds);
            _odf = new float[len];
            _work = new float[len];
            _odfPos = 0; _hops = 0; _hopFill = 0;
            _eLow = _eMid = _eHigh = 0;
            _dbLow = _dbMid = _dbHigh = -120f;
            Array.Clear(_last, 0, _last.Length);
            _mean = 1f; _var = 1f;
            Now = 0; Period = 0; Confidence = 0; Locked = false;
            LastBeatTime = -1; LastOnsetTime = -1; LastOnsetStrength = 0; Signal = false;
            _predictedRun = 0; _offbeatHits = 0; _silenceSince = -1;
            _candidatePeriod = 0; _candidateHits = 0; _hopsSinceTempo = 0; _lastWasPredicted = false;
        }

        /// <summary>Feeds <paramref name="count"/> stereo frames starting at <paramref name="offset"/>.</summary>
        public void Process(float[] left, float[] right, int offset, int count)
        {
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                float m = 0.5f * (left[i] + right[i]);
                float yl = _low.Run(m), ym = _mid.Run(m), yh = _high.Run(m);
                _eLow += yl * yl; _eMid += ym * ym; _eHigh += yh * yh;
                if (++_hopFill == Hop) EndHop();
            }
        }

        private void EndHop()
        {
            _hopFill = 0;
            _hops++;
            Now = _hops * _hopSeconds;
            float dbL = 10f * MathF.Log10((float)(_eLow / Hop) + 1e-12f);
            float dbM = 10f * MathF.Log10((float)(_eMid / Hop) + 1e-12f);
            float dbH = 10f * MathF.Log10((float)(_eHigh / Hop) + 1e-12f);
            _eLow = _eMid = _eHigh = 0;
            float flux = Flux(dbL, _dbLow, 1.0f) + Flux(dbM, _dbMid, 0.5f) + Flux(dbH, _dbHigh, 0.6f);
            _dbLow = dbL; _dbMid = dbM; _dbHigh = dbH;
            Signal = Math.Max(dbL, Math.Max(dbM, dbH)) > SilenceDb;

            _odf[_odfPos] = flux;
            _odfPos = (_odfPos + 1) % _odf.Length;
            _last[0] = _last[1]; _last[1] = _last[2]; _last[2] = _last[3]; _last[3] = _last[4]; _last[4] = flux;
            float a = (float)(_hopSeconds / 1.5);
            _mean += (flux - _mean) * a;
            float d = flux - _mean;
            _var += (d * d - _var) * a;

            // onset = local maximum of the onset function (evaluated two hops late), well above its recent level
            float c = _last[2];
            if (_hops >= 5 && c >= _last[0] && c >= _last[1] && c > _last[3] && c >= _last[4])
            {
                float sd = MathF.Sqrt(Math.Max(_var, 0.25f));
                double t = Now - 2 * _hopSeconds;
                if (c > _mean + 1.2f * sd + 1.0f && c > 2.5f && t - LastOnsetTime >= 0.1)
                    OnOnset(t, (c - _mean) / sd);
            }

            if (!Signal)
            {
                if (_silenceSince < 0) _silenceSince = Now;
                else if (Now - _silenceSince > 1.0 && LastBeatTime >= 0) { Locked = false; LastBeatTime = -1; _predictedRun = 0; }
            }
            else _silenceSince = -1;

            if (++_hopsSinceTempo * _hopSeconds >= 0.5) { _hopsSinceTempo = 0; EstimateTempo(); }

            if (Locked && Period > 0 && LastBeatTime >= 0 && Now >= LastBeatTime + Period)
            {
                if (Now - LastBeatTime > 2.5 * Period)
                {
                    // stale clock: wait for an onset rather than fire a burst of catch-up beats
                    LastBeatTime = -1; _predictedRun = 0;
                }
                else
                {
                    LastBeatTime += Period;
                    BeatCount++;
                    _predictedRun++;
                    _lastWasPredicted = true;
                    if (_predictedRun > 12 && Confidence < 0.25f) { Locked = false; LastBeatTime = -1; _predictedRun = 0; }
                }
            }
        }

        private static float Flux(float db, float prev, float weight)
        {
            if (db < -70f) return 0f;
            float rise = db - Math.Max(prev, -70f);
            return rise > 0f ? weight * Math.Min(rise, 24f) : 0f;
        }

        private void OnOnset(double t, float strength)
        {
            LastOnsetTime = t;
            LastOnsetStrength = strength;
            if (!HasTempo)
            {
                // no usable tempo yet: strong onsets are beats
                if (strength >= 2.5f && (LastBeatTime < 0 || t - LastBeatTime >= 0.25)) Beat(t);
                return;
            }
            if (LastBeatTime < 0) { Beat(t); return; }

            double since = t - LastBeatTime;
            double k = Math.Round(since / Period);
            double pred = LastBeatTime + k * Period;
            double err = t - pred;
            double tol = 0.16 * Period;
            if (k >= 1 && Math.Abs(err) <= tol)
            {
                if (k > 1) BeatCount += Math.Min((int)k - 1, 3);
                Period = Math.Clamp(Period + 0.03 * err / k, MinPeriod, MaxPeriod);
                Beat(pred + 0.5 * err);
                return;
            }
            if (k == 0 && _lastWasPredicted && err >= 0 && err <= tol)
            {
                // late confirmation of a predicted beat: pull the clock toward the onset
                LastBeatTime += 0.5 * err;
                Period = Math.Clamp(Period + 0.03 * err, MinPeriod, MaxPeriod);
                _predictedRun = 0; _offbeatHits = 0; _lastWasPredicted = false;
                return;
            }
            double frac = since / Period;
            frac -= Math.Floor(frac);
            if (frac > 0.3 && frac < 0.7 && strength >= 3f)
            {
                // strong onsets consistently half a beat away: we are locked on the off-beat, flip
                if (++_offbeatHits >= 3) Beat(t);
            }
        }

        private void Beat(double t)
        {
            LastBeatTime = t;
            BeatCount++;
            _predictedRun = 0; _offbeatHits = 0; _lastWasPredicted = false;
            Locked = HasTempo;
        }

        private void EstimateTempo()
        {
            int len = _odf.Length;
            int n = (int)Math.Min(_hops, len);
            if (n * _hopSeconds < 3.0) return;
            int start = ((_odfPos - n) % len + len) % len;
            double sum = 0;
            for (int i = 0; i < n; i++) { float v = _odf[(start + i) % len]; _work[i] = v; sum += v; }
            float mean = (float)(sum / n);
            double r0 = 0;
            for (int i = 0; i < n; i++) { float v = _work[i] - mean; if (v < 0) v = 0; _work[i] = v; r0 += v * v; }
            double r0n = r0 / n;
            if (r0n < 1e-4)
            {
                Confidence *= 0.5f;
                if (Confidence < LockConfidence) Locked = false;
                return;
            }

            int lMin = (int)Math.Ceiling(MinPeriod / _hopSeconds);
            int lMax = (int)Math.Floor(MaxPeriod / _hopSeconds);
            int lTop = Math.Min(lMax * 3, n - lMin);
            if (lTop < lMin + 3) return;
            if (_ac.Length < lTop + 2) _ac = new double[lTop + 2];
            for (int lag = lMin - 1; lag <= lTop; lag++)
            {
                double acc = 0;
                for (int i = lag; i < n; i++) acc += _work[i] * _work[i - lag];
                _ac[lag] = acc / (n - lag);
            }

            // comb over the lag and its multiples, weighted by a broad prior around 115 BPM
            int bestL = -1;
            double bestScore = 0;
            int lLast = Math.Min(lMax, lTop - 1);
            for (int lag = lMin; lag <= lLast; lag++)
            {
                double s = _ac[lag];
                if (2 * lag <= lTop) s += 0.5 * _ac[2 * lag];
                if (3 * lag <= lTop) s += 0.25 * _ac[3 * lag];
                double tempo = 60.0 / (lag * _hopSeconds);
                double z = Math.Log(tempo / 115.0, 2) / 0.7;
                s *= Math.Exp(-0.5 * z * z);
                if (s > bestScore) { bestScore = s; bestL = lag; }
            }
            if (bestL < 0) return;

            double y0 = _ac[bestL - 1], y1 = _ac[bestL], y2 = _ac[bestL + 1];
            double denom = y0 - 2 * y1 + y2;
            double delta = Math.Abs(denom) > 1e-9 ? Math.Clamp(0.5 * (y0 - y2) / denom, -0.5, 0.5) : 0;
            double newPeriod = (bestL + delta) * _hopSeconds;
            float conf = (float)Math.Clamp(_ac[bestL] / r0n * 1.6, 0, 1);

            if (Period <= 0) Period = newPeriod;
            else if (Math.Abs(newPeriod - Period) / Period < 0.06)
            {
                Period = 0.75 * Period + 0.25 * newPeriod;
                _candidateHits = 0;
            }
            else
            {
                // a different tempo has to show up twice in a row (1 s) before we jump to it
                if (_candidatePeriod > 0 && Math.Abs(newPeriod - _candidatePeriod) / _candidatePeriod < 0.05) _candidateHits++;
                else { _candidatePeriod = newPeriod; _candidateHits = 1; }
                if (_candidateHits >= 2) { Period = newPeriod; _candidateHits = 0; _candidatePeriod = 0; }
            }
            Confidence = 0.5f * (Confidence + conf);
            if (!HasTempo) Locked = false;
            else if (LastBeatTime >= 0 && Now - LastBeatTime < 2 * Period) Locked = true;
        }

        /// <summary>RBJ band-pass biquad (constant 0 dB peak gain), transposed direct form II.</summary>
        private sealed class Biquad
        {
            private readonly float _b0, _b1, _b2, _a1, _a2;
            private float _z1, _z2;

            private Biquad(float b0, float b1, float b2, float a1, float a2) { _b0 = b0; _b1 = b1; _b2 = b2; _a1 = a1; _a2 = a2; }

            public static Biquad BandPass(int fs, double f0, double q)
            {
                double w0 = 2 * Math.PI * f0 / fs;
                double alpha = Math.Sin(w0) / (2 * q);
                double a0 = 1 + alpha;
                return new Biquad((float)(alpha / a0), 0f, (float)(-alpha / a0), (float)(-2 * Math.Cos(w0) / a0), (float)((1 - alpha) / a0));
            }

            public float Run(float x)
            {
                float y = _b0 * x + _z1;
                _z1 = _b1 * x - _a1 * y + _z2;
                _z2 = _b2 * x - _a2 * y;
                return y;
            }
        }
    }
}
