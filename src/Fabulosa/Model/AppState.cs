using Fabulosa.Audio;

namespace Fabulosa.Model
{
    /// <summary>Everything the user can change. Plain properties; the renderer reads them every frame.</summary>
    public sealed class AppState
    {
        public SpectrumChannel SpectrumChannel { get; set; } = SpectrumChannel.Both;
        public float PeakHoldSeconds { get; set; } = 0.8f;
        public bool PeakHoldEnabled { get; set; } = true;
        public float SpectrumAttack { get; set; } = 0.025f;
        public float SpectrumReleaseDbPerSec { get; set; } = 42f;
        public int FftSize { get; set; } = 8192;
        public bool MeshLines { get; set; } = true;
        public bool AlwaysOnTop { get; set; }
        public bool ShowFps { get; set; }
        /// <summary>Sprite frames the dancer advances per detected beat.</summary>
        public float DancerFramesPerBeat { get; set; } = 4f;

        /// <summary>Clicks on the clock that swap the stick figure for the dancer clip.</summary>
        public const int ClockClicksForClip = 11;

        /// <summary>True while the dancer panel shows the clip (session only, never saved).</summary>
        public bool ShowDancerClip { get; private set; }

        /// <summary>Clock clicks counted so far towards <see cref="ClockClicksForClip"/>.</summary>
        public int ClockClicks { get; private set; }

        /// <summary>The 11th click on the clock swaps in the clip; the next click swaps back and the count starts again.</summary>
        public void ClockClicked()
        {
            if (ShowDancerClip)
            {
                ShowDancerClip = false;
                ClockClicks = 0;
                return;
            }
            if (++ClockClicks >= ClockClicksForClip)
            {
                ShowDancerClip = true;
                ClockClicks = 0;
            }
        }

        public void ResetClockClicks()
        {
            ShowDancerClip = false;
            ClockClicks = 0;
        }
    }
}
