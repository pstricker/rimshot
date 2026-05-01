# 🥁 RIMSHOT

> *A drum trainer that hits harder than your snare.*

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square)
![Avalonia](https://img.shields.io/badge/Avalonia-12-purple?style=flat-square)
![Platform](https://img.shields.io/badge/platform-macOS-lightgrey?style=flat-square)

---

## What Is This?

Rimshot is a drum trainer for your real electronic drum kit. Note cues scroll across the screen — your job is to hit the right drum at the right time. Connect your MIDI kit, crank the BPM, and get tight.

No MIDI kit? No problem. Your keyboard works too.

---

## Features

### 🎯 Note Cues
Notes scroll from right to left and must be hit as they cross the white hit-zone line. You've got a **±150ms window** — tight but fair. Land it on the metronome beat and you'll get a sick lime green ring. Miss the beat and you get the lane color. Miss entirely and you get to think about what you've done.

### 🥁 8-Lane Drum Kit

| Lane | Piece | MIDI Notes | Color |
|------|-------|-----------|-------|
| HH | Hi-Hat | 42, 44, 46 | Gold |
| CR | Crash | 49 | Gold |
| SN | Snare | 38, 37 | Hot Pink |
| TM1 | Tom 1 | 50, 48 | Cyan |
| TM2 | Tom 2 | 47 | Cyan |
| BD | Bass Drum | 36 | Hot Pink |
| FTM | Floor Tom | 41, 43 | Cyan |
| RD | Ride | 51 | Gold |

Open hi-hat (note 46) is tracked separately — the HH indicator switches between `●` (closed) and `○` (open) in real time.

### 🎛 MIDI Hardware Support
Plug in any USB MIDI drum kit, pick it from the dropdown, hit **CONNECT**. Rimshot auto-detects all available devices. Velocity is captured live and maps directly to audio gain — hit harder, sound louder.

### ⌨️ Keyboard Mode
No kit? No excuses.

| Key | Drum |
|-----|------|
| `A` | Hi-Hat |
| `S` | Crash |
| `D` | Snare |
| `F` | Tom 1 |
| `G` | Tom 2 |
| `Space` | Bass Drum |
| `J` | Floor Tom |
| `K` | Ride |

Key repeat is suppressed — you have to actually re-press each key, just like a real drum hit.

### ⏱ BPM Control
Dial in anything from **40 to 200 BPM**. Change it live while playing — the engine re-syncs instantly without note pile-ups. Increment by 5 or type in whatever tempo you want to embarrass yourself at.

### 🎵 Metronome
Toggle the metronome on/off in the Cue View. Set your subdivision in the Tempo tab:

| Subdivision | Feel |
|------------|------|
| 1/1 | Whole notes — very zen |
| 1/2 | Half notes |
| 1/4 | Quarter notes — the classic |
| 1/8 | Eighth notes — get ready |

On-beat hits glow **lime green**. The metronome doesn't lie.

### 💥 Hit Feedback
Every hit spawns an expanding ring animation centered on the drum pad:
- **Lime green** ring = on-beat hit. Nice.
- **Lane-colored** ring = hit landed, but the metronome disagrees with you.

Rings expand to 2.4× the pad diameter and fade over 350ms.

### 📡 Monitor Tab
Every drum hit gets logged with a timestamp and velocity:
```
[14:23:01.042]  Snare               vel: 98
[14:23:01.458]  Bass Drum 1         vel: 112
[14:23:01.921]  Closed Hi-Hat       vel: 74
```
Keeps the last 200 hits. Hit **CLEAR** to start fresh.

### 🎚 Tempo Tab
A dedicated panel for BPM and metronome subdivision — synced with the main toolbar controls. Useful when you want to make adjustments without digging around the toolbar.

### 🔊 Audio Engine
Rimshot loads WAV samples from the `Sounds/` folder via OpenAL. Each lane gets a pool of 4 audio sources for polyphonic playback. If a WAV file is missing, it falls back to a **procedural synthesizer** — so the app always makes noise, even with no sample files.

Special routing:
- Hi-hat note 46 → `hh_open.wav` (open hat sound)
- Snare note 37 → `sn_rimshot.wav` (rimshot sound)

### 🎸 Startup Rimshot
Every time the app opens, it plays `rimshot.wav`. Because of course it does.

---

## Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- A USB MIDI drum kit *(optional — keyboard works fine)*

### Run It
```bash
git clone https://github.com/pstricker/rimshot.git
cd rimshot
dotnet run --project Rimshot/Rimshot.csproj
```

### Connect Your Kit
1. Plug in your MIDI drum kit
2. Select it from the **device dropdown** in the toolbar
3. Click **CONNECT**
4. Hit **▶ PLAY**
5. Start drumming

---

## Project Structure

```
Rimshot/
├── Models/
│   ├── DrumLane.cs       — lane definitions, MIDI notes, colors
│   ├── DrumHit.cs        — hit event record
│   └── FallingCue.cs     — scheduled note cue
├── Services/
│   ├── AudioService.cs   — OpenAL playback, WAV loading
│   ├── CueEngine.cs      — pattern generator and playback timing
│   ├── DrumSynth.cs      — procedural audio fallback
│   ├── MetronomeService  — beat tracking and subdivision
│   ├── MidiService.cs    — MIDI device I/O (DryWetMidi)
│   └── WavLoader.cs      — 16/24-bit PCM WAV parser
├── Views/
│   ├── CueView           — main 60fps canvas (notes, pads, rings)
│   ├── TempoView         — BPM + metronome subdivision UI
│   └── MainWindow        — toolbar, tabs, transport controls
├── Assets/
│   ├── Fonts/            — Bebas Neue, Roboto Mono
│   └── AppStyles.axaml   — global styles and color palette
└── Sounds/               — WAV samples (hh, sn, bd, cr, rd, toms...)
```

---

## Tech Stack

| | |
|-|-|
| Language | C# 12 |
| Runtime | .NET 8 |
| UI Framework | Avalonia 12 |
| MIDI | Melanchall.DryWetMidi |
| Audio | Silk.NET.OpenAL |
| Fonts | Bebas Neue, Roboto Mono |

---

*Built for drummers who want to get better, not just louder.*
