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
    }
}
