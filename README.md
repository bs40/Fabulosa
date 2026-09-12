# Fabulosa

Music-reactive visualiser for Windows. It listens to **whatever Windows is playing** (WASAPI loopback on the
default output device — Spotify, a browser, a game, anything) and drives a rack of instruments modelled on the
reference render in `sample.png`:

| Panel | What it shows |
| --- | --- |
| Spectrum analyser | 64 log-spaced bands, 20 Hz – 20 kHz, 0 to −60 dB, segmented rainbow bars with peak hold. Click **L+R** to switch to L or R. |
| Level meters | Two analogue VU meters with true 300 ms ballistics (0 VU = −18 dBFS) plus 30-segment peak / RMS ladders and numeric readouts. |
| Oscilloscope | Rising-edge-triggered trace, 5 ms per division. |
| Phase scope | X = left, Y = right goniometer with phosphor persistence and a fitted stereo-field ellipse. |
| 3D spectrum | 40 time slices of the spectrum as an oblique wireframe, coloured by level. |
| Now playing | Title, artist, album, artwork, progress and transport controls from the Windows media session of the app that is playing. Shuffle / repeat in the footer control it too. |
| Graphic equaliser, effects, knobs | Shape the *visualisation* (the app is not in the audio path): the EQ curve tilts the displayed spectrum, Loudness sets display gain, Balance weights the meters, Stereo Width scales the phase scope, Master is the analysis input gain. Effects are latching switches saved with presets. |
| Presets | Ten factory presets; save, delete, import / export as JSON. |

Drag the caps and knobs, scroll to nudge them, double-click to reset. Drag the title bar to move the window,
double-click it (or F11) to maximise — the whole UI scales uniformly, and **Tools → Reset Window Size** returns it
to the native 1672 × 941 pixels of the design. Space toggles play / pause of the current media session.

## Build

Requirements: Windows 10 1809+ / Windows 11, [.NET 8 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet build Fabulosa.sln -c Release
```

Single self-contained executable (no runtime install needed on the target machine):

```powershell
powershell -ExecutionPolicy Bypass -File build\publish.ps1
```

Output: `publish\Fabulosa.exe`.

## Test signal

`tools\TestMusic` plays synthesised music (or a file, or a calibration tone) through the default output device:

```powershell
dotnet run --project tools\TestMusic -- --seconds 30
dotnet run --project tools\TestMusic -- --wav "C:\path\to\song.mp3"
dotnet run --project tools\TestMusic -- --tone 1000 -18      # a -18 dBFS sine should read 0 VU
```

## Command line

`Fabulosa.exe --screenshot out.png [--delay ms] [--keep]` renders a frame at native resolution to a PNG
(used for automated verification against the reference render) and exits unless `--keep` is given.

## Settings

`%AppData%\Fabulosa\settings.json` (window placement, visualisation options, current controls) and
`%AppData%\Fabulosa\presets.json`. Errors are logged to `%AppData%\Fabulosa\error.log`.

## Project layout

```
src/Fabulosa/Audio     WASAPI loopback capture, FFT, band analysis, meter ballistics
src/Fabulosa/Media     Windows media-session (GSMTC) integration
src/Fabulosa/Model     App state, presets, settings persistence
src/Fabulosa/UI        Skia renderer, theme, layout, menus and one view per panel
tools/TestMusic        Test-signal generator
```

Fonts: Michroma (SIL OFL) and Yellowtail (Apache 2.0), bundled under `src/Fabulosa/Assets/Fonts`.
