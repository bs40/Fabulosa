using System;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Fabulosa.Media
{
    public enum PlayState { None, Playing, Paused, Stopped }

    /// <summary>Immutable snapshot of what Windows reports as the current media session.</summary>
    public sealed class MediaSnapshot
    {
        public bool HasSession;
        public string Title = "";
        public string Artist = "";
        public string Album = "";
        public string SourceApp = "";
        public PlayState State = PlayState.None;
        public TimeSpan Position;
        public TimeSpan Duration;
        public DateTimeOffset PositionTimestamp;
        public double PlaybackRate = 1;
        public bool ShuffleActive;
        public MediaPlaybackAutoRepeatMode RepeatMode = MediaPlaybackAutoRepeatMode.None;
        public bool CanNext, CanPrevious, CanPlayPause, CanShuffle, CanRepeat;
        public byte[] Thumbnail;
        public int ThumbnailVersion;

        /// <summary>Position extrapolated from the last timeline update while playing.</summary>
        public TimeSpan CurrentPosition
        {
            get
            {
                if (State != PlayState.Playing) return Position;
                var elapsed = DateTimeOffset.UtcNow - PositionTimestamp;
                var p = Position + TimeSpan.FromSeconds(elapsed.TotalSeconds * PlaybackRate);
                if (Duration > TimeSpan.Zero && p > Duration) p = Duration;
                return p;
            }
        }
    }

    /// <summary>
    /// Wraps the Windows GlobalSystemMediaTransportControls session manager: reads the
    /// current session's metadata/timeline and sends transport commands. All WinRT events
    /// are folded into a snapshot that the UI thread reads once per frame.
    /// </summary>
    public sealed class MediaSessionService : IDisposable
    {
        private GlobalSystemMediaTransportControlsSessionManager _manager;
        private GlobalSystemMediaTransportControlsSession _session;
        private readonly object _gate = new object();
        private MediaSnapshot _snapshot = new MediaSnapshot();
        private int _thumbVersion;
        private string _lastThumbKey;
        private bool _disposed;

        public MediaSnapshot Snapshot { get { lock (_gate) return _snapshot; } }
        public bool Available { get; private set; }

        public async void Start()
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager.CurrentSessionChanged += (s, e) => AttachCurrent();
                _manager.SessionsChanged += (s, e) => AttachCurrent();
                Available = true;
                AttachCurrent();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Media session manager unavailable: " + ex.Message);
                Available = false;
            }
        }

        private void AttachCurrent()
        {
            if (_disposed || _manager == null) return;
            GlobalSystemMediaTransportControlsSession session = null;
            try { session = _manager.GetCurrentSession(); } catch { }
            lock (_gate)
            {
                if (_session != null && !ReferenceEquals(_session, session))
                {
                    try
                    {
                        _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                        _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                        _session.TimelinePropertiesChanged -= OnTimelineChanged;
                    }
                    catch { }
                }
                _session = session;
                if (_session != null)
                {
                    _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                    _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
                    _session.TimelinePropertiesChanged += OnTimelineChanged;
                }
            }
            _ = RefreshAllAsync();
        }

        private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => _ = RefreshAllAsync();
        private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => RefreshPlayback();
        private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => RefreshPlayback();

        private async Task RefreshAllAsync()
        {
            var session = _session;
            if (session == null)
            {
                lock (_gate) _snapshot = new MediaSnapshot();
                return;
            }
            try
            {
                var props = await session.TryGetMediaPropertiesAsync();
                byte[] thumb = null;
                string thumbKey = (props?.Title ?? "") + "|" + (props?.Artist ?? "") + "|" + (props?.AlbumTitle ?? "");
                bool sameThumb = thumbKey == _lastThumbKey;
                if (props?.Thumbnail != null && !sameThumb)
                {
                    try
                    {
                        using var stream = await props.Thumbnail.OpenReadAsync();
                        if (stream != null && stream.Size > 0 && stream.Size < 16 * 1024 * 1024)
                        {
                            using var reader = new DataReader(stream);
                            uint size = (uint)stream.Size;
                            await reader.LoadAsync(size);
                            thumb = new byte[size];
                            reader.ReadBytes(thumb);
                        }
                    }
                    catch { thumb = null; }
                }
                lock (_gate)
                {
                    var old = _snapshot;
                    var snap = Copy(old);
                    snap.HasSession = true;
                    snap.Title = props?.Title ?? "";
                    snap.Artist = props?.Artist ?? "";
                    snap.Album = props?.AlbumTitle ?? "";
                    snap.SourceApp = session.SourceAppUserModelId ?? "";
                    if (!sameThumb)
                    {
                        snap.Thumbnail = thumb;
                        snap.ThumbnailVersion = ++_thumbVersion;
                        _lastThumbKey = thumbKey;
                    }
                    _snapshot = snap;
                }
                RefreshPlayback();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("media refresh failed: " + ex.Message);
            }
        }

        private void RefreshPlayback()
        {
            var session = _session;
            if (session == null) return;
            try
            {
                var info = session.GetPlaybackInfo();
                var timeline = session.GetTimelineProperties();
                lock (_gate)
                {
                    var snap = Copy(_snapshot);
                    snap.HasSession = true;
                    snap.State = info.PlaybackStatus switch
                    {
                        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlayState.Playing,
                        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlayState.Paused,
                        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => PlayState.Stopped,
                        _ => PlayState.None,
                    };
                    snap.PlaybackRate = info.PlaybackRate ?? 1.0;
                    snap.ShuffleActive = info.IsShuffleActive ?? false;
                    snap.RepeatMode = info.AutoRepeatMode ?? MediaPlaybackAutoRepeatMode.None;
                    snap.CanNext = info.Controls.IsNextEnabled;
                    snap.CanPrevious = info.Controls.IsPreviousEnabled;
                    snap.CanPlayPause = info.Controls.IsPlayEnabled || info.Controls.IsPauseEnabled || info.Controls.IsPlayPauseToggleEnabled;
                    snap.CanShuffle = info.Controls.IsShuffleEnabled;
                    snap.CanRepeat = info.Controls.IsRepeatEnabled;
                    snap.Position = timeline.Position;
                    snap.Duration = timeline.EndTime - timeline.StartTime;
                    snap.PositionTimestamp = timeline.LastUpdatedTime;
                    if (snap.PositionTimestamp == default) snap.PositionTimestamp = DateTimeOffset.UtcNow;
                    _snapshot = snap;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("playback refresh failed: " + ex.Message);
            }
        }

        private static MediaSnapshot Copy(MediaSnapshot s)
        {
            return new MediaSnapshot
            {
                HasSession = s.HasSession, Title = s.Title, Artist = s.Artist, Album = s.Album, SourceApp = s.SourceApp,
                State = s.State, Position = s.Position, Duration = s.Duration, PositionTimestamp = s.PositionTimestamp,
                PlaybackRate = s.PlaybackRate, ShuffleActive = s.ShuffleActive, RepeatMode = s.RepeatMode,
                CanNext = s.CanNext, CanPrevious = s.CanPrevious, CanPlayPause = s.CanPlayPause, CanShuffle = s.CanShuffle,
                CanRepeat = s.CanRepeat, Thumbnail = s.Thumbnail, ThumbnailVersion = s.ThumbnailVersion,
            };
        }

        // ---- transport commands (fire and forget; the session raises change events) ----
        public void TogglePlayPause() => Fire(s => s.TryTogglePlayPauseAsync());
        public void Next() => Fire(s => s.TrySkipNextAsync());
        public void Previous() => Fire(s => s.TrySkipPreviousAsync());
        public void ToggleShuffle() => Fire(s => s.TryChangeShuffleActiveAsync(!(Snapshot.ShuffleActive)));
        public void CycleRepeat()
        {
            var next = Snapshot.RepeatMode switch
            {
                MediaPlaybackAutoRepeatMode.None => MediaPlaybackAutoRepeatMode.List,
                MediaPlaybackAutoRepeatMode.List => MediaPlaybackAutoRepeatMode.Track,
                _ => MediaPlaybackAutoRepeatMode.None,
            };
            Fire(s => s.TryChangeAutoRepeatModeAsync(next));
        }
        public void Seek(TimeSpan position)
        {
            Fire(s => s.TryChangePlaybackPositionAsync(position.Ticks));
        }

        private async void Fire(Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> command)
        {
            var session = _session;
            if (session == null) return;
            try { await command(session); RefreshPlayback(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("media command failed: " + ex.Message); }
        }

        public void Dispose()
        {
            _disposed = true;
            lock (_gate)
            {
                if (_session != null)
                {
                    try
                    {
                        _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                        _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                        _session.TimelinePropertiesChanged -= OnTimelineChanged;
                    }
                    catch { }
                    _session = null;
                }
            }
        }
    }
}
