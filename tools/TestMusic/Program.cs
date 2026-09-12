using NAudio.CoreAudioApi;
using NAudio.Wave;

// Plays synthesised test music (or a WAV/MP3 file, or a calibration tone) through the default
// output device so Fabulosa's loopback capture has something to react to.
//
//   TestMusic [--seconds N] [--wav path] [--tone hz dbfs]

int seconds = 30;
string file = null;
double toneHz = 0, toneDb = -18;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--seconds": seconds = int.Parse(args[++i]); break;
        case "--wav": file = args[++i]; break;
        case "--tone": toneHz = double.Parse(args[++i]); toneDb = double.Parse(args[++i]); break;
    }
}

using var enumerator = new MMDeviceEnumerator();
using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
Console.WriteLine($"Playing on: {device.FriendlyName} for {seconds}s");

ISampleProvider source;
if (file != null)
{
    var reader = new AudioFileReader(file);
    source = reader;
}
else if (toneHz > 0)
{
    source = new ToneSource(48000, toneHz, Math.Pow(10, toneDb / 20));
}
else
{
    source = new SynthMusic(48000);
}

using var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 60);
output.Init(source);
output.Play();
Thread.Sleep(seconds * 1000);
output.Stop();
Console.WriteLine("done");

sealed class ToneSource : ISampleProvider
{
    private readonly double _hz, _amp; private double _phase;
    public ToneSource(int rate, double hz, double amp) { WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, 2); _hz = hz; _amp = amp; }
    public WaveFormat WaveFormat { get; }
    public int Read(float[] buffer, int offset, int count)
    {
        for (int i = 0; i < count; i += 2)
        {
            float v = (float)(Math.Sin(_phase) * _amp);
            _phase += 2 * Math.PI * _hz / WaveFormat.SampleRate;
            buffer[offset + i] = v; buffer[offset + i + 1] = v;
        }
        return count;
    }
}

/// <summary>A little synth-wave loop: kick, snare, hats, bass, pads and an arpeggio at 118 BPM.</summary>
sealed class SynthMusic : ISampleProvider
{
    private readonly int _rate;
    private long _n;
    private readonly Random _rnd = new Random(3);
    private double _lpL, _lpR;
    private readonly float[] _delay;
    private int _delayPos;

    public SynthMusic(int rate)
    {
        _rate = rate;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, 2);
        _delay = new float[(int)(rate * 0.375)];
    }

    public WaveFormat WaveFormat { get; }

    private static readonly double[] BassNotes = { 55.0, 55.0, 43.65, 49.0 };            // A1 A1 F1 G1
    private static readonly double[][] Chords =
    {
        new[] { 220.0, 261.63, 329.63 }, new[] { 220.0, 261.63, 329.63 },               // Am
        new[] { 174.61, 220.0, 261.63 }, new[] { 196.0, 246.94, 293.66 },                // F, G
    };
    private static readonly double[] Arp = { 440, 523.25, 659.25, 880, 659.25, 523.25, 440, 329.63 };

    public int Read(float[] buffer, int offset, int count)
    {
        const double bpm = 118;
        double beatLen = 60.0 / bpm;
        for (int i = 0; i < count; i += 2)
        {
            double t = _n / (double)_rate;
            double beat = t / beatLen;
            int beatIndex = (int)beat;
            double beatFrac = beat - beatIndex;
            int bar = (beatIndex / 4) % 4;
            double barFrac = (beat % 4) / 4;

            // kick: pitch-swept sine with fast decay on every beat
            double kickT = beatFrac * beatLen;
            double kick = Math.Sin(2 * Math.PI * (48 + 110 * Math.Exp(-kickT * 22)) * kickT) * Math.Exp(-kickT * 7) * 0.9;

            // snare on 2 and 4: noise burst + tone
            double snare = 0;
            if (beatIndex % 2 == 1)
            {
                double st = beatFrac * beatLen;
                snare = ((_rnd.NextDouble() * 2 - 1) * 0.5 + Math.Sin(2 * Math.PI * 190 * st) * 0.4) * Math.Exp(-st * 16) * 0.55;
            }
            // hats on eighths
            double eighthFrac = (beat * 2) % 1;
            double hat = (_rnd.NextDouble() * 2 - 1) * Math.Exp(-eighthFrac * beatLen * 2 * 40) * 0.18;

            // bass: saw-ish with slow LP, sidechain pump on the beat
            double bassHz = BassNotes[bar];
            double bassPhase = (t * bassHz) % 1;
            double bass = (2 * bassPhase - 1) * 0.5 + Math.Sin(2 * Math.PI * bassHz * t) * 0.5;
            bass *= 0.5 * (0.35 + 0.65 * Math.Min(1, beatFrac * 4));
            bool bassGate = ((int)(beat * 2)) % 8 != 7;
            if (!bassGate) bass *= 0.2;

            // pads: detuned saws, filtered, wide stereo
            double[] chord = Chords[bar];
            double padL = 0, padR = 0;
            foreach (double hz in chord)
            {
                padL += (2 * ((t * hz * 1.003) % 1) - 1) + (2 * ((t * hz * 0.997) % 1) - 1);
                padR += (2 * ((t * hz * 1.006) % 1) - 1) + (2 * ((t * hz * 0.994) % 1) - 1);
            }
            padL *= 0.06; padR *= 0.06;
            double lpCoef = 0.10 + 0.08 * Math.Sin(2 * Math.PI * t / 8);
            _lpL += (padL - _lpL) * lpCoef;
            _lpR += (padR - _lpR) * lpCoef;

            // arpeggio: sixteenths, plucked
            int step = (int)(beat * 4) % Arp.Length;
            double stepFrac = (beat * 4) % 1;
            double arp = Math.Sin(2 * Math.PI * Arp[step] * t) * Math.Exp(-stepFrac * 6) * 0.22;
            arp *= barFrac > 0.5 || bar >= 2 ? 1 : 0.4;

            double left = kick * 0.9 + snare + hat * 0.8 + bass + _lpL * 1.3 + arp * 0.7;
            double right = kick * 0.9 + snare + hat * 1.2 + bass + _lpR * 1.3 + arp * 1.0;

            // ping-pong delay on the arp / hats
            float delayed = _delay[_delayPos];
            _delay[_delayPos] = (float)(arp * 0.5 + hat * 0.3) + delayed * 0.45f;
            _delayPos = (_delayPos + 1) % _delay.Length;
            left += delayed * 0.5; right += delayed * 0.35;

            buffer[offset + i] = (float)Math.Tanh(left * 0.7) * 0.8f;
            buffer[offset + i + 1] = (float)Math.Tanh(right * 0.7) * 0.8f;
            _n++;
        }
        return count;
    }
}
