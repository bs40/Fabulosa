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
        private const ulong MaxThumbnailBytes = 16 * 1024 * 1024;

        private GlobalSystemMediaTransportControlsSessionManager _manager;
        private GlobalSystemMediaTransportControlsSession _session;
        private readonly object _gate = new object();
        private MediaSnapshot _snapshot = new MediaSnapshot();
        private int _thumbVersion;
        private bool _refreshRunning, _refreshPending;
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
                // subscribe only when the session really changes, so repeated notifications cannot stack up handlers
                if (!ReferenceEquals(_session, session))
                {
                    Unsubscribe(_session);
                    _session = session;
                    if (_session != null)
                    {
                        _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                        _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
                        _session.TimelinePropertiesChanged += OnTimelineChanged;
                    }
                }
            }
            RequestRefresh();
        }

        private void Unsubscribe(GlobalSystemMediaTransportControlsSession session)
        {
            if (session == null) return;
            try
            {
                session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                session.TimelinePropertiesChanged -= OnTimelineChanged;
            }
            catch { }
        }

        private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => RequestRefresh();
        private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => RefreshPlayback();
        private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => RefreshPlayback();

        /// <summary>
        /// Refreshes run one at a time: a change that arrives mid-refresh queues exactly one more pass, so the
        /// last read always reflects the latest state and a slow earlier read can never overwrite a newer one.
        /// </summary>
        private void RequestRefresh()
        {
            lock (_gate)
            {
                if (_disposed) return;
                if (_refreshRunning) { _refreshPending = true; return; }
                _refreshRunning = true;
            }
            _ = RefreshLoopAsync();
        }

        private async Task RefreshLoopAsync()
        {
            while (true)
            {
                try { await RefreshPropertiesAsync(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("media refresh failed: " + ex.Message); }
                lock (_gate)
                {
                    if (!_refreshPending || _disposed) { _refreshRunning = false; return; }
                    _refreshPending = false;
                }
            }
        }

        /// <summary>
        /// Reads the metadata and artwork. Windows often reports a new track's title and its artwork as separate
        /// change events (players fetch the art after switching track), so the artwork is read on every change
        /// and compared by content, never assumed to belong to the title.
        /// </summary>
        private async Task RefreshPropertiesAsync()
        {
            var session = _session;
            if (session == null)
            {
                lock (_gate) _snapshot = new MediaSnapshot();
                return;
            }
            var props = await session.TryGetMediaPropertiesAsync();
            byte[] art = null;
            bool artFailed = false;
            if (props?.Thumbnail != null)
            {
                try { art = await ReadThumbnailAsync(props.Thumbnail); }
                catch (Exception) { artFailed = true; }
            }
            lock (_gate)
            {
                if (!ReferenceEquals(session, _session)) return;   // the session changed while reading; its own refresh follows
                var snap = Copy(_snapshot);
                string title = props?.Title ?? "", artist = props?.Artist ?? "", album = props?.AlbumTitle ?? "";
                bool sameTrack = snap.HasSession && title == snap.Title && artist == snap.Artist && album == snap.Album;
                snap.HasSession = true;
                snap.Title = title;
                snap.Artist = artist;
                snap.Album = album;
                snap.SourceApp = session.SourceAppUserModelId ?? "";
                // a failed read keeps the current track's artwork, but never carries artwork over to a new track
                if (!(artFailed && sameTrack) && !SameBytes(art, snap.Thumbnail))
                {
                    snap.Thumbnail = art;
                    snap.ThumbnailVersion = ++_thumbVersion;
                }
                _snapshot = snap;
            }
            RefreshPlayback();
        }

        private static async Task<byte[]> ReadThumbnailAsync(IRandomAccessStreamReference reference)
        {
            using var stream = await reference.OpenReadAsync();
            if (stream == null || stream.Size == 0 || stream.Size > MaxThumbnailBytes) return null;
            using var reader = new DataReader(stream);
            uint loaded = await reader.LoadAsync((uint)stream.Size);
            var bytes = new byte[loaded];
            reader.ReadBytes(bytes);
            return bytes;
        }

        private static bool SameBytes(byte[] a, byte[] b) => a == null ? b == null : b != null && a.AsSpan().SequenceEqual(b);

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
            lock (_gate)
            {
                _disposed = true;
                Unsubscribe(_session);
                _session = null;
            }
        }
    }
}
