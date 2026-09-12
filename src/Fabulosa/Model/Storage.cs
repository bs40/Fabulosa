using System;
using System.Collections.Generic;
using System.IO;
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
        public int SelectedPreset { get; set; }
        public Preset Current { get; set; }
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
        public static string PresetsPath => Path.Combine(Folder, "presets.json");

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

        public static List<Preset> LoadPresets()
        {
            try
            {
                if (File.Exists(PresetsPath))
                {
                    var list = JsonSerializer.Deserialize<List<Preset>>(File.ReadAllText(PresetsPath), Options);
                    if (list != null && list.Count > 0)
                    {
                        foreach (var p in list) Normalise(p);
                        return list;
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("presets load failed: " + ex.Message); }
            return Preset.Defaults();
        }

        public static void SavePresets(List<Preset> presets)
        {
            try { File.WriteAllText(PresetsPath, JsonSerializer.Serialize(presets, Options)); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("presets save failed: " + ex.Message); }
        }

        public static List<Preset> ImportPresets(string path)
        {
            string json = File.ReadAllText(path);
            var result = new List<Preset>();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                result.AddRange(JsonSerializer.Deserialize<List<Preset>>(json, Options) ?? new List<Preset>());
            else
            {
                var single = JsonSerializer.Deserialize<Preset>(json, Options);
                if (single != null) result.Add(single);
            }
            foreach (var p in result) Normalise(p);
            return result;
        }

        public static void ExportPreset(string path, Preset preset)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(preset, Options));
        }

        private static void Normalise(Preset p)
        {
            if (p.EqGains == null || p.EqGains.Length != AppState.EqBandCount)
            {
                var g = new float[AppState.EqBandCount];
                if (p.EqGains != null) Array.Copy(p.EqGains, g, Math.Min(g.Length, p.EqGains.Length));
                p.EqGains = g;
            }
            if (p.Effects == null || p.Effects.Length != AppState.EffectCount)
            {
                var e = new bool[AppState.EffectCount];
                if (p.Effects != null) Array.Copy(p.Effects, e, Math.Min(e.Length, p.Effects.Length));
                p.Effects = e;
            }
            for (int i = 0; i < p.EqGains.Length; i++) p.EqGains[i] = Math.Clamp(p.EqGains[i], -12f, 12f);
            p.Loudness = Math.Clamp(p.Loudness, 0f, 1f);
            p.Balance = Math.Clamp(p.Balance, -1f, 1f);
            p.StereoWidth = Math.Clamp(p.StereoWidth, 0f, 2f);
            p.Master = Math.Clamp(p.Master, 0f, 1f);
            if (string.IsNullOrWhiteSpace(p.Name)) p.Name = "Imported";
        }

        public static void ApplySettings(SettingsFile s, AppState state)
        {
            state.AlwaysOnTop = s.AlwaysOnTop;
            state.SpectrumChannel = (SpectrumChannel)Math.Clamp(s.SpectrumChannel, 0, 2);
            state.PeakHoldEnabled = s.PeakHold;
            state.FftSize = s.FftSize is 4096 or 8192 or 16384 ? s.FftSize : 8192;
            state.SpectrumReleaseDbPerSec = Math.Clamp(s.SpectrumReleaseDbPerSec, 10f, 200f);
            state.MeshLines = s.MeshLines;
            state.SelectedPreset = Math.Clamp(s.SelectedPreset, 0, Math.Max(0, state.Presets.Count - 1));
            if (s.Current != null)
            {
                Normalise(s.Current);
                state.ApplyPreset(s.Current);
                state.EqDirty = state.Presets.Count > 0 && !state.MatchesPreset(state.Presets[state.SelectedPreset]);
            }
            else if (state.Presets.Count > 0) state.ApplyPreset(state.Presets[state.SelectedPreset]);
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
            s.SelectedPreset = state.SelectedPreset;
            s.Current = state.CaptureAsPreset("current");
            return s;
        }
    }
}
