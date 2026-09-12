using System;

namespace Fabulosa.Audio
{
    /// <summary>
    /// Fixed-size ring buffer of stereo frames. The capture thread writes, the UI thread
    /// takes snapshots of the most recent N frames. A single lock is used; both sides
    /// hold it only for the duration of an array copy.
    /// </summary>
    public sealed class StereoRingBuffer
    {
        private readonly float[] _left;
        private readonly float[] _right;
        private readonly object _gate = new object();
        private int _writePos;
        private long _totalWritten;

        public StereoRingBuffer(int capacityFrames)
        {
            Capacity = capacityFrames;
            _left = new float[capacityFrames];
            _right = new float[capacityFrames];
        }

        public int Capacity { get; }

        /// <summary>Total frames written since creation (monotonic).</summary>
        public long TotalWritten { get { lock (_gate) return _totalWritten; } }

        public void Write(float[] left, float[] right, int count)
        {
            lock (_gate)
            {
                int pos = _writePos;
                for (int i = 0; i < count; i++)
                {
                    _left[pos] = left[i];
                    _right[pos] = right[i];
                    pos++;
                    if (pos == Capacity) pos = 0;
                }
                _writePos = pos;
                _totalWritten += count;
            }
        }

        /// <summary>Copies the most recent <paramref name="count"/> frames into the destination arrays (oldest first).</summary>
        public void CopyLatest(float[] dstLeft, float[] dstRight, int count)
        {
            if (count > Capacity) throw new ArgumentOutOfRangeException(nameof(count));
            lock (_gate)
            {
                int start = _writePos - count;
                if (start < 0) start += Capacity;
                int firstChunk = Math.Min(count, Capacity - start);
                Array.Copy(_left, start, dstLeft, 0, firstChunk);
                Array.Copy(_right, start, dstRight, 0, firstChunk);
                int remaining = count - firstChunk;
                if (remaining > 0)
                {
                    Array.Copy(_left, 0, dstLeft, firstChunk, remaining);
                    Array.Copy(_right, 0, dstRight, firstChunk, remaining);
                }
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                Array.Clear(_left, 0, _left.Length);
                Array.Clear(_right, 0, _right.Length);
            }
        }
    }
}
