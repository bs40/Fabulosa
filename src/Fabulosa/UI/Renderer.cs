using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Fabulosa.Audio;
using Fabulosa.Model;
using Fabulosa.UI.Views;
using SkiaSharp;

namespace Fabulosa.UI
{
    /// <summary>
    /// Owns the views, the cached static layer and the hit-testing. Everything is drawn in a
    /// 1672 x 941 design space that is scaled uniformly (letter-boxed) into the surface.
    /// </summary>
    public sealed class Renderer : IDisposable
    {
        private readonly List<View> _views = new List<View>();
        private readonly ChromeView _chrome;
        private readonly BottomBarView _bottomBar;
        private readonly MenuSystem _menus;
        private SKImage _static;
        private int _staticW, _staticH;
        private bool _staticDirty = true;
        private float _scale = 1f, _offX, _offY;
        private View _captured;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Stopwatch _frameClock = new Stopwatch();
        private double _fpsAccum, _msAccum;
        private int _fpsFrames;

        public FrameContext Ctx { get; }

        public Renderer(FrameContext ctx)
        {
            Ctx = ctx;
            _chrome = new ChromeView();
            _bottomBar = new BottomBarView();
            _menus = new MenuSystem(BuildMenus());

            _views.Add(new NowPlayingView());
            _views.Add(new SpectrumView());
            _views.Add(new LevelMetersView());
            _views.Add(new PresetsView());
            _views.Add(new OscilloscopeView());
            _views.Add(new PhaseScopeView());
            _views.Add(new Spectrum3DView());
            _views.Add(new EqualizerView());
            _views.Add(new EffectsView());
            _views.Add(new KnobView(Layout.Loudness, "LOUDNESS", "MIN", "MAX",
                new[] { new SKColor(0xff, 0x2a, 0x2a), new SKColor(0xff, 0xa0, 0x30), new SKColor(0xff, 0xd0, 0x40), new SKColor(0x30, 0xc0, 0xff), new SKColor(0x1a, 0x6c, 0xff) },
                s => s.Loudness, (s, v) => s.Loudness = v, 0.5f));
            _views.Add(new KnobView(Layout.Balance, "BALANCE", "L", "R",
                new[] { new SKColor(0xff, 0x2a, 0x2a), new SKColor(0xff, 0x8a, 0x30), new SKColor(0xff, 0xb0, 0x30), new SKColor(0xff, 0xd0, 0x60), new SKColor(0xff, 0xa0, 0x30) },
                s => (s.Balance + 1f) / 2f, (s, v) => s.Balance = v * 2f - 1f, 0.5f));
            _views.Add(new KnobView(Layout.StereoWidth, "STEREO WIDTH", "NARROW", "WIDE",
                new[] { new SKColor(0xff, 0x2a, 0x2a), new SKColor(0xff, 0x90, 0x30), new SKColor(0xff, 0xd0, 0x40), new SKColor(0x40, 0xb8, 0xff), new SKColor(0x1a, 0x70, 0xff) },
                s => s.StereoWidth / 2f, (s, v) => s.StereoWidth = v * 2f, 0.5f));
            _views.Add(new KnobView(Layout.Master, "MASTER", "-∞", "+6 dB",
                new[] { new SKColor(0x1a, 0x6c, 0xff), new SKColor(0x30, 0xc0, 0xff), new SKColor(0xff, 0xd0, 0x40), new SKColor(0xff, 0x8a, 0x30), new SKColor(0xff, 0x2a, 0x2a) },
                s => s.Master, (s, v) => s.Master = v, 0.8f, 30f));
            _views.Add(_bottomBar);

            ctx.SetStatus = msg => { _bottomBar.Status = msg; _bottomBar.StatusUntil = ctx.Time + 3f; };
            ctx.MarkStaticDirty = () => _staticDirty = true;
        }

        public void InvalidateStatic() => _staticDirty = true;

        public SKPoint ToDesign(float px, float py) => new SKPoint((px - _offX) / _scale, (py - _offY) / _scale);

        private void UpdateTransform(int w, int h)
        {
            float scale = Math.Min(w / Layout.Width, h / Layout.Height);
            if (scale <= 0) scale = 1;
            _scale = scale;
            _offX = (w - Layout.Width * scale) / 2f;
            _offY = (h - Layout.Height * scale) / 2f;
            Ctx.Scale = scale;
        }

        private void ApplyTransform(SKCanvas c)
        {
            c.ResetMatrix();
            c.Translate(_offX, _offY);
            c.Scale(_scale);
        }

        private void EnsureStatic(int w, int h)
        {
            if (_static != null && !_staticDirty && _staticW == w && _staticH == h) return;
            _static?.Dispose();
            using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
            var c = surface.Canvas;
            c.Clear(Theme.Letterbox);
            ApplyTransform(c);
            _chrome.DrawStatic(c, Ctx);
            foreach (var v in _views) v.DrawStatic(c, Ctx);
            _static = surface.Snapshot();
            _staticW = w; _staticH = h;
            _staticDirty = false;
            foreach (var v in _views) v.OnSizeChanged(Ctx);
        }

        /// <summary>Draws one frame. <paramref name="dt"/> is seconds since the previous frame.</summary>
        public void Render(SKCanvas canvas, int w, int h, float dt)
        {
            _frameClock.Restart();
            Ctx.Time = (float)_clock.Elapsed.TotalSeconds;
            Ctx.Dt = dt;
            Ctx.MenuOpen = _menus.IsOpen;
            UpdateTransform(w, h);
            EnsureStatic(w, h);

            canvas.ResetMatrix();
            canvas.DrawImage(_static, 0, 0);
            ApplyTransform(canvas);

            _chrome.HoverMenu = _menus.IsOpen ? -1 : HitMenuTitle(Ctx.Mouse);
            _chrome.OpenMenu = _menus.OpenIndex;
            if (Profile)
            {
                _profileMs ??= new double[_views.Count];
                for (int i = 0; i < _views.Count; i++)
                {
                    long t0 = Stopwatch.GetTimestamp();
                    _views[i].Draw(canvas, Ctx);
                    double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                    _profileMs[i] = _profileMs[i] * 0.9 + ms * 0.1;
                }
            }
            else
            {
                foreach (var v in _views) v.Draw(canvas, Ctx);
            }
            _chrome.Draw(canvas, Ctx);
            _menus.Draw(canvas, Ctx);

            _frameClock.Stop();
            _msAccum += _frameClock.Elapsed.TotalMilliseconds;
            _fpsAccum += dt;
            _fpsFrames++;
            if (_fpsAccum >= 0.5)
            {
                _chrome.Fps = (float)(_fpsFrames / _fpsAccum);
                _chrome.RenderMs = (float)(_msAccum / _fpsFrames);
                _fpsAccum = 0; _msAccum = 0; _fpsFrames = 0;
            }
        }

        public float LastRenderMs => _chrome.RenderMs;
        public float Fps => _chrome.Fps;

        /// <summary>When set, per-view draw times are tracked (exponential average) for <see cref="ProfileReport"/>.</summary>
        public bool Profile { get; set; }
        private double[] _profileMs;

        public string ProfileReport()
        {
            if (_profileMs == null) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _views.Count; i++) sb.Append(_views[i].GetType().Name).Append('=').Append(_profileMs[i].ToString("0.00")).Append("ms\n");
            return sb.ToString();
        }

        /// <summary>Renders a frame at native 1672 x 941 to a PNG file (used for screenshots and verification).</summary>
        public void RenderToPng(string path)
        {
            int w = (int)Layout.Width, h = (int)Layout.Height;
            using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
            var c = surface.Canvas;
            float savedScale = _scale, savedX = _offX, savedY = _offY;
            var savedStatic = _static; int sw = _staticW, sh = _staticH; bool dirty = _staticDirty;
            _static = null; _staticDirty = true;
            try
            {
                Render(c, w, h, 1f / 60f);
                using var img = surface.Snapshot();
                using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                using var fs = File.Create(path);
                data.SaveTo(fs);
            }
            finally
            {
                _static?.Dispose();
                _static = savedStatic; _staticW = sw; _staticH = sh; _staticDirty = dirty;
                _scale = savedScale; _offX = savedX; _offY = savedY; Ctx.Scale = savedScale;
                foreach (var v in _views) v.OnSizeChanged(Ctx);
            }
        }

        // ---- input --------------------------------------------------------------------

        private int HitMenuTitle(SKPoint p)
        {
            for (int i = 0; i < Layout.MenuTitles.Length; i++) if (_chrome.MenuRect(i).Contains(p)) return i;
            return -1;
        }

        public void MouseMove(SKPoint p)
        {
            Ctx.Mouse = p;
            if (_menus.IsOpen)
            {
                _menus.MouseMove(p);
                int title = HitMenuTitle(p);
                if (title >= 0 && title != _menus.OpenIndex) _menus.Open(title, _chrome.MenuRect(title), Ctx);
                return;
            }
            if (_captured != null) _captured.OnMouseMove(p, Ctx);
        }

        public void MouseLeave()
        {
            Ctx.Mouse = new SKPoint(-1, -1);
        }

        /// <summary>Returns true when the press should start a window drag.</summary>
        public bool MouseDown(SKPoint p, int clickCount)
        {
            Ctx.Mouse = p;
            Ctx.MouseDown = true;
            if (_menus.IsOpen)
            {
                if (_menus.MouseDown(p, Ctx)) return false;
                int t = HitMenuTitle(p);
                _menus.Close();
                if (t >= 0) return false; // clicking the open title closes it
                return false;
            }

            int title2 = HitMenuTitle(p);
            if (title2 >= 0) { _menus.Open(title2, _chrome.MenuRect(title2), Ctx); return false; }

            if (Layout.BtnMinimize.Contains(p)) { Ctx.Host.Minimize(); return false; }
            if (Layout.BtnMaximize.Contains(p)) { Ctx.Host.ToggleMaximize(); return false; }
            if (Layout.BtnClose.Contains(p)) { Ctx.Host.CloseWindow(); return false; }

            foreach (var v in _views)
            {
                if (!v.HitTest(p)) continue;
                if (clickCount == 2 && v.OnDoubleClick(p, Ctx)) { _captured = null; return false; }
                if (v.OnMouseDown(p, Ctx)) { _captured = v; return false; }
            }

            // title bar / empty chassis: drag the window (double-click toggles maximise)
            if (Layout.TitleBar.Contains(p) || Layout.MenuBar.Contains(p))
            {
                if (clickCount == 2) { Ctx.Host.ToggleMaximize(); return false; }
                return true;
            }
            return false;
        }

        public void MouseUp(SKPoint p)
        {
            Ctx.Mouse = p;
            Ctx.MouseDown = false;
            if (_captured != null) { _captured.OnMouseUp(p, Ctx); _captured = null; }
        }

        public void Wheel(SKPoint p, int delta)
        {
            Ctx.Mouse = p;
            if (_menus.IsOpen) return;
            foreach (var v in _views)
                if (v.HitTest(p) && v.OnWheel(p, delta, Ctx)) return;
        }

        public void CloseMenus() => _menus.Close();
        public bool IsMenuOpen => _menus.IsOpen;

        // ---- menus --------------------------------------------------------------------

        private List<Menu> BuildMenus()
        {
            return new List<Menu>
            {
                new Menu { Title = "File", Build = f => new List<MenuItem>
                {
                    new MenuItem { Label = "Import Presets…", Action = PresetsView.ImportPresets },
                    new MenuItem { Label = "Export Selected Preset…", Action = PresetsView.ExportSelected },
                    MenuItem.Separator(),
                    new MenuItem { Label = "Save Screenshot…", Action = SaveScreenshot },
                    MenuItem.Separator(),
                    new MenuItem { Label = "Exit", Action = ctx => ctx.Host.CloseWindow() },
                }},
                new Menu { Title = "Playback", Build = f => new List<MenuItem>
                {
                    new MenuItem { Label = "Play / Pause", Action = ctx => ctx.Media?.TogglePlayPause(), Enabled = HasSession },
                    new MenuItem { Label = "Next Track", Action = ctx => ctx.Media?.Next(), Enabled = HasSession },
                    new MenuItem { Label = "Previous Track", Action = ctx => ctx.Media?.Previous(), Enabled = HasSession },
                    MenuItem.Separator(),
                    new MenuItem { Label = "Shuffle", Action = ctx => ctx.Media?.ToggleShuffle(), Checked = ctx => ctx.Media?.Snapshot.ShuffleActive == true, Enabled = HasSession },
                    new MenuItem { DynamicLabel = ctx => "Repeat: " + RepeatLabel(ctx), Action = ctx => ctx.Media?.CycleRepeat(), Enabled = HasSession },
                    MenuItem.Separator(),
                    new MenuItem { DynamicLabel = ctx => "Source: " + (ctx.Capture?.DeviceName ?? "none"), Enabled = _ => false },
                    new MenuItem { DynamicLabel = ctx => "Format: " + (ctx.Capture?.FormatDescription ?? ""), Enabled = _ => false },
                    new MenuItem { Label = "Restart Capture", Action = ctx => ctx.Host.RestartCapture() },
                }},
                new Menu { Title = "Visualisation", Build = f => new List<MenuItem>
                {
                    new MenuItem { Label = "Spectrum: L + R", Action = ctx => SetChannel(ctx, SpectrumChannel.Both), Checked = ctx => ctx.State.SpectrumChannel == SpectrumChannel.Both },
                    new MenuItem { Label = "Spectrum: Left", Action = ctx => SetChannel(ctx, SpectrumChannel.Left), Checked = ctx => ctx.State.SpectrumChannel == SpectrumChannel.Left },
                    new MenuItem { Label = "Spectrum: Right", Action = ctx => SetChannel(ctx, SpectrumChannel.Right), Checked = ctx => ctx.State.SpectrumChannel == SpectrumChannel.Right },
                    MenuItem.Separator(),
                    new MenuItem { Label = "Peak Hold", Action = ctx => { ctx.State.PeakHoldEnabled = !ctx.State.PeakHoldEnabled; ctx.PersistSettings?.Invoke(); }, Checked = ctx => ctx.State.PeakHoldEnabled },
                    new MenuItem { Label = "Release: Fast", Action = ctx => SetRelease(ctx, 80f), Checked = ctx => ctx.State.SpectrumReleaseDbPerSec >= 70f },
                    new MenuItem { Label = "Release: Medium", Action = ctx => SetRelease(ctx, 42f), Checked = ctx => ctx.State.SpectrumReleaseDbPerSec < 70f && ctx.State.SpectrumReleaseDbPerSec > 30f },
                    new MenuItem { Label = "Release: Slow", Action = ctx => SetRelease(ctx, 24f), Checked = ctx => ctx.State.SpectrumReleaseDbPerSec <= 30f },
                    MenuItem.Separator(),
                    new MenuItem { Label = "FFT 4096 (fast)", Action = ctx => SetFft(ctx, 4096), Checked = ctx => ctx.State.FftSize == 4096 },
                    new MenuItem { Label = "FFT 8192 (default)", Action = ctx => SetFft(ctx, 8192), Checked = ctx => ctx.State.FftSize == 8192 },
                    new MenuItem { Label = "FFT 16384 (fine)", Action = ctx => SetFft(ctx, 16384), Checked = ctx => ctx.State.FftSize == 16384 },
                    MenuItem.Separator(),
                    new MenuItem { Label = "3D Mesh Lines", Action = ctx => { ctx.State.MeshLines = !ctx.State.MeshLines; ctx.PersistSettings?.Invoke(); }, Checked = ctx => ctx.State.MeshLines },
                    new MenuItem { Label = "Show Frame Rate", Action = ctx => ctx.State.ShowFps = !ctx.State.ShowFps, Checked = ctx => ctx.State.ShowFps },
                }},
                new Menu { Title = "EQ", Build = f => new List<MenuItem>
                {
                    new MenuItem { Label = "Reset to Flat", Action = ctx => { ctx.State.ResetEq(); ctx.PersistSettings?.Invoke(); } },
                    new MenuItem { Label = "Invert Curve", Action = ctx => { for (int i = 0; i < AppState.EqBandCount; i++) ctx.State.EqGains[i] = -ctx.State.EqGains[i]; ctx.State.EqDirty = true; ctx.PersistSettings?.Invoke(); } },
                    new MenuItem { Label = "Smooth Curve", Action = SmoothEq },
                    MenuItem.Separator(),
                    new MenuItem { Label = "Bass Boost", Action = ctx => ApplyShape(ctx, new float[] { 8, 8, 7, 5, 3, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }) },
                    new MenuItem { Label = "Treble Boost", Action = ctx => ApplyShape(ctx, new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 6, 6 }) },
                    new MenuItem { Label = "Loudness Curve", Action = ctx => ApplyShape(ctx, new float[] { 6, 6, 5, 3, 1, 0, -1, -2, -2, -1, 0, 1, 2, 3, 4, 4, 3 }) },
                }},
                new Menu { Title = "Presets", Build = BuildPresetsMenu },
                new Menu { Title = "Tools", Build = f => new List<MenuItem>
                {
                    new MenuItem { Label = "Always on Top", Action = ctx => { ctx.State.AlwaysOnTop = !ctx.State.AlwaysOnTop; ctx.Host.SetTopmost(ctx.State.AlwaysOnTop); ctx.PersistSettings?.Invoke(); }, Checked = ctx => ctx.State.AlwaysOnTop },
                    new MenuItem { Label = "Reset Window Size (1:1)", Action = ctx => ctx.Host.ResetWindowSize() },
                    MenuItem.Separator(),
                    new MenuItem { Label = "Save Screenshot…", Action = SaveScreenshot },
                    new MenuItem { Label = "Open Settings Folder", Action = ctx => { try { Process.Start(new ProcessStartInfo("explorer.exe", Storage.Folder) { UseShellExecute = true }); } catch { } } },
                }},
                new Menu { Title = "Help", Build = f => new List<MenuItem>
                {
                    new MenuItem { Label = "How It Works", Action = ctx => ctx.Host.ShowMessage("How Fabulosa works",
                        "Fabulosa listens to whatever Windows is playing (WASAPI loopback on the default output device) and visualises it.\n\n" +
                        "• Spectrum / 3D spectrum: 64 log-spaced bands, 20 Hz – 20 kHz. Click L+R to switch channels.\n" +
                        "• VU meters: true 300 ms ballistics, 0 VU = -18 dBFS.\n" +
                        "• Oscilloscope: 5 ms/div, rising-edge triggered. Phase scope: X = left, Y = right.\n" +
                        "• Now Playing and the transport buttons use the Windows media session of the app that is playing.\n\n" +
                        "The equaliser, effects and knobs shape the visualisation (the app is not in the audio path):\n" +
                        "EQ tilts the displayed spectrum, Loudness sets the display gain, Balance weights the meters,\n" +
                        "Stereo Width scales the phase scope, Master is the analysis input gain.\n\n" +
                        "Drag caps and knobs, scroll to nudge them, double-click to reset.") },
                    new MenuItem { Label = "About Fabulosa", Action = ctx => ctx.Host.ShowMessage("Fabulosa", "Fabulosa v1.0\nMusic-reactive visualiser for Windows.\n\nSound. In colour.") },
                }},
            };
        }

        private static bool HasSession(FrameContext ctx) => ctx.Media?.Snapshot.HasSession == true;

        private static string RepeatLabel(FrameContext ctx)
        {
            var mode = ctx.Media?.Snapshot.RepeatMode ?? Windows.Media.MediaPlaybackAutoRepeatMode.None;
            return mode switch { Windows.Media.MediaPlaybackAutoRepeatMode.List => "All", Windows.Media.MediaPlaybackAutoRepeatMode.Track => "One", _ => "Off" };
        }

        private static void SetChannel(FrameContext ctx, SpectrumChannel ch) { ctx.State.SpectrumChannel = ch; ctx.PersistSettings?.Invoke(); }
        private static void SetRelease(FrameContext ctx, float v) { ctx.State.SpectrumReleaseDbPerSec = v; ctx.PersistSettings?.Invoke(); }
        private static void SetFft(FrameContext ctx, int n) { ctx.State.FftSize = n; ctx.Analyzer.FftSize = n; ctx.PersistSettings?.Invoke(); }

        private static void SmoothEq(FrameContext ctx)
        {
            var g = ctx.State.EqGains;
            var copy = (float[])g.Clone();
            for (int i = 0; i < g.Length; i++)
            {
                float prev = copy[Math.Max(0, i - 1)], next = copy[Math.Min(g.Length - 1, i + 1)];
                g[i] = MathF.Round((prev + 2 * copy[i] + next) / 4f * 4f) / 4f;
            }
            ctx.State.EqDirty = true;
            ctx.PersistSettings?.Invoke();
        }

        private static void ApplyShape(FrameContext ctx, float[] shape)
        {
            Array.Copy(shape, ctx.State.EqGains, Math.Min(shape.Length, AppState.EqBandCount));
            ctx.State.EqDirty = true;
            ctx.PersistSettings?.Invoke();
        }

        private static List<MenuItem> BuildPresetsMenu(FrameContext f)
        {
            var items = new List<MenuItem>();
            for (int i = 0; i < f.State.Presets.Count; i++)
            {
                int index = i;
                items.Add(new MenuItem
                {
                    Label = $"{index + 1:00}  {f.State.Presets[index].Name}",
                    Action = ctx => { ctx.State.SelectPreset(index); ctx.PersistSettings?.Invoke(); },
                    Checked = ctx => ctx.State.SelectedPreset == index,
                });
            }
            items.Add(MenuItem.Separator());
            items.Add(new MenuItem { Label = "Save to Selected Preset", Action = PresetsView.SavePreset });
            items.Add(new MenuItem { Label = "Save as New Preset…", Action = PresetsView.SaveAsNew });
            items.Add(new MenuItem { Label = "Delete Selected Preset", Action = PresetsView.DeletePreset, Enabled = ctx => ctx.State.Presets.Count > 1 });
            return items;
        }

        private static void SaveScreenshot(FrameContext ctx)
        {
            string path = ctx.Host.SaveFileDialog("Save screenshot", "PNG image (*.png)|*.png", $"Fabulosa {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png");
            if (!string.IsNullOrEmpty(path)) ctx.Host.SaveScreenshot(path);
        }

        public void Dispose()
        {
            _static?.Dispose();
        }
    }
}
