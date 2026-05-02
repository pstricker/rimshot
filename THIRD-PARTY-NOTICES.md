# Third-Party Notices

Rimshot bundles or depends on the following third-party assets and libraries.
This file collects their licenses and credits as required for redistribution.

## Fonts

### Bebas Neue

- File: `Rimshot.Core/Assets/Fonts/BebasNeue-Regular.ttf`
- Author: Ryoichi Tsunekawa (Dharma Type)
- License: SIL Open Font License 1.1 — https://scripts.sil.org/OFL

### Roboto Mono

- Files:
  - `Rimshot.Core/Assets/Fonts/RobotoMono-Regular.ttf`
  - `Rimshot.Core/Assets/Fonts/RobotoMono-SemiBold.ttf`
- Author: Christian Robertson (Google)
- License: Apache License 2.0 — http://www.apache.org/licenses/LICENSE-2.0

## Sound samples

All `.wav` files in `Rimshot/Sounds/` are released under
[Creative Commons CC0 1.0 Universal (Public Domain Dedication)](https://creativecommons.org/publicdomain/zero/1.0/).
No attribution required.

## NuGet packages

Rimshot uses the following NuGet packages, all under permissive licenses
(MIT or Apache 2.0). Full license texts ship with each package via NuGet.

- [Avalonia](https://github.com/AvaloniaUI/Avalonia) — MIT
- [Melanchall.DryWetMidi](https://github.com/melanchall/drywetmidi) — MIT
- [MeltySynth](https://github.com/sinshu/meltysynth) — MIT
- [Silk.NET](https://github.com/dotnet/Silk.NET) — MIT (includes OpenAL bindings + OpenAL Soft native binaries)
- [AvaloniaUI.DiagnosticsSupport](https://github.com/AvaloniaUI/Avalonia) — MIT (Debug builds only)

## SoundFont (optional, user-supplied)

Rimshot's Backing Track feature can load a General MIDI SoundFont (`.sf2`)
from `Rimshot/Sounds/soundfonts/`. No SoundFont is bundled. The recommended
choice — [FluidR3 GM](https://member.keymusician.com/Member/FluidR3_GM/) — is
distributed under the MIT License by its author, Frank Wen.
