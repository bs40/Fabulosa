using System;
using System.Collections.Generic;
using Fabulosa.Audio;

namespace Fabulosa.Model
{
    /// <summary>The panels the View menu can show or hide, in menu order.</summary>
    public enum Visualiser { Spectrum, LevelMeters, Dancer, Oscilloscope, PhaseScope, Spectrum3D, Radial }

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

        /// <summary>How much of the radial trail is drawn, in seconds (0.4 = short, saved).</summary>
        public float RadialTrailSeconds { get; set; } = 0.4f;

        /// <summary>Draw the radial spikes as outlines instead of filled shapes (saved).</summary>
        public bool RadialLines { get; set; }

        /// <summary>Draw at the design size and let the graphics card scale it up: steadier on big screens (saved).</summary>
        public bool PerformanceMode { get; set; }

        /// <summary>Show the YouTube player in the Now Playing slot instead of the track details (saved).</summary>
        public bool ShowYouTube { get; set; }

        /// <summary>Video or playlist the YouTube panel plays (saved); empty until a link is set.</summary>
        public string YouTubeUrl { get; set; } = "";

        public static readonly Visualiser[] AllVisualisers = Enum.GetValues<Visualiser>();

        public static string NameOf(Visualiser v) => v switch
        {
            Visualiser.Spectrum => "Spectrum Analyser",
            Visualiser.LevelMeters => "Level Meters",
            Visualiser.Dancer => "Dancer",
            Visualiser.Oscilloscope => "Oscilloscope",
            Visualiser.PhaseScope => "Phase Scope",
            Visualiser.Spectrum3D => "3D Spectrum",
            Visualiser.Radial => "Radial Spectrum",
            _ => v.ToString(),
        };

        /// <summary>Visualisers the user has hidden. The hidden set is what gets saved, so visualisers added later start out shown.</summary>
        public HashSet<Visualiser> HiddenVisualisers { get; } = new HashSet<Visualiser>();

        public bool IsVisible(Visualiser v) => !HiddenVisualisers.Contains(v);

        public void SetVisible(Visualiser v, bool visible)
        {
            if (visible) HiddenVisualisers.Remove(v);
            else HiddenVisualisers.Add(v);
        }

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
