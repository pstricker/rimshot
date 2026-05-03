# 🍎 Running Rimshot on macOS

Rimshot's Mac binary release is held up waiting on Apple Developer signing + notarization. Until that ships, you can run Rimshot from source — it works just fine on macOS, both Apple Silicon (M1/M2/M3/M4) and Intel.

This guide walks you through every step. **Total setup time: ~10 minutes.** You only do this once; afterwards, launching Rimshot is a single command.

---

## What you'll install

| Tool | Why | Size |
|---|---|---|
| **Xcode Command Line Tools** | Gives you `git` (and other Unix dev tools) | ~700 MB |
| **.NET 8 SDK** | The runtime + compiler Rimshot is built on | ~250 MB |
| **Rimshot itself** | Cloned from GitHub | ~50 MB |

Nothing here phones home, runs in the background, or installs at the system level. It all lives in standard developer locations under your home folder.

---

## Step 1 — Open Terminal

Press `⌘ + Space`, type `Terminal`, hit return. A black-or-white window with a prompt opens. That's your command line.

You'll be copying commands from this guide and pasting them into Terminal. After each one, hit return and wait for it to finish before pasting the next.

---

## Step 2 — Install Xcode Command Line Tools (provides git)

Paste this into Terminal:

```bash
xcode-select --install
```

A graphical installer pops up. Click **Install**, agree to the license, wait. This downloads ~700 MB and takes a few minutes.

> If you already have Xcode or the Command Line Tools, you'll see a message saying so — that's fine, skip ahead.

**Verify:**

```bash
git --version
```

You should see something like `git version 2.43.0`. If you do, move on.

---

## Step 3 — Install the .NET 8 SDK

You have two options. Pick whichever feels easier.

### Option A — Microsoft's installer (recommended for non-developers)

1. Go to https://dotnet.microsoft.com/download/dotnet/8.0
2. Under **SDK 8.0**, download the macOS installer that matches your Mac:
   - **Apple Silicon (M1/M2/M3/M4)** → `Arm64 macOS Installer`
   - **Intel** → `x64 macOS Installer`
   - Not sure? Click the Apple menu → *About This Mac*. If "Chip" starts with "Apple", you have Apple Silicon.
3. Open the `.pkg` file and click through the installer.

### Option B — Homebrew (if you already use it)

```bash
brew install --cask dotnet-sdk
```

### Verify (either option)

Close Terminal completely (`⌘ + Q`) and reopen it, then run:

```bash
dotnet --version
```

You should see `8.0.x` (e.g. `8.0.404`). If you get *"command not found"*, see [Troubleshooting](#troubleshooting) below.

---

## Step 4 — Clone Rimshot

Pick a folder to put the source in. Most people use a `Projects` or `Code` folder in their home directory:

```bash
mkdir -p ~/Projects
cd ~/Projects
git clone https://github.com/pstricker/rimshot.git
cd rimshot
```

You're now inside the Rimshot source folder.

---

## Step 5 — Run it

```bash
dotnet run --project Rimshot/Rimshot.csproj
```

The first time, .NET downloads packages (~30 sec) and compiles the app (~30 sec). After that, the Rimshot window opens.

That's it — you're playing.

---

## Connecting your drum kit

macOS has built-in MIDI support (CoreMIDI), so USB MIDI drum kits work plug-and-play.

1. Plug in your kit's USB cable.
2. Click **CONNECT DRUMS** in the Rimshot header.
3. Pick your kit from the dropdown and hit CONNECT.

If your kit doesn't appear in the dropdown:
- Hit **REFRESH** in the dialog.
- Open the macOS app **Audio MIDI Setup** (`⌘ + Space` → "Audio MIDI Setup") → *Window* → *Show MIDI Studio*. Make sure your kit shows up there. If it doesn't, macOS isn't seeing it — try a different USB cable or port.

No drum kit? Use your keyboard:

| Key | Drum |
|---|---|
| `A` | Hi-Hat |
| `S` | Crash |
| `D` | Snare |
| `F` | Tom 1 |
| `G` | Tom 2 |
| `Space` | Bass Drum |
| `J` | Floor Tom |
| `K` | Ride |

---

## Optional: Backing Track support

To play along with the melodic parts of `.mid` files, Rimshot needs a General MIDI SoundFont (`.sf2`). Easiest path:

1. Load any melodic `.mid` (try one from https://bitmidi.com).
2. Tick the **BACKING TRACK** checkbox.
3. Rimshot pops up a prompt — pick a `.sf2` file from your Mac and Rimshot copies it into the right place automatically.

Don't have one? **FluidR3 GM** (free, MIT-licensed) is a good default:
- https://musical-artifacts.com/artifacts/738
- https://github.com/FluidSynth/fluidsynth/wiki/SoundFont

---

## Updating Rimshot

When new versions ship, update with:

```bash
cd ~/Projects/rimshot
git pull
dotnet run --project Rimshot/Rimshot.csproj
```

---

## Launching Rimshot the next day

Open Terminal and run:

```bash
cd ~/Projects/rimshot
dotnet run --project Rimshot/Rimshot.csproj
```

If you'd rather have a one-click launcher, you can save the above into a `.command` file on your Desktop:

```bash
cat > ~/Desktop/Rimshot.command <<'EOF'
#!/bin/bash
cd ~/Projects/rimshot
dotnet run --project Rimshot/Rimshot.csproj
EOF
chmod +x ~/Desktop/Rimshot.command
```

Now double-clicking `Rimshot.command` on your Desktop launches the app. (macOS may complain the first time — right-click → Open → confirm in the Gatekeeper dialog.)

---

## Troubleshooting

**`dotnet --version` says "command not found"**
You probably need to close + reopen Terminal so it picks up the new install. If that doesn't fix it, the dotnet binary is at `/usr/local/share/dotnet/dotnet`. Add it to your shell:
```bash
echo 'export PATH="/usr/local/share/dotnet:$PATH"' >> ~/.zshrc
source ~/.zshrc
```

**`dotnet run` fails with NuGet / restore errors**
Most often a transient network issue. Try once more:
```bash
dotnet restore Rimshot.sln
dotnet run --project Rimshot/Rimshot.csproj
```

**Drum kit isn't in the dropdown**
- REFRESH inside the Connect Drums dialog.
- Confirm macOS sees the device in *Audio MIDI Setup → MIDI Studio*.
- Try a different USB port / cable.

**The window opens but sound is silent**
- Check macOS *System Settings → Sound* and confirm Rimshot's audio device is active.
- Rimshot ships with WAV samples in `Rimshot/Sounds/`. If those are missing, the app falls back to a synth — quieter but should still be audible.

**Open hi-hat note 46 doesn't sound different**
That's intentional — note 46 plays the open hi-hat sample only when `Rimshot/Sounds/hh_open.wav` is present. The repo includes it; you shouldn't need to do anything.

**Crash on launch / weird error**
File an issue at https://github.com/pstricker/rimshot/issues with:
- Your macOS version (`sw_vers -productVersion`)
- Your Mac's chip (Apple/Intel)
- The output of `dotnet --version`
- The full error from Terminal

---

## When the proper Mac binary lands

Once Apple Developer signing is in place, a `Rimshot-macOS.dmg` will appear on the [Releases page](https://github.com/pstricker/rimshot/releases/latest) alongside the Windows and Linux builds. At that point this guide becomes optional — but everything you set up here (the .NET SDK, the cloned source) is reusable for any other .NET project, so it's not wasted effort.

Drum on.
