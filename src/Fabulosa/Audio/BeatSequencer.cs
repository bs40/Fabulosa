using System;

namespace Fabulosa.Audio
{
    /// <summary>
    /// Turns the beat clock into a sprite frame that only ever moves forward one frame at a time.
    /// Frames advance on each subdivision of the beat; timing corrections that would skip a frame
    /// or step back are absorbed by catching up or holding the pose, and losing the beat returns
    /// to the rest pose (frame 0).
    /// </summary>
    public sealed class BeatSequencer
    {
        private const int MaxLag = 16;   // further behind than this (a run of missed beats) jumps straight to the beat
        private readonly int _frames;
        private bool _active;
        private long _anchor, _shown;
        private float _framesPerBeat;

        public BeatSequencer(int frames) { _frames = Math.Max(1, frames); }

        public int Frame { get; private set; }

        public int Update(BeatTracker beat, float framesPerBeat)
        {
            if (beat.LastBeatTime < 0)
            {
                _active = false;
                return Frame = 0;
            }
            long target = (long)Math.Floor((beat.BeatCount + Math.Min(beat.Phase, 0.999f)) * framesPerBeat);
            if (!_active)
            {
                _active = true;
                _shown = 0;
                _anchor = target;
                _framesPerBeat = framesPerBeat;
            }
            else if (framesPerBeat != _framesPerBeat)
            {
                _anchor = target - _shown;   // keep the current pose when the rate changes
                _framesPerBeat = framesPerBeat;
            }

            long want = target - _anchor;
            if (want > _shown + MaxLag) _shown = want;
            else if (want > _shown) _shown++;
            else if (want < _shown - MaxLag) _anchor = target - _shown;   // clock jumped far back: re-anchor on the current pose
            return Frame = (int)(_shown % _frames);
        }
    }
}
