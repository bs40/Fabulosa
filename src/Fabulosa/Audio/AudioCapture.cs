using System;
using System.Diagnostics;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace Fabulosa.Audio
{
    /// <summary>
    /// Captures whatever is playing on the default output device via WASAPI loopback and
    /// pushes de-interleaved stereo frames into a ring buffer. A silent output stream is
    /// kept open so the loopback endpoint keeps delivering packets (and meters decay
    /// naturally) even when nothing is playing. Follows default-device changes.
    /// </summary>
    public sealed class AudioCapture : IDisposable
    {
        private readonly MMDeviceEnumerator _enumerator = new MMDeviceEnumerator();
        private readonly NotificationClient _notifications;
        private readonly object _gate = new object();
        private WasapiCapture _capture;
        private WasapiOut _silence;
        private MMDevice _device;
        private float[] _left = new float[8192];
        private float[] _right = new float[8192];
        private Timer _restartTimer;
        private readonly Stopwatch _packetClock = Stopwatch.StartNew();
        private double _lastPacket;
        private bool _disposed;

        public AudioCapture(StereoRingBuffer buffer)
        {
            Buffer = buffer;
            _notifications = new NotificationClient(this);
            try { _enumerator.RegisterEndpointNotificationCallback(_notifications); } catch { /* optional */ }
        }

        public StereoRingBuffer Buffer { get; }
        public int SampleRate { get; private set; } = 48000;
        public int Channels { get; private set; } = 2;
        public int BitDepth { get; private set; } = 32;
        public bool IsFloat { get; private set; } = true;
        public string DeviceName { get; private set; } = "No device";
        public string Status { get; private set; } = "Starting";
        public bool IsRunning { get; private set; }

        /// <summary>Human readable description of the capture format, e.g. "32-bit float 48 kHz".</summary>
        public string FormatDescription
        {
            get
            {
                if (!IsRunning) return Status;
                string rate = SampleRate % 1000 == 0 ? (SampleRate / 1000) + " kHz" : (SampleRate / 1000.0).ToString("0.#") + " kHz";
                return $"{BitDepth}-bit {(IsFloat ? "float " : "")}{rate}";
            }
        }

        public void Start()
        {
            lock (_gate)
            {
                StopInternal();
                try
                {
                    _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    DeviceName = _device.FriendlyName;

                    _capture = StartCapture(_device);
                    var fmt = _capture.WaveFormat;
                    SampleRate = fmt.SampleRate;
                    Channels = fmt.Channels;
                    BitDepth = fmt.BitsPerSample;
                    IsFloat = IsFloatFormat(fmt);

                    // Keep the endpoint active so loopback keeps delivering (silent) packets.
                    _silence = new WasapiOut(_device, AudioClientShareMode.Shared, true, 200);
                    _silence.Init(new SilenceProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2)));
                    _silence.Play();
                    IsRunning = true;
                    Status = "Listening";
                }
                catch (Exception ex)
                {
                    IsRunning = false;
                    Status = "No audio device";
                    DeviceName = "No device";
                    System.Diagnostics.Debug.WriteLine("Audio capture failed: " + ex);
                    ScheduleRestart(2000);
                }
            }
        }

        /// <summary>How audio is being delivered, for the diagnostics in the stats file.</summary>
        public string CaptureMode { get; private set; } = "";

        /// <summary>Smoothed gap between audio packets: the floor under how soon anything on screen can react.</summary>
        public float PacketIntervalMs { get; private set; }

        /// <summary>Loopback capture with a short buffer that can be driven by the device's own event.</summary>
        private sealed class LowLatencyLoopback : WasapiCapture
        {
            public LowLatencyLoopback(MMDevice device, bool eventSync, int bufferMs) : base(device, eventSync, bufferMs) { }
            protected override AudioClientStreamFlags GetAudioClientStreamFlags() => AudioClientStreamFlags.Loopback | base.GetAudioClientStreamFlags();
        }

        /// <summary>
        /// Starts the quickest delivery the device accepts. Event-driven capture hands over audio about every
        /// 10 ms; NAudio's default polls at roughly 60 ms, which is enough to leave every panel behind the music.
        /// </summary>
        private WasapiCapture StartCapture(MMDevice device)
        {
            var attempts = new (string Mode, Func<WasapiCapture> Make)[]
            {
                ("event-driven, 20 ms buffer", () => new LowLatencyLoopback(device, true, 20)),
                ("polled, 20 ms buffer", () => new LowLatencyLoopback(device, false, 20)),
                ("NAudio default", () => new WasapiLoopbackCapture(device)),
            };
            Exception failure = null;
            foreach (var attempt in attempts)
            {
                WasapiCapture capture = null;
                try
                {
                    capture = attempt.Make();
                    capture.DataAvailable += OnDataAvailable;
                    capture.RecordingStopped += OnRecordingStopped;
                    capture.StartRecording();
                    CaptureMode = attempt.Mode;
                    _lastPacket = 0;
                    PacketIntervalMs = 0;
                    return capture;
                }
                catch (Exception ex)
                {
                    failure = ex;
                    System.Diagnostics.Debug.WriteLine($"capture mode \"{attempt.Mode}\" failed: {ex.Message}");
                    try { capture?.Dispose(); } catch { }
                }
            }
            throw failure ?? new InvalidOperationException("No loopback capture could be started");
        }

        private static readonly Guid IeeeFloatSubtype = new Guid("00000003-0000-0010-8000-00aa00389b71");

        private static bool IsFloatFormat(WaveFormat fmt)
        {
            if (fmt.Encoding == WaveFormatEncoding.IeeeFloat) return true;
            if (fmt is WaveFormatExtensible ext) return ext.SubFormat == IeeeFloatSubtype;
            return false;
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            var capture = _capture;
            if (capture == null) return;
            var fmt = capture.WaveFormat;
            int channels = fmt.Channels;
            int bytesPerSample = fmt.BitsPerSample / 8;
            int frameBytes = bytesPerSample * channels;
            if (frameBytes == 0) return;
            int frames = e.BytesRecorded / frameBytes;
            if (frames == 0) return;
            double now = _packetClock.Elapsed.TotalMilliseconds;
            if (_lastPacket > 0)
                PacketIntervalMs = PacketIntervalMs <= 0 ? (float)(now - _lastPacket) : PacketIntervalMs + (float)(now - _lastPacket - PacketIntervalMs) * 0.05f;
            _lastPacket = now;
            if (_left.Length < frames) { _left = new float[frames * 2]; _right = new float[frames * 2]; }

            byte[] buf = e.Buffer;
            bool isFloat = IsFloatFormat(fmt);
            for (int f = 0; f < frames; f++)
            {
                int o = f * frameBytes;
                float l = ReadSample(buf, o, bytesPerSample, isFloat);
                float r = channels > 1 ? ReadSample(buf, o + bytesPerSample, bytesPerSample, isFloat) : l;
                _left[f] = l;
                _right[f] = r;
            }
            Buffer.Write(_left, _right, frames);
        }

        private static float ReadSample(byte[] buf, int offset, int bytesPerSample, bool isFloat)
        {
            switch (bytesPerSample)
            {
                case 4:
                    if (isFloat) return BitConverter.ToSingle(buf, offset);
                    return BitConverter.ToInt32(buf, offset) / 2147483648f;
                case 2:
                    return BitConverter.ToInt16(buf, offset) / 32768f;
                case 3:
                    int v = (buf[offset] << 8) | (buf[offset + 1] << 16) | (buf[offset + 2] << 24);
                    return v / 2147483648f;
                case 1:
                    return (buf[offset] - 128) / 128f;
                default:
                    return 0f;
            }
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            if (_disposed) return;
            if (e.Exception != null)
            {
                lock (_gate)
                {
                    IsRunning = false;
                    Status = "Device lost";
                }
                ScheduleRestart(1500);
            }
        }

        private void ScheduleRestart(int delayMs)
        {
            if (_disposed) return;
            _restartTimer?.Dispose();
            _restartTimer = new Timer(_ => { if (!_disposed) Start(); }, null, delayMs, Timeout.Infinite);
        }

        private void StopInternal()
        {
            try { _capture?.StopRecording(); } catch { }
            try { _capture?.Dispose(); } catch { }
            _capture = null;
            try { _silence?.Stop(); } catch { }
            try { _silence?.Dispose(); } catch { }
            _silence = null;
            try { _device?.Dispose(); } catch { }
            _device = null;
            IsRunning = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _restartTimer?.Dispose();
            try { _enumerator.UnregisterEndpointNotificationCallback(_notifications); } catch { }
            lock (_gate) StopInternal();
            _enumerator.Dispose();
        }

        private sealed class NotificationClient : IMMNotificationClient
        {
            private readonly AudioCapture _owner;
            public NotificationClient(AudioCapture owner) { _owner = owner; }

            public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
            {
                if (flow == DataFlow.Render && role == Role.Multimedia) _owner.ScheduleRestart(500);
            }
            public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
            public void OnDeviceAdded(string pwstrDeviceId) { }
            public void OnDeviceRemoved(string deviceId) { }
            public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
        }
    }
}
