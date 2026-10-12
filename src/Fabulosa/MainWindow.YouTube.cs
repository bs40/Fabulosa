using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Fabulosa.Model;
using Fabulosa.UI.Views;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SkiaSharp;

namespace Fabulosa
{
    /// <summary>
    /// The embedded YouTube player. Skia cannot draw a video, so a WebView2 control is parked over the panel's
    /// screen area and moved whenever the layout or the window changes. It loads a small local page that drives
    /// YouTube's own IFrame player, and the sound leaves through Windows like any other app, so every visualiser
    /// reacts to whatever is playing.
    /// </summary>
    public partial class MainWindow
    {
        private const string PlayerHost = "fabulosa.player";
        private Canvas _playerLayer;
        private WebView2 _player;
        private bool _playerStarting, _playerReady;
        private string _playerShowing = "";   // the video or playlist currently loaded
        private string _playerMessage;        // what the panel says instead of a video
        private int _playerLoads;
        private Rect _playerPlacement;

        private void InitYouTubeLayer()
        {
            _playerLayer = new Canvas();
            Root.Children.Add(_playerLayer);
        }

        /// <summary>Every frame: parks the player over its panel, or silences and hides it when the panel is off.</summary>
        private void UpdateYouTubePlayer()
        {
            var screen = _renderer.YouTubeScreen;
            if (screen == null)
            {
                if (_player != null && _player.Visibility != Visibility.Collapsed)
                {
                    _player.Visibility = Visibility.Collapsed;
                    StopPlayer();
                }
                return;
            }

            string wanted = YouTubeView.PlayerQuery(_state.YouTubeUrl);
            if (!_playerReady && !_playerStarting) StartPlayer();
            var view = _renderer.ViewOf<YouTubeView>();
            if (view != null) view.Status = PanelMessage(wanted);
            if (_player == null) return;

            Place(screen.Value);
            // menus drop down over this corner, and nothing can be painted over a video window, so it steps aside
            _player.Visibility = _renderer.IsMenuOpen || WindowState == WindowState.Minimized ? Visibility.Hidden : Visibility.Visible;

            if (_playerReady && wanted != null && wanted != _playerShowing)
            {
                _playerShowing = wanted;
                _playerMessage = "Loading…";
                // the counter keeps the address unique, so the page reloads and picks up the new video
                try { _player.CoreWebView2.Navigate($"https://{PlayerHost}/player.html?n={++_playerLoads}#{wanted}"); }
                catch (Exception ex) { _playerMessage = ex.Message; }
            }
        }

        private string PanelMessage(string wanted)
        {
            if (wanted == null) return "No video set\nView ▸ YouTube: Set Video or Playlist…";
            if (_playerMessage != null) return _playerMessage;
            return _playerReady ? "" : "Starting the player…";
        }

        private async void StartPlayer()
        {
            _playerStarting = true;
            try
            {
                CoreWebView2Environment.GetAvailableBrowserVersionString();   // throws when the runtime is missing
                string folder = Path.Combine(Storage.Folder, "player");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "player.html"), PlayerPage());

                var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
                var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Storage.Folder, "WebView2"), options);
                _player = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Black, Visibility = Visibility.Hidden };
                _playerLayer.Children.Add(_player);
                await _player.EnsureCoreWebView2Async(environment);

                var core = _player.CoreWebView2;
                core.SetVirtualHostNameToFolderMapping(PlayerHost, folder, CoreWebView2HostResourceAccessKind.Allow);
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsSwipeNavigationEnabled = false;
                core.NewWindowRequested += (s, e) => { e.Handled = true; OpenInBrowser(e.Uri); };   // links leave for the real browser
                core.WebMessageReceived += OnPlayerMessage;
                _playerMessage = null;
                _playerReady = true;
            }
            catch (WebView2RuntimeNotFoundException)
            {
                _playerMessage = "Needs the Microsoft Edge WebView2 runtime\n(included with Windows 11; install it to use this panel)";
            }
            catch (Exception ex)
            {
                _playerMessage = "The player could not start: " + ex.Message;
                App.LogError(ex);
            }
        }

        private static string PlayerPage()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Fabulosa.player.html");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>The page reports what YouTube's player is doing, so the panel can explain itself.</summary>
        private void OnPlayerMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var json = JsonDocument.Parse(e.TryGetWebMessageAsString());
                string state = json.RootElement.GetProperty("state").GetString();
                string error = json.RootElement.GetProperty("error").GetString();
                _playerMessage = error switch
                {
                    null or "" => state == "loading" || state == "buffering" ? "Loading…" : null,
                    "101" or "150" => "This video cannot be played outside YouTube",
                    "100" => "That video is not available",
                    "2" => "That link was not understood",
                    _ => "The player reported error " + error,
                };
            }
            catch { }
        }

        private void StopPlayer()
        {
            _playerShowing = "";
            _playerMessage = null;
            if (_playerReady && _player?.CoreWebView2 != null)
                try { _player.CoreWebView2.Navigate("about:blank"); } catch { }
        }

        /// <summary>Puts the control exactly over the panel's screen area, through whatever scaling the window applies.</summary>
        private void Place(SKRect designScreen)
        {
            var pixels = _renderer.ToSurface(designScreen);
            var dpi = VisualTreeHelper.GetDpi(this);
            var map = Surface.TransformToAncestor(Root);
            var topLeft = map.Transform(new Point(pixels.Left / dpi.DpiScaleX, pixels.Top / dpi.DpiScaleY));
            var bottomRight = map.Transform(new Point(pixels.Right / dpi.DpiScaleX, pixels.Bottom / dpi.DpiScaleY));
            var rect = new Rect(topLeft, bottomRight);
            if (rect == _playerPlacement) return;
            _playerPlacement = rect;
            Canvas.SetLeft(_player, rect.X);
            Canvas.SetTop(_player, rect.Y);
            _player.Width = Math.Max(1, rect.Width);
            _player.Height = Math.Max(1, rect.Height);
        }

        private static void OpenInBrowser(string uri)
        {
            if (!uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
            try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
        }

        private void DisposeYouTubePlayer()
        {
            try { _player?.Dispose(); } catch { }
            _player = null;
            _playerReady = false;
        }
    }
}
