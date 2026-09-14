using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fabulosa.Audio;

namespace Fabulosa.Model
{
    /// <summary>Serialisable settings written to %AppData%\Fabulosa\settings.json.</summary>
    public sealed class SettingsFile
    {
        public double WindowLeft { get; set; } = double.NaN;
        public double WindowTop { get; set; } = double.NaN;
        public double WindowWidth { get; set; } = 0;
        public double WindowHeight { get; set; } = 0;
        public bool Maximized { get; set; }
        public bool AlwaysOnTop { get; set; }
        public int SpectrumChannel { get; set; }
        public bool PeakHold { get; set; } = true;
        public int FftSize { get; set; } = 8192;
        public float SpectrumReleaseDbPerSec { get; set; } = 42f;
        public bool MeshLines { get; set; } = true;
        public float DancerFramesPerBeat { get; set; } = 4f;
        public string[] HiddenVisualisers { get; set; } = Array.Empty<string>();
    }

    public static class Storage
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static string Folder
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Fabulosa");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string SettingsPath => Path.Combine(Folder, "settings.json");

        public static SettingsFile LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(SettingsPath), Options) ?? new SettingsFile();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("settings load failed: " + ex.Message); }
            return new SettingsFile();
        }

        public static void SaveSettings(SettingsFile s)
        {
            try { File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, Options)); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("settings save failed: " + ex.Message); }
        }

        public static void ApplySettings(SettingsFile s, AppState state)
        {
            state.AlwaysOnTop = s.AlwaysOnTop;
            state.SpectrumChannel = (SpectrumChannel)Math.Clamp(s.SpectrumChannel, 0, 2);
            state.PeakHoldEnabled = s.PeakHold;
            state.FftSize = s.FftSize is 4096 or 8192 or 16384 ? s.FftSize : 8192;
            state.SpectrumReleaseDbPerSec = Math.Clamp(s.SpectrumReleaseDbPerSec, 10f, 200f);
            state.MeshLines = s.MeshLines;
            state.DancerFramesPerBeat = s.DancerFramesPerBeat is 1f or 2f or 4f or 8f ? s.DancerFramesPerBeat : 4f;
            state.HiddenVisualisers.Clear();
            foreach (string name in s.HiddenVisualisers ?? Array.Empty<string>())
                if (Enum.TryParse(name, out Visualiser v) && Enum.IsDefined(v)) state.HiddenVisualisers.Add(v);
        }

        public static SettingsFile CaptureSettings(AppState state, SettingsFile existing)
        {
            var s = existing ?? new SettingsFile();
            s.AlwaysOnTop = state.AlwaysOnTop;
            s.SpectrumChannel = (int)state.SpectrumChannel;
            s.PeakHold = state.PeakHoldEnabled;
            s.FftSize = state.FftSize;
            s.SpectrumReleaseDbPerSec = state.SpectrumReleaseDbPerSec;
            s.MeshLines = state.MeshLines;
            s.DancerFramesPerBeat = state.DancerFramesPerBeat;
            s.HiddenVisualisers = state.HiddenVisualisers.Select(v => v.ToString()).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            return s;
        }
    }
}
