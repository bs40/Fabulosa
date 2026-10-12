using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        private readonly List<View> _visible = new List<View>();
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
        private readonly Dictionary<View, double> _profileMs = new Dictionary<View, double>();

        public FrameContext Ctx { get; }

        public Renderer(FrameContext ctx)
        {
            Ctx = ctx;
            _chrome = new ChromeView();
            _bottomBar = new BottomBarView();
            _menus = new MenuSystem(BuildMenus());

            _views.Add(new NowPlayingView());
            _views.Add(new YouTubeView());
            _views.Add(new SpectrumView());
            _views.Add(new LevelMetersView());
            _views.Add(new DancerView());
            _views.Add(new OscilloscopeView());
            _views.Add(new PhaseScopeView());
            _views.Add(new Spectrum3DView());
            _views.Add(new RadialSpectrumView());
            _views.Add(_bottomBar);

            ctx.SetStatus = msg => { _bottomBar.Status = msg; _bottomBar.StatusUntil = ctx.Time + 3f; };
            ctx.MarkStaticDirty = () => _staticDirty = true;
            ctx.RearrangePanels = ArrangePanels;
            ArrangePanels();
        }

        /// <summary>Shows the visualisers the user picked and lays them out; hidden ones neither draw nor take input.</summary>
        public void ArrangePanels()
        {
            bool youTube = Ctx.State.ShowYouTube, dancer = Ctx.State.IsVisible(Visualiser.Dancer);
            var placed = Layout.Arrange(Ctx.State.IsVisible, youTube && !dancer);
            _visible.Clear();
            foreach (var view in _views)
            {
                if (view is NowPlayingView) { if (youTube) continue; }   // the player takes over its slot
                else if (view is YouTubeView) { if (!youTube) continue; view.SetBounds(Layout.YouTubeSlot(dancer)); }
                else if (view.PanelId is Visualiser id)
                {
                    if (!placed.TryGetValue(id, out var bounds)) continue;
                    view.SetBounds(bounds);
                }
                _visible.Add(view);
            }
            if (_captured != null && !_visible.Contains(_captured)) _captured = null;
            _staticDirty = true;
        }

        public bool IsShown(Visualiser v) => _visible.Any(view => view.PanelId == v);
        public SKRect BoundsOf(Visualiser v) => _views.First(view => view.PanelId == v).Bounds;
        public T ViewOf<T>() where T : View => _views.OfType<T>().FirstOrDefault();

        /// <summary>Where the embedded YouTube player must sit, in design coordinates, or null when it is not shown.</summary>
        public SKRect? YouTubeScreen
        {
            get { foreach (var v in _visible) if (v is YouTubeView yt) return yt.Screen; return null; }
        }

        /// <summary>Maps a design rectangle onto the pixels of the surface being drawn into.</summary>
        public SKRect ToSurface(SKRect design) =>
            new SKRect(_offX + design.Left * _scale, _offY + design.Top * _scale, _offX + design.Right * _scale, _offY + design.Bottom * _scale);

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
            foreach (var v in _visible) v.DrawStatic(c, Ctx);
            if (!AppState.AllVisualisers.Any(Ctx.State.IsVisible))
                Theme.Text(c, "Pick visualisers from the View menu", Layout.VisualiserArea.MidX, Layout.VisualiserArea.MidY, 16f, Theme.TextDim, Theme.Sans, SKTextAlign.Center, 1f);
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
            foreach (var v in _visible)
            {
                if (!Profile) { v.Draw(canvas, Ctx); continue; }
                long t0 = Stopwatch.GetTimestamp();
                v.Draw(canvas, Ctx);
                double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                _profileMs[v] = _profileMs.TryGetValue(v, out double avg) ? avg * 0.9 + ms * 0.1 : ms;
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

        public string ProfileReport()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var v in _visible)
                if (_profileMs.TryGetValue(v, out double ms)) sb.Append(v.GetType().Name).Append('=').Append(ms.ToString("0.00")).Append("ms\n");
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

        /// <summary>Centre of a menu-bar title (self-test aid).</summary>
        public SKPoint MenuTitleCentre(string title)
        {
            var r = _chrome.MenuRect(Array.IndexOf(Layout.MenuTitles, title));
            return new SKPoint(r.MidX, r.MidY);
        }

        /// <summary>Centre of an item in the open menu, or null (self-test aid).</summary>
        public SKPoint? MenuItemCentre(string label) => _menus.ItemCentre(label, Ctx);

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
                _menus.Close();
                return false;
            }

            int title = HitMenuTitle(p);
            if (title >= 0) { _menus.Open(title, _chrome.MenuRect(title), Ctx); return false; }

            if (Layout.BtnMinimize.Contains(p)) { Ctx.Host.Minimize(); return false; }
            if (Layout.BtnMaximize.Contains(p)) { Ctx.Host.ToggleMaximize(); return false; }
            if (Layout.BtnClose.Contains(p)) { Ctx.Host.CloseWindow(); return false; }

            foreach (var v in _visible)
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
            foreach (var v in _visible)
                if (v.HitTest(p) && v.OnWheel(p, delta, Ctx)) return;
        }

        public void CloseMenus() => _menus.Close();
        public bool IsMenuOpen => _menus.IsOpen;

        // ---- menus (same order as Layout.MenuTitles) ------------------------------------

        private List<Menu> BuildMenus()
        {
            return new List<Menu>
            {
                new Menu { Title = "File", Build = f => new List<MenuItem>
                {
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
                new Menu { Title = "View", Build = f =>
                {
                    // the picker: ticks stay open so several visualisers can be switched in one go
                    var items = AppState.AllVisualisers.Select(v => new MenuItem
                    {
                        Label = AppState.NameOf(v),
                        KeepOpen = true,
                        Checked = ctx => ctx.State.IsVisible(v),
                        Action = ctx => ShowVisualisers(ctx, !ctx.State.IsVisible(v), v),
                    }).ToList();
                    items.Add(MenuItem.Separator());
                    items.Add(new MenuItem { Label = "YouTube Player", KeepOpen = true, Checked = ctx => ctx.State.ShowYouTube, Action = ToggleYouTube });
                    items.Add(new MenuItem { Label = "YouTube: Set Video or Playlist…", Action = SetYouTubeLink });
                    items.Add(MenuItem.Separator());
                    items.Add(new MenuItem { Label = "Show All", KeepOpen = true, Enabled = ctx => ctx.State.HiddenVisualisers.Count > 0, Action = ctx => ShowVisualisers(ctx, true, AppState.AllVisualisers) });
                    return items;
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
                    new MenuItem { Label = "Dancer: 1 frame per beat", Action = ctx => SetDancerRate(ctx, 1f), Checked = ctx => IsDancerRate(ctx, 1f) },
                    new MenuItem { Label = "Dancer: 2 frames per beat", Action = ctx => SetDancerRate(ctx, 2f), Checked = ctx => IsDancerRate(ctx, 2f) },
                    new MenuItem { Label = "Dancer: 4 frames per beat", Action = ctx => SetDancerRate(ctx, 4f), Checked = ctx => IsDancerRate(ctx, 4f) },
                    new MenuItem { Label = "Dancer: 8 frames per beat", Action = ctx => SetDancerRate(ctx, 8f), Checked = ctx => IsDancerRate(ctx, 8f) },
                    MenuItem.Separator(),
                    new MenuItem { Label = "Radial: Filled", Action = ctx => SetRadialLines(ctx, false), Checked = ctx => !ctx.State.RadialLines },
                    new MenuItem { Label = "Radial: Lines", Action = ctx => SetRadialLines(ctx, true), Checked = ctx => ctx.State.RadialLines },
                    new MenuItem { Label = "Radial trail: Short", Action = ctx => SetRadialTrail(ctx, 0.4f), Checked = ctx => IsRadialTrail(ctx, 0.4f) },
                    new MenuItem { Label = "Radial trail: Long", Action = ctx => SetRadialTrail(ctx, 1.1f), Checked = ctx => IsRadialTrail(ctx, 1.1f) },
                    new MenuItem { Label = "Radial trail: Off", Action = ctx => SetRadialTrail(ctx, 0.08f), Checked = ctx => IsRadialTrail(ctx, 0.08f) },
                    MenuItem.Separator(),
                    new MenuItem { Label = "Show Frame Rate", Action = ctx => ctx.State.ShowFps = !ctx.State.ShowFps, Checked = ctx => ctx.State.ShowFps },
                }},
                new Menu { Title = "Tools", Build = f => new List<MenuItem>
                {
                    new MenuItem { Label = "Always on Top", Action = ctx => { ctx.State.AlwaysOnTop = !ctx.State.AlwaysOnTop; ctx.Host.SetTopmost(ctx.State.AlwaysOnTop); ctx.PersistSettings?.Invoke(); }, Checked = ctx => ctx.State.AlwaysOnTop },
                    new MenuItem { Label = "Reset Window Size (1:1)", Action = ctx => ctx.Host.ResetWindowSize() },
                    new MenuItem { Label = "Performance Mode (draw at 1672 x 941 and scale up)", Action = TogglePerformanceMode, Checked = ctx => ctx.State.PerformanceMode },
                    MenuItem.Separator(),
                    new MenuItem { Label = "Save Screenshot…", Action = SaveScreenshot },
                    new MenuItem { Label = "Open Settings Folder", Action = ctx => { try { Process.Start(new ProcessStartInfo("explorer.exe", Storage.Folder) { UseShellExecute = true }); } catch { } } },
                }},
                new Menu { Title = "Help", Build = f => new List<MenuItem>
                {
                    new MenuItem { Label = "How It Works", Action = ctx => ctx.Host.ShowMessage("How Fabulosa works",
                        "Fabulosa listens to whatever Windows is playing (WASAPI loopback on the default output device) and visualises it.\n\n" +
                        "• Spectrum / 3D spectrum: 64 log-spaced bands, 20 Hz – 20 kHz. Click L+R to switch channels.\n" +
                        "• Radial spectrum: a spoke per band, right channel down one side and left channel up the other. The newest\n" +
                        "  spikes are worked out every frame and flow outward (Visualisation menu: filled or lines, trail length).\n" +
                        "• VU meters: 150 ms ballistics (twice classic VU speed), 0 VU = -14 dBFS.\n" +
                        "• Oscilloscope: 5 ms/div, rising-edge triggered. Phase scope: X = left, Y = right.\n" +
                        "• Dancer: a beat tracker follows the kick and snare, estimates the tempo and steps the figure\n" +
                        "  through its poses, four frames per beat (the Visualisation menu changes the rate).\n" +
                        "• Now Playing and the transport buttons use the Windows media session of the app that is playing.\n" +
                        "• YouTube: the View menu can put YouTube's own player in the Now Playing slot; its sound drives every panel.\n\n" +
                        "The View menu picks which visualisers are shown; the choice is remembered.") },
                    new MenuItem { Label = "About Fabulosa", Action = ctx => ctx.Host.ShowMessage("Fabulosa", "Fabulosa v3.2\nMusic-reactive visualiser for Windows.") },
                }},
            };
        }

        private static void ShowVisualisers(FrameContext ctx, bool visible, params Visualiser[] which)
        {
            foreach (var v in which) ctx.State.SetVisible(v, visible);
            ctx.RearrangePanels?.Invoke();
            ctx.PersistSettings?.Invoke();
        }

        private static void ToggleYouTube(FrameContext ctx)
        {
            ctx.State.ShowYouTube = !ctx.State.ShowYouTube;
            ctx.RearrangePanels?.Invoke();
            ctx.PersistSettings?.Invoke();
        }

        /// <summary>Asks for a link and keeps it only when it is a YouTube video or playlist.</summary>
        private static void SetYouTubeLink(FrameContext ctx)
        {
            string entered = ctx.Host.PromptText("YouTube", "Video or playlist link:", ctx.State.YouTubeUrl);
            if (entered == null) return;
            entered = entered.Trim();
            if (entered.Length > 0 && YouTubeView.PlayerQuery(entered) == null)
            {
                ctx.Host.ShowMessage("YouTube", "That does not look like a YouTube video or playlist link.");
                return;
            }
            ctx.State.YouTubeUrl = entered;
            if (entered.Length > 0) ctx.State.ShowYouTube = true;
            ctx.RearrangePanels?.Invoke();
            ctx.PersistSettings?.Invoke();
            ctx.SetStatus?.Invoke(entered.Length > 0 ? "YouTube link set" : "YouTube link cleared");
        }

        private static void SetRadialLines(FrameContext ctx, bool lines) { ctx.State.RadialLines = lines; ctx.PersistSettings?.Invoke(); }
        private static void SetRadialTrail(FrameContext ctx, float seconds) { ctx.State.RadialTrailSeconds = seconds; ctx.PersistSettings?.Invoke(); }
        private static bool IsRadialTrail(FrameContext ctx, float seconds) => Math.Abs(ctx.State.RadialTrailSeconds - seconds) < 0.01f;

        private static void TogglePerformanceMode(FrameContext ctx)
        {
            ctx.State.PerformanceMode = !ctx.State.PerformanceMode;
            ctx.Host.SetPerformanceMode(ctx.State.PerformanceMode);
            ctx.PersistSettings?.Invoke();
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
        private static void SetDancerRate(FrameContext ctx, float v) { ctx.State.DancerFramesPerBeat = v; ctx.PersistSettings?.Invoke(); }
        private static bool IsDancerRate(FrameContext ctx, float v) => Math.Abs(ctx.State.DancerFramesPerBeat - v) < 0.01f;

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
