using System;

namespace Fabulosa.Audio
{
    /// <summary>
    /// Plays a video loop on the beat clock: framesPerBeat frames per beat, with the clip's own beat
    /// frame (phaseFrames) landing on the music's beat. Frames only move forward - a timing correction
    /// that would step back holds the picture instead - and without a beat and tempo the clip rests
    /// on its beat frame.
    /// </summary>
    public sealed class ClipClock
    {
        private bool _active;
        private int _anchorBeat;
        private long _shown;

        public int Update(BeatTracker beat, int loopFrames, double framesPerBeat, double phaseFrames)
        {
            if (loopFrames <= 0) return 0;
            if (beat.LastBeatTime < 0 || beat.Period <= 0)
            {
                _active = false;
                return Wrap((long)Math.Floor(phaseFrames), loopFrames);
            }
            if (!_active)
            {
                // start on a whole beat so the clip's beat frames stay on the music's beats
                _active = true;
                _anchorBeat = beat.BeatCount;
                _shown = (long)Math.Floor(phaseFrames);
            }
            double beats = beat.BeatCount - _anchorBeat + Math.Min(beat.Phase, 0.999f);
            long target = (long)Math.Floor(beats * framesPerBeat + phaseFrames);
            if (target > _shown) _shown = target;
            return Wrap(_shown, loopFrames);
        }

        private static int Wrap(long frame, int loopFrames) => (int)(((frame % loopFrames) + loopFrames) % loopFrames);
    }
}
