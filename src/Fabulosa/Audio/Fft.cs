using System;

namespace Fabulosa.Audio
{
    /// <summary>
    /// In-place iterative radix-2 FFT for real input with a precomputed Hann window,
    /// bit-reversal table and twiddle factors. Output is a magnitude spectrum in dBFS
    /// normalised so that a full-scale sine reads 0 dB in its bin.
    /// </summary>
    public sealed class Fft
    {
        private readonly int _n;
        private readonly int _log2n;
        private readonly float[] _re;
        private readonly float[] _im;
        private readonly float[] _window;
        private readonly int[] _bitRev;
        private readonly float[] _cos;
        private readonly float[] _sin;
        private readonly float _normalise;

        public Fft(int size)
        {
            if (size < 16 || (size & (size - 1)) != 0) throw new ArgumentException("FFT size must be a power of two", nameof(size));
            _n = size;
            _log2n = (int)Math.Round(Math.Log(size, 2));
            _re = new float[size];
            _im = new float[size];
            _window = new float[size];
            _bitRev = new int[size];
            _cos = new float[size / 2];
            _sin = new float[size / 2];

            double windowSum = 0;
            for (int i = 0; i < size; i++)
            {
                _window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (size - 1)));
                windowSum += _window[i];
            }
            // A sine of amplitude A produces a bin magnitude of A * sum(window) / 2.
            _normalise = (float)(2.0 / windowSum);

            for (int i = 0; i < size; i++)
            {
                int rev = 0;
                for (int b = 0; b < _log2n; b++) if ((i & (1 << b)) != 0) rev |= 1 << (_log2n - 1 - b);
                _bitRev[i] = rev;
            }
            for (int i = 0; i < size / 2; i++)
            {
                double a = -2 * Math.PI * i / size;
                _cos[i] = (float)Math.Cos(a);
                _sin[i] = (float)Math.Sin(a);
            }
        }

        public int Size => _n;
        public int Bins => _n / 2;

        /// <summary>
        /// Computes the windowed magnitude spectrum of <paramref name="input"/> (length &gt;= Size)
        /// into <paramref name="magnitudeDb"/> (length &gt;= Size/2).
        /// </summary>
        public void MagnitudeDb(float[] input, float[] magnitudeDb)
        {
            Transform(input);
            int bins = _n / 2;
            for (int i = 0; i < bins; i++)
            {
                float mag = MathF.Sqrt(_re[i] * _re[i] + _im[i] * _im[i]) * _normalise;
                magnitudeDb[i] = 20f * MathF.Log10(mag + 1e-9f);
            }
        }

        /// <summary>
        /// Computes the normalised power spectrum (a full-scale sine gives 1.0 in its bin) of
        /// <paramref name="input"/> into <paramref name="power"/> (length &gt;= Size/2).
        /// </summary>
        public void Power(float[] input, float[] power)
        {
            Transform(input);
            int bins = _n / 2;
            float k = _normalise * _normalise;
            for (int i = 0; i < bins; i++) power[i] = (_re[i] * _re[i] + _im[i] * _im[i]) * k;
        }

        private void Transform(float[] input)
        {
            int n = _n;
            for (int i = 0; i < n; i++)
            {
                int j = _bitRev[i];
                _re[j] = input[i] * _window[i];
                _im[j] = 0f;
            }

            for (int len = 2, half = 1, step = n / 2; len <= n; len <<= 1, half <<= 1, step >>= 1)
            {
                for (int start = 0; start < n; start += len)
                {
                    int k = 0;
                    for (int j = start; j < start + half; j++, k += step)
                    {
                        float wr = _cos[k], wi = _sin[k];
                        int idx2 = j + half;
                        float xr = _re[idx2] * wr - _im[idx2] * wi;
                        float xi = _re[idx2] * wi + _im[idx2] * wr;
                        _re[idx2] = _re[j] - xr;
                        _im[idx2] = _im[j] - xi;
                        _re[j] += xr;
                        _im[j] += xi;
                    }
                }
            }
        }
    }
}
