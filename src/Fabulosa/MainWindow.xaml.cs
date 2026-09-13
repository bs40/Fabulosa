using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Fabulosa.Audio;
using Fabulosa.Media;
using Fabulosa.Model;
using Fabulosa.UI;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Fabulosa
{
    public partial class MainWindow : Window, IHost
    {
        private readonly AppState _state = new AppState();
        private readonly StereoRingBuffer _buffer = new StereoRingBuffer(192000);
        private readonly AudioCapture _capture;
        private readonly Analyzer _analyzer;
        private readonly MediaSessionService _media = new MediaSessionService();
        private readonly Renderer _renderer;
        private readonly FrameContext _ctx;
        private readonly Stopwatch _frameTimer = Stopwatch.StartNew();
        private SettingsFile _settings;
        private DispatcherTimer _saveTimer;
        private bool _settingsDirty;
        private double _lastFrame;
        private bool _closing;
        private float _pendingDt = 1f / 60f;

        public MainWindow()
        {
            InitializeComponent();
            Theme.Init();

            _settings = Storage.LoadSettings();
            Storage.ApplySettings(_settings, _state);

            _capture = new AudioCapture(_buffer);
            _analyzer = new Analyzer(_buffer, _state) { FftSize = _state.FftSize };

            _ctx = new FrameContext
            {
                State = _state,
                Analyzer = _analyzer,
                Capture = _capture,
                Media = _media,
                Host = this,
                PersistSettings = QueueSaveSettings,
            };
            _renderer = new Renderer(_ctx);

            RestoreWindowPlacement();
            Topmost = _state.AlwaysOnTop;

            Surface.MouseMove += OnMouseMove;
            Surface.MouseLeftButtonDown += OnMouseLeftDown;
            Surface.MouseLeftButtonUp += OnMouseLeftUp;
            Surface.MouseLeave += (s, e) => _renderer.MouseLeave();
            Surface.MouseWheel += OnMouseWheel;
            KeyDown += OnKeyDown;
            Loaded += OnLoaded;
            Closing += OnClosing;
            StateChanged += (s, e) => _renderer.InvalidateStatic();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _capture.Start();
            _analyzer.SampleRate = _capture.SampleRate;
            _media.Start();
            CompositionTarget.Rendering += OnRendering;

            if (!string.IsNullOrEmpty(App.SelfTestPath))
            {
                var st = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                st.Tick += (s, a) => { st.Stop(); RunSelfTest(App.SelfTestPath); Close(); };
                st.Start();
            }

            if (!string.IsNullOrEmpty(App.WindowShotPath))
            {
                // grabs the window as it actually appears on screen (DWM-composed), unlike --screenshot which renders offscreen
                var wt = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(App.ScreenshotDelayMs) };
                wt.Tick += (s, a) =>
                {
                    wt.Stop();
                    try
                    {
                        CaptureWindowFromScreen(App.WindowShotPath);
                        File.WriteAllText(App.WindowShotPath + ".stats.txt",
                            $"window={ActualWidth:0}x{ActualHeight:0} state={WindowState}\ncapture={_capture.Status} {_capture.DeviceName}\npeak_db={_analyzer.PeakDb:0.0}\nrms_db={_analyzer.RmsDb:0.0}\nsignal={_analyzer.SignalPresent}\n" + BeatStats());
                    }
                    catch (Exception ex) { App.LogError(ex); }
                    if (string.IsNullOrEmpty(App.ScreenshotPath)) Close();
                };
                wt.Start();
            }

            if (!string.IsNullOrEmpty(App.ScreenshotPath))
            {
                _renderer.Profile = true;
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(App.ScreenshotDelayMs) };
                t.Tick += (s, a) =>
                {
                    t.Stop();
                    try
                    {
                        SaveScreenshot(App.ScreenshotPath);
                        File.WriteAllText(App.ScreenshotPath + ".stats.txt",
                            $"fps={_renderer.Fps:0.0}\nrender_ms={_renderer.LastRenderMs:0.00}\ncapture={_capture.Status} {_capture.DeviceName} {_capture.FormatDescription}\n" +
                            $"peak_db={_analyzer.PeakDb:0.0}\nrms_db={_analyzer.RmsDb:0.0}\nvu_l={_analyzer.VuLeft:0.000}\nvu_r={_analyzer.VuRight:0.000}\nsignal={_analyzer.SignalPresent}\n" +
                            $"media={_media.Snapshot.HasSession} {_media.Snapshot.Title}\n" + BeatStats() + _renderer.ProfileReport());
                    }
                    catch (Exception ex) { App.LogError(ex); }
                    if (App.ExitAfterScreenshot) Close();
                };
                t.Start();
            }
        }

        private string BeatStats()
        {
            var b = _analyzer.Beat;
            return $"bpm={b.Bpm:0.0}\nbeats={b.BeatCount}\nlocked={b.Locked}\nconfidence={b.Confidence:0.00}\n";
        }

        private void OnRendering(object sender, EventArgs e)
        {
            double now = _frameTimer.Elapsed.TotalSeconds;
            float dt = (float)(now - _lastFrame);
            _lastFrame = now;
            if (dt <= 0 || dt > 0.25f) dt = 1f / 60f;
            if (_capture.SampleRate != _analyzer.SampleRate) _analyzer.SampleRate = _capture.SampleRate;
            if (_analyzer.FftSize != _state.FftSize) _analyzer.FftSize = _state.FftSize;
            try { _analyzer.Update(dt); }
            catch (Exception ex) { App.LogError(ex); }
            _pendingDt = dt;
            Surface.InvalidateVisual();
        }

        private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            try { _renderer.Render(canvas, e.Info.Width, e.Info.Height, _pendingDt); }
            catch (Exception ex) { App.LogError(ex); }
        }

        // ---- input ------------------------------------------------------------------------

        private SKPoint DesignPoint(MouseEventArgs e)
        {
            var p = e.GetPosition(Surface);
            var dpi = VisualTreeHelper.GetDpi(this);
            return _renderer.ToDesign((float)(p.X * dpi.DpiScaleX), (float)(p.Y * dpi.DpiScaleY));
        }

        private void OnMouseMove(object sender, MouseEventArgs e) => _renderer.MouseMove(DesignPoint(e));

        private void OnMouseLeftDown(object sender, MouseButtonEventArgs e)
        {
            var p = DesignPoint(e);
            bool drag = _renderer.MouseDown(p, e.ClickCount);
            if (drag)
            {
                _renderer.MouseUp(p);
                try { DragMove(); } catch { }
            }
            else
            {
                Surface.CaptureMouse();
            }
            e.Handled = true;
        }

        private void OnMouseLeftUp(object sender, MouseButtonEventArgs e)
        {
            _renderer.MouseUp(DesignPoint(e));
            if (Surface.IsMouseCaptured) Surface.ReleaseMouseCapture();
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e) => _renderer.Wheel(DesignPoint(e), e.Delta);

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape: _renderer.CloseMenus(); break;
                case Key.Space: _media.TogglePlayPause(); e.Handled = true; break;
                case Key.MediaNextTrack: _media.Next(); break;
                case Key.MediaPreviousTrack: _media.Previous(); break;
                case Key.F11: ToggleMaximize(); break;
            }
        }

        // ---- IHost ------------------------------------------------------------------------

        public void DragWindow() { try { DragMove(); } catch { } }
        public void Minimize() => WindowState = WindowState.Minimized;
        public void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        public void CloseWindow() => Close();
        public bool IsMaximized => WindowState == WindowState.Maximized;
        public void SetTopmost(bool on) => Topmost = on;

        public void ResetWindowSize()
        {
            WindowState = WindowState.Normal;
            var dpi = VisualTreeHelper.GetDpi(this);
            Width = Layout.Width / dpi.DpiScaleX;
            Height = Layout.Height / dpi.DpiScaleY;
            ClampToWorkArea();
        }

        public string OpenFileDialog(string title, string filter)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
            return dlg.ShowDialog(this) == true ? dlg.FileName : null;
        }

        public string SaveFileDialog(string title, string filter, string defaultName)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Title = title, Filter = filter, FileName = defaultName, AddExtension = true };
            return dlg.ShowDialog(this) == true ? dlg.FileName : null;
        }

        public void ShowMessage(string title, string text) => MessageBox.Show(this, text, title, MessageBoxButton.OK, MessageBoxImage.Information);

        public void SaveScreenshot(string path)
        {
            _renderer.RenderToPng(path);
            _ctx.SetStatus?.Invoke("Screenshot saved");
        }

        public void RestartCapture()
        {
            _capture.Start();
            _analyzer.SampleRate = _capture.SampleRate;
            _ctx.SetStatus?.Invoke("Capture restarted: " + _capture.DeviceName);
        }

        public string PromptText(string title, string prompt, string initial)
        {
            var dlg = new Window
            {
                Title = title, Owner = this, Width = 360, Height = 150, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.FromRgb(0x14, 0x1a, 0x1b)),
                WindowStyle = WindowStyle.ToolWindow,
            };
            var panel = new StackPanel { Margin = new Thickness(14) };
            panel.Children.Add(new TextBlock { Text = prompt, Foreground = Brushes.Gainsboro, Margin = new Thickness(0, 0, 0, 6) });
            var box = new TextBox { Text = initial ?? "", Margin = new Thickness(0, 0, 0, 10) };
            box.SelectAll();
            panel.Children.Add(box);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "OK", Width = 70, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancel = new Button { Content = "Cancel", Width = 70, IsCancel = true };
            ok.Click += (s, e) => { dlg.DialogResult = true; dlg.Close(); };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            dlg.Content = panel;
            box.Focus();
            return dlg.ShowDialog() == true ? box.Text : null;
        }

        /// <summary>Renders the live WPF visual tree (i.e. what the SKElement actually painted) to a PNG.</summary>
        private void CaptureWindowFromScreen(string path)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            int w = (int)Math.Round(Root.ActualWidth * dpi.DpiScaleX), h = (int)Math.Round(Root.ActualHeight * dpi.DpiScaleY);
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            rtb.Render(Root);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using var fs = File.Create(path);
            encoder.Save(fs);
        }

        // ---- self test (drives the renderer's hit-testing in design coordinates) ---------------

        private void RunSelfTest(string reportPath)
        {
            var sb = new StringBuilder();
            int pass = 0, fail = 0;
            void Check(string name, bool ok) { sb.AppendLine((ok ? "PASS " : "FAIL ") + name); if (ok) pass++; else fail++; }
            void Click(float x, float y, int clicks = 1) { _renderer.MouseDown(new SKPoint(x, y), clicks); _renderer.MouseUp(new SKPoint(x, y)); }

            var savedPersist = _ctx.PersistSettings;
            _ctx.PersistSettings = () => { };
            var initialChannel = _state.SpectrumChannel;
            bool initialPeakHold = _state.PeakHoldEnabled;
            float initialSteps = _state.DancerStepsPerBeat;
            try
            {
                Check("renderer produced frames", _renderer.Fps > 0 || _renderer.LastRenderMs > 0);
                Check("audio capture running", _capture.IsRunning);

                Click(1060, 129);
                Check("spectrum L+R label cycles channel", _state.SpectrumChannel != initialChannel);

                // Visualisation menu: "Peak Hold" is the fourth row (three items, then a separator)
                Click(230, 72);
                Check("menu opens", _renderer.IsMenuOpen);
                Click(250, 197);
                Check("menu item toggles peak hold and closes menu", _state.PeakHoldEnabled != initialPeakHold && !_renderer.IsMenuOpen);

                // "Dancer: 2 steps per beat" sits after Peak Hold, three releases, a separator, three FFT sizes, a separator, mesh lines and the 1-step item
                Click(230, 72);
                Click(250, 440);
                Check("menu sets dancer to 2 steps per beat", Math.Abs(_state.DancerStepsPerBeat - 2f) < 0.01f && !_renderer.IsMenuOpen);

                bool drag = _renderer.MouseDown(new SKPoint(700, 30), 1);
                _renderer.MouseUp(new SKPoint(700, 30));
                Check("title bar press requests window drag", drag);
                bool overPanel = _renderer.MouseDown(new SKPoint(1000, 128), 1);
                _renderer.MouseUp(new SKPoint(1000, 128));
                Check("panel press does not drag the window", !overPanel);

                string png = reportPath + ".png";
                _renderer.RenderToPng(png);
                Check("offscreen render writes a PNG", File.Exists(png) && new FileInfo(png).Length > 100000);

                var r128 = RunSyntheticTrack(128, 12);
                sb.AppendLine($"  128 BPM track -> {r128.bpm:0.0} BPM, {r128.beats} beats, confidence {r128.conf:0.00}");
                Check("beat tracker finds 128 BPM (+/- 2)", Math.Abs(r128.bpm - 128f) <= 2f);
                Check("beat tracker emits about one beat per beat", r128.beats >= 20 && r128.beats <= 30);
                var r90 = RunSyntheticTrack(90, 12);
                sb.AppendLine($"  90 BPM track -> {r90.bpm:0.0} BPM, {r90.beats} beats, confidence {r90.conf:0.00}");
                Check("beat tracker finds 90 BPM (+/- 2)", Math.Abs(r90.bpm - 90f) <= 2f);
                Check("beat tracker keeps count at 90 BPM", r90.beats >= 14 && r90.beats <= 22);
                var silent = RunSyntheticTrack(0, 6);
                Check("silence produces no beats", silent.beats == 0 && silent.bpm == 0);
            }
            catch (Exception ex)
            {
                sb.AppendLine("EXCEPTION " + ex);
                fail++;
            }
            finally
            {
                _ctx.PersistSettings = savedPersist;
                _state.SpectrumChannel = initialChannel;
                _state.PeakHoldEnabled = initialPeakHold;
                _state.DancerStepsPerBeat = initialSteps;
            }
            sb.AppendLine($"{pass} passed, {fail} failed");
            File.WriteAllText(reportPath, sb.ToString());
        }

        /// <summary>Feeds a synthetic kick-on-every-beat track through a fresh tracker; bpm = 0 gives silence.</summary>
        private static (float bpm, int beats, float conf) RunSyntheticTrack(double bpm, double seconds)
        {
            const int sr = 48000;
            var bt = new BeatTracker();
            bt.Reset(sr);
            int n = (int)(seconds * sr);
            var l = new float[4096];
            var r = new float[4096];
            double beatLen = bpm > 0 ? 60.0 / bpm : 0;
            for (int start = 0; start < n; start += 4096)
            {
                int count = Math.Min(4096, n - start);
                for (int i = 0; i < count; i++)
                {
                    float v = 0f;
                    if (beatLen > 0)
                    {
                        double t = (start + i) / (double)sr;
                        double tb = t % beatLen;
                        double kick = Math.Sin(2 * Math.PI * (50 + 80 * Math.Exp(-tb * 30)) * tb) * Math.Exp(-tb * 12) * 0.8;
                        double pad = 0.03 * Math.Sin(2 * Math.PI * 220 * t);
                        v = (float)(kick + pad);
                    }
                    l[i] = v; r[i] = v;
                }
                bt.Process(l, r, 0, count);
            }
            return (bt.Bpm, bt.BeatCount, bt.Confidence);
        }

        // ---- persistence ------------------------------------------------------------------

        private void QueueSaveSettings()
        {
            _settingsDirty = true;
            if (_saveTimer == null)
            {
                _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); if (_settingsDirty) SaveSettingsNow(); };
            }
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void SaveSettingsNow()
        {
            _settingsDirty = false;
            _settings = Storage.CaptureSettings(_state, _settings);
            if (WindowState == WindowState.Normal)
            {
                _settings.WindowLeft = Left; _settings.WindowTop = Top;
                _settings.WindowWidth = Width; _settings.WindowHeight = Height;
            }
            _settings.Maximized = WindowState == WindowState.Maximized;
            Storage.SaveSettings(_settings);
        }

        private void RestoreWindowPlacement()
        {
            var s = _settings;
            if (s.WindowWidth > 200 && s.WindowHeight > 100 && !double.IsNaN(s.WindowLeft) && !double.IsNaN(s.WindowTop))
            {
                Left = s.WindowLeft; Top = s.WindowTop; Width = s.WindowWidth; Height = s.WindowHeight;
                WindowStartupLocation = WindowStartupLocation.Manual;
            }
            ClampToWorkArea();
            if (s.Maximized) WindowState = WindowState.Maximized;
        }

        private void ClampToWorkArea()
        {
            var area = SystemParameters.WorkArea;
            if (Width > area.Width || Height > area.Height)
            {
                // keep the design aspect ratio when the native size does not fit the screen
                double scale = Math.Min(area.Width / Width, area.Height / Height);
                Width = Math.Floor(Width * scale);
                Height = Math.Floor(Height * scale);
            }
            if (WindowStartupLocation == WindowStartupLocation.Manual)
            {
                if (Left + Width > area.Right) Left = Math.Max(area.Left, area.Right - Width);
                if (Top + Height > area.Bottom) Top = Math.Max(area.Top, area.Bottom - Height);
                if (Left < area.Left) Left = area.Left;
                if (Top < area.Top) Top = area.Top;
            }
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_closing) return;
            _closing = true;
            CompositionTarget.Rendering -= OnRendering;
            try { SaveSettingsNow(); } catch { }
            _media.Dispose();
            _capture.Dispose();
            _renderer.Dispose();
        }
    }
}
