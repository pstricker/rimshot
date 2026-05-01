# SoundFonts

Drop a General MIDI `.sf2` file in this folder to enable the **BACKING TRACK** feature. The first `.sf2` Rimshot finds at startup is loaded into the synth.

## Recommended: FluidR3 GM

- **License:** MIT
- **Size:** ~141 MB (or ~25 MB for a trimmed variant)
- **Quality:** widely-used General MIDI soundfont, good across all instruments

Common mirrors:

- https://github.com/FluidSynth/fluidsynth/wiki/SoundFont
- https://musical-artifacts.com/artifacts/738 (FluidR3_GM.sf2)
- https://archive.org/details/fluidr3-gm-gs

Save the file as `FluidR3_GM.sf2` (or anything ending in `.sf2`) inside this folder.

## Anything else?

Any GM-compatible `.sf2` will work — the synth doesn't care about the filename. If multiple `.sf2` files are present, the first one found is used.

If no soundfont is present, the BACKING TRACK checkbox stays hidden and the rest of the app works normally.
