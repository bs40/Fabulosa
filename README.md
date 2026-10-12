# Fabulosa

Music-reactive visualiser for Windows. It listens to **whatever Windows is playing** (WASAPI loopback on the
default output device — Spotify, a browser, a game, anything) and drives a rack of instruments:

| Panel | What it shows |
| --- | --- |
| Spectrum analyser | 64 log-spaced bands, 20 Hz – 20 kHz, 0 to −60 dB, segmented rainbow bars with peak hold. Click **L+R** to switch to L or R. |
| Level meters | Two analogue VU meters with 150 ms ballistics (twice classic VU speed; 0 VU = −14 dBFS) plus 30-segment peak / RMS ladders and numeric readouts. |
| Dancer | A neon stick figure (48-pose sprite sheet, `stickanim.png`) that dances through its poses in order, four frames per beat. A beat tracker follows the kick/snare onsets, estimates the tempo (shown as BPM) and keeps a phase-locked clock running through breakdowns; frames never skip or step back on timing corrections. The rate (1, 2, 4 or 8 frames per beat) is in the Visualisation menu. |
| Oscilloscope | Rising-edge-triggered trace, 5 ms per division, auto-ranged. |
| 3D spectrum | 32 time slices of the spectrum as an oblique wireframe, coloured by level. |
| Radial spectrum | A tilted ring of spokes, one per band: the right channel runs down one side and the left channel back up the other. The newest spikes are worked out every frame and flow outward as a trail. It pulses on every drum hit and turns clockwise once a minute. Filled or lines, and the trail length, are in the Visualisation menu. |
| Phase scope | X = left, Y = right goniometer with phosphor persistence and a fitted stereo-field ellipse. |
| Now playing | Title, artist, album, artwork, progress and transport controls from the Windows media session of the app that is playing. Shuffle / repeat in the footer control it too. |
| YouTube (optional) | Replaces Now Playing with YouTube's own embedded player, so a video or playlist plays inline and every visualiser reacts to its sound. Turn it on in the **View** menu, paste a link with **View → YouTube: Set Video or Playlist…**, and hide the Dancer to give it the whole left column. |

The **View** menu picks which visualisers are shown. The rest of the rack closes up around the ones you keep,
and the choice is remembered between runs.

The YouTube panel needs the Microsoft Edge WebView2 runtime (part of Windows 11). It plays public videos and
playlists through YouTube's official IFrame player; signing in to a Google account inside an embedded view is
blocked by Google, and the video is a separate window layer, so it does not appear in `--screenshot` captures.

**Tools → Performance Mode** draws at 1672 × 941 and lets the graphics card scale the result to the window, which
keeps the frame rate steady on large screens for a slightly softer picture.

Drag the title bar to move the window, double-click it (or F11) to maximise — the whole UI scales uniformly,
and **Tools → Reset Window Size** returns it to the native 1672 × 941 pixels of the design. Space toggles
play / pause of the current media session.

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

## Artwork

`wordmark.png` (title-bar logo, black background keyed out at draw time) and `stickanim.png` (12 × 4 sprite
sheet of 48 frames, transparent) live in the repository root and are embedded at build time, so they can be
swapped without touching the code. Each frame is flattened onto black and cropped inside its card outline, leaving out the frame number.

## Test signal

`tools\TestMusic` plays synthesised music (or a file, or a calibration tone) through the default output device:

```powershell
dotnet run --project tools\TestMusic -- --seconds 30
dotnet run --project tools\TestMusic -- --wav "C:\path\to\song.mp3"
dotnet run --project tools\TestMusic -- --tone 1000 -14      # a -14 dBFS sine should read 0 VU
```

## Command line

- `Fabulosa.exe --screenshot out.png [--delay ms] [--keep]` renders a frame at native resolution to a PNG and
  writes `out.png.stats.txt` (frame time, levels, detected BPM, per-panel draw cost); exits unless `--keep`.
- `Fabulosa.exe --windowshot out.png [--delay ms]` captures the live window's visual tree.
- `Fabulosa.exe --selftest report.txt` exercises the hit-testing, menus, the View picker and every combination of
  shown visualisers, the beat tracker (synthetic 128 and 90 BPM tracks plus silence), the dancer frame order, the
  VU timing and the radial view's rotation, and writes a PASS/FAIL report plus pictures of a few layouts
  (`report.txt.layout-*.png`) and a contact sheet of the cropped dancer frames (`report.txt.dancer.png`).

## Settings

`%AppData%\Fabulosa\settings.json` (window placement, visualisation options, the YouTube link). The YouTube panel
keeps its browser profile in `%AppData%\Fabulosa\WebView2` and its player page in `%AppData%\Fabulosa\player`.
Errors are logged to
`%AppData%\Fabulosa\error.log`.

## Project layout

```
src/Fabulosa/Audio     WASAPI loopback capture, FFT, band analysis, meter ballistics, beat tracker
src/Fabulosa/Media     Windows media-session (GSMTC) integration
src/Fabulosa/Model     App state and settings persistence
src/Fabulosa/UI        Skia renderer, theme, layout, menus and one view per panel
tools/TestMusic        Test-signal generator
```

## Licence

MIT — see [LICENSE](LICENSE).
