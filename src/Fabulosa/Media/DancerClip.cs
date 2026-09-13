using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NAudio.MediaFoundation;
using SkiaSharp;

namespace Fabulosa.Media
{
    /// <summary>
    /// The alternative dancer: a short video (dancer.mp4 beside the exe), decoded once in the background
    /// with Media Foundation into compressed in-memory frames, plus the timing to play it on the beat.
    /// The clip's own tempo is measured from how the dancer's body rises and falls, so the loop can be
    /// spread over a whole number of beats with the low point of each bounce on the music's beat.
    /// </summary>
    public sealed class DancerClip
    {
        private const int StoredSize = 400;       // frames are kept within 400 x 400, about 1.25x the panel
        private const int MaxFrames = 1200;
        private const int GridRows = 64;           // resolution of the motion analysis
        private const double MinBpm = 80, MaxBpm = 150;

        private static readonly Guid EnableVideoProcessing = new Guid("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
        private static readonly Guid MajorTypeKey = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        private static readonly Guid SubtypeKey = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        private static readonly Guid FrameSizeKey = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
        private static readonly Guid FrameRateKey = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        private static readonly Guid DefaultStrideKey = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        private static readonly Guid VideoMajorType = new Guid("73646976-0000-0010-8000-00AA00389B71");
        private static readonly Guid Rgb32Subtype = new Guid("00000016-0000-0010-8000-00AA00389B71");
        private const int AllStreams = unchecked((int)0xFFFFFFFE), FirstVideoStream = unchecked((int)0xFFFFFFFC);

        private readonly string _path;
        private readonly object _gate = new object();
        private Task _loading;
        private volatile bool _ready;
        private SKImage[] _frames = Array.Empty<SKImage>();
        private SKColor[] _wall = Array.Empty<SKColor>();

        public DancerClip(string path) { _path = path; }

        public bool IsReady => _ready;
        public string Error { get; private set; }
        public double Fps { get; private set; }
        public int SourceFrames { get; private set; }
        public int LeadingFramesSkipped { get; private set; }
        public int LoopFrames => _frames.Length;
        public int Width { get; private set; }
        public int Height { get; private set; }
        /// <summary>Tempo of the dancer's own movement.</summary>
        public double NativeBpm { get; private set; }
        public int BeatsPerLoop { get; private set; }
        public double FramesPerBeat { get; private set; }
        /// <summary>Loop frame of the first low point of a bounce; beats fall on PhaseFrames + k * FramesPerBeat.</summary>
        public double PhaseFrames { get; private set; }
        public string Diagnostics { get; private set; } = "";

        public SKImage Frame(int index) => _frames[index];

        /// <summary>Background colour along the top of a frame, used to carry the wall on above the picture.</summary>
        public SKColor WallColour(int index) => _wall[index];

        /// <summary>Starts decoding in the background; later calls do nothing.</summary>
        public void Preload()
        {
            lock (_gate)
            {
                if (_loading != null || !File.Exists(_path)) return;
                _loading = Task.Run(Load);
            }
        }

        public bool WaitUntilReady(TimeSpan timeout)
        {
            Preload();
            Task loading;
            lock (_gate) loading = _loading;
            loading?.Wait(timeout);
            return _ready;
        }

        private sealed class RawFrame
        {
            public SKData Jpeg;
            public SKColor Wall;
            public float[] Grid;
            public float Luma;
        }

        private void Load()
        {
            try
            {
                var frames = Decode(out double fps);
                if (frames.Count < 8) throw new InvalidDataException("the clip is too short");

                // opening frames that fade in from white or black look nothing like the rest of the loop
                float median = frames.Select(f => f.Luma).OrderBy(v => v).ElementAt(frames.Count / 2);
                int skip = 0;
                while (skip < Math.Min(8, frames.Count / 4) && Math.Abs(frames[skip].Luma - median) > 20) skip++;
                var loop = frames.Skip(skip).ToList();

                var height = BodyHeight(loop);
                double period = BestPeriod(height, fps, out double score);

                // The clip does not loop cleanly, so its last frames cross-fade into its first ones and playback
                // wraps to just after them. The fade length is chosen so the loop holds a whole number of the
                // dancer's own beats, which keeps every bounce on the beat all the way round.
                int fade = BestFadeLength(loop.Count, period);
                int length = loop.Count - fade;
                int beatsPerLoop = Math.Max(1, (int)Math.Round(length / period));
                double framesPerBeat = (double)length / beatsPerLoop;
                Comb(height.Skip(fade).ToArray(), framesPerBeat, out double phase);
                var shown = CrossFade(loop, fade);

                static double Diff(float[] a, float[] b) { double s = 0; for (int k = 0; k < a.Length; k++) s += Math.Abs(a[k] - b[k]); return s / a.Length; }
                var steps = Enumerable.Range(1, shown.Count - 1).Select(i => Diff(shown[i].Grid, shown[i - 1].Grid)).OrderBy(v => v).ToArray();
                Diagnostics = $"opening luma {string.Join("/", frames.Take(4).Select(f => f.Luma.ToString("0")))} vs median {median:0}, " +
                              $"raw seam {Diff(loop[^1].Grid, loop[0].Grid):0.0}, {fade}-frame cross-fade, seam now {Diff(shown[^1].Grid, shown[0].Grid):0.0} " +
                              $"vs typical step {steps[steps.Length / 2]:0.0}, rhythm score {score:0.00}";

                _wall = shown.Select(f => f.Wall).ToArray();
                _frames = shown.Select(f => SKImage.FromEncodedData(f.Jpeg)).ToArray();
                Fps = fps;
                SourceFrames = frames.Count;
                LeadingFramesSkipped = skip;
                NativeBpm = 60 * fps / period;
                BeatsPerLoop = beatsPerLoop;
                FramesPerBeat = framesPerBeat;
                PhaseFrames = phase;
                _ready = true;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                System.Diagnostics.Debug.WriteLine("dancer clip failed: " + ex);
            }
        }

        /// <summary>Cross-fade length (4-12 frames) that leaves the loop closest to a whole number of beats of the given period.</summary>
        private static int BestFadeLength(int frames, double period)
        {
            int best = 0;
            double bestError = double.MaxValue;
            for (int fade = 4; fade <= Math.Min(12, frames / 4); fade++)
            {
                double beats = (frames - fade) / period;
                double error = Math.Abs(beats - Math.Round(beats));
                if (Math.Round(beats) >= 1 && error < bestError - 1e-9) { bestError = error; best = fade; }
            }
            return best;
        }

        /// <summary>
        /// Drops the first <paramref name="fade"/> frames from the loop and blends them, progressively, over the
        /// last ones, so the final frame leads straight into the new first frame.
        /// </summary>
        private static List<RawFrame> CrossFade(List<RawFrame> loop, int fade)
        {
            var shown = loop.Skip(fade).ToList();
            if (fade == 0) return shown;
            int length = shown.Count;
            using var probe = SKImage.FromEncodedData(loop[0].Jpeg);
            using var surface = SKSurface.Create(new SKImageInfo(probe.Width, probe.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            for (int k = 0; k < fade; k++)
            {
                float t = (k + 1f) / (fade + 1f);
                var tail = shown[length - fade + k];
                var head = loop[k];
                using var a = SKImage.FromEncodedData(tail.Jpeg);
                using var b = SKImage.FromEncodedData(head.Jpeg);
                surface.Canvas.Clear(SKColors.Black);
                surface.Canvas.DrawImage(a, 0, 0);
                using (var p = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(255 * t)) }) surface.Canvas.DrawImage(b, 0, 0, p);
                using var blended = surface.Snapshot();
                shown[length - fade + k] = new RawFrame
                {
                    Jpeg = blended.Encode(SKEncodedImageFormat.Jpeg, 90),
                    Wall = new SKColor((byte)(tail.Wall.Red + (head.Wall.Red - tail.Wall.Red) * t), (byte)(tail.Wall.Green + (head.Wall.Green - tail.Wall.Green) * t), (byte)(tail.Wall.Blue + (head.Wall.Blue - tail.Wall.Blue) * t)),
                    Grid = tail.Grid.Zip(head.Grid, (x, y) => x + (y - x) * t).ToArray(),
                    Luma = tail.Luma + (head.Luma - tail.Luma) * t,
                };
            }
            return shown;
        }

        private List<RawFrame> Decode(out double fps)
        {
            MediaFoundationApi.Startup();
            var frames = new List<RawFrame>();
            IMFSourceReader reader = null;
            fps = 0;
            try
            {
                var attributes = MediaFoundationApi.CreateAttributes(1);
                attributes.SetUINT32(EnableVideoProcessing, 1);   // the reader converts the decoder's YUV to RGB32
                MediaFoundationInterop.MFCreateSourceReaderFromURL(_path, attributes, out reader);
                Marshal.ReleaseComObject(attributes);

                reader.SetStreamSelection(AllStreams, false);
                reader.SetStreamSelection(FirstVideoStream, true);
                var wanted = MediaFoundationApi.CreateMediaType();
                wanted.SetGUID(MajorTypeKey, VideoMajorType);
                wanted.SetGUID(SubtypeKey, Rgb32Subtype);
                reader.SetCurrentMediaType(FirstVideoStream, IntPtr.Zero, wanted);
                Marshal.ReleaseComObject(wanted);

                int w, h, stride;
                reader.GetCurrentMediaType(FirstVideoStream, out IMFMediaType current);
                try
                {
                    current.GetUINT64(FrameSizeKey, out long size);
                    w = (int)(size >> 32);
                    h = (int)(size & 0xffffffff);
                    try
                    {
                        current.GetUINT64(FrameRateKey, out long rate);
                        long num = rate >> 32, den = rate & 0xffffffff;
                        if (num > 0 && den > 0) fps = (double)num / den;
                    }
                    catch (COMException) { }
                    try { current.GetUINT32(DefaultStrideKey, out stride); }
                    catch (COMException) { stride = w * 4; }
                }
                finally { Marshal.ReleaseComObject(current); }
                if (w <= 0 || h <= 0) throw new InvalidDataException("no video stream");

                float scale = Math.Min(1f, (float)StoredSize / Math.Max(w, h));
                int tw = Math.Max(1, (int)MathF.Round(w * scale)), th = Math.Max(1, (int)MathF.Round(h * scale));
                int gridCols = Math.Clamp((int)Math.Round((double)GridRows * w / h), 16, 128);
                Width = tw;
                Height = th;

                using var source = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
                using var target = SKSurface.Create(new SKImageInfo(tw, th, SKColorType.Bgra8888, SKAlphaType.Premul));
                using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
                double first = -1, last = 0;
                while (frames.Count < MaxFrames)
                {
                    reader.ReadSample(FirstVideoStream, 0, out _, out MF_SOURCE_READER_FLAG flags, out ulong stamp, out IMFSample sample);
                    if (sample != null)
                    {
                        try { CopySample(sample, source, stride); }
                        finally { Marshal.ReleaseComObject(sample); }
                        source.NotifyPixelsChanged();
                        if (first < 0) first = stamp / 1e7;
                        last = stamp / 1e7;

                        target.Canvas.DrawBitmap(source, SKRect.Create(0, 0, tw, th), paint);
                        using var image = target.Snapshot();
                        var grid = Grid(source, gridCols, out float luma);
                        frames.Add(new RawFrame { Jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 90), Wall = TopColour(source), Grid = grid, Luma = luma });
                    }
                    if ((flags & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM) != 0) break;
                    if ((flags & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ERROR) != 0) throw new InvalidDataException("the clip could not be decoded");
                }
                if (fps <= 0 && frames.Count > 1 && last > first) fps = (frames.Count - 1) / (last - first);
                if (fps <= 0) fps = 25;
            }
            finally
            {
                if (reader != null) Marshal.ReleaseComObject(reader);
            }
            return frames;
        }

        private static unsafe void CopySample(IMFSample sample, SKBitmap target, int stride)
        {
            sample.ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
            try
            {
                buffer.Lock(out IntPtr data, out _, out int length);
                try
                {
                    int w = target.Width, h = target.Height, rowBytes = w * 4, pitch = Math.Abs(stride);
                    if (pitch < rowBytes || length < (long)pitch * (h - 1) + rowBytes) throw new InvalidDataException("unexpected frame layout");
                    byte* src = (byte*)data, dst = (byte*)target.GetPixels();
                    for (int y = 0; y < h; y++)
                    {
                        byte* from = src + (long)(stride >= 0 ? y : h - 1 - y) * pitch;   // a negative stride means bottom-up rows
                        byte* to = dst + (long)y * target.RowBytes;
                        Buffer.MemoryCopy(from, to, rowBytes, rowBytes);
                        for (int x = 3; x < rowBytes; x += 4) to[x] = 255;               // RGB32 leaves the fourth byte undefined
                    }
                }
                finally { buffer.Unlock(); }
            }
            finally { Marshal.ReleaseComObject(buffer); }
        }

        /// <summary>Average luma over a coarse grid (sampling every other pixel), and the frame's mean luma.</summary>
        private static unsafe float[] Grid(SKBitmap bitmap, int cols, out float meanLuma)
        {
            int w = bitmap.Width, h = bitmap.Height, bx = Math.Max(1, w / cols), by = Math.Max(1, h / GridRows);
            var grid = new float[cols * GridRows];
            byte* p = (byte*)bitmap.GetPixels();
            double total = 0;
            for (int gy = 0; gy < GridRows; gy++)
                for (int gx = 0; gx < cols; gx++)
                {
                    float acc = 0;
                    int n = 0, y1 = Math.Min(h, (gy + 1) * by), x1 = Math.Min(w, (gx + 1) * bx);
                    for (int y = gy * by; y < y1; y += 2)
                        for (int x = gx * bx; x < x1; x += 2)
                        {
                            byte* q = p + (long)y * bitmap.RowBytes + x * 4;
                            acc += 0.114f * q[0] + 0.587f * q[1] + 0.299f * q[2];
                            n++;
                        }
                    float v = n > 0 ? acc / n : 0;
                    grid[gy * cols + gx] = v;
                    total += v;
                }
            meanLuma = (float)(total / grid.Length);
            return grid;
        }

        /// <summary>Median colour of the top rows: the wall behind the dancer even when hair or a hand reaches the edge.</summary>
        private static unsafe SKColor TopColour(SKBitmap bitmap)
        {
            int rows = Math.Min(4, bitmap.Height);
            var samples = new List<(float luma, SKColor colour)>();
            byte* p = (byte*)bitmap.GetPixels();
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < bitmap.Width; x += 2)
                {
                    byte* q = p + (long)y * bitmap.RowBytes + x * 4;
                    samples.Add((0.114f * q[0] + 0.587f * q[1] + 0.299f * q[2], new SKColor(q[2], q[1], q[0])));
                }
            samples.Sort((a, b) => a.luma.CompareTo(b.luma));
            return samples[samples.Count / 2].colour;
        }

        /// <summary>Per frame, the mean grid row of cells that differ from the clip's background: larger when the body drops.</summary>
        private static double[] BodyHeight(List<RawFrame> frames)
        {
            int n = frames.Count, cells = frames[0].Grid.Length, cols = cells / GridRows;
            var background = new float[cells];
            var column = new float[n];
            for (int k = 0; k < cells; k++)
            {
                for (int i = 0; i < n; i++) column[i] = frames[i].Grid[k];
                Array.Sort(column);
                background[k] = column[n / 2];
            }
            var height = new double[n];
            for (int i = 0; i < n; i++)
            {
                double weight = 0, rows = 0;
                var grid = frames[i].Grid;
                for (int k = 0; k < cells; k++)
                {
                    double d = Math.Abs(grid[k] - background[k]);
                    if (d > 18) { weight += d; rows += d * (k / cols); }
                }
                height[i] = weight > 0 ? rows / weight : GridRows / 2.0;
            }
            return height;
        }

        /// <summary>Beat period in frames (80-150 BPM) at which the body's low points line up best; 120 BPM if there is no clear rhythm.</summary>
        private static double BestPeriod(double[] height, double fps, out double score)
        {
            double shortest = 60 * fps / MaxBpm, longest = Math.Min(60 * fps / MinBpm, height.Length / 3.0);
            double best = 60 * fps / 120;
            score = double.NegativeInfinity;
            for (double p = shortest; p <= longest; p += 0.05)
            {
                double s = Comb(height, p, out _);
                if (s > score) { score = s; best = p; }
            }
            if (score < 0.3) best = 60 * fps / 120;
            return best;
        }

        /// <summary>How much lower than average the body sits on frames phase + k * period, at the best phase.</summary>
        private static double Comb(double[] series, double period, out double bestPhase)
        {
            double mean = series.Average(), best = double.NegativeInfinity;
            bestPhase = 0;
            for (double phase = 0; phase < period; phase += 0.1)
            {
                double sum = 0;
                int count = 0;
                for (double t = phase; t <= series.Length - 1; t += period)
                {
                    int i = (int)t;
                    double frac = t - i;
                    sum += i + 1 < series.Length ? series[i] * (1 - frac) + series[i + 1] * frac : series[i];
                    count++;
                }
                double score = sum / count - mean;
                if (score > best) { best = score; bestPhase = phase; }
            }
            return best;
        }
    }
}
