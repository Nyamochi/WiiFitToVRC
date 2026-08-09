# WiiFitToVRC

[日本語](README.md) | **English** | [한국어](README.ko.md) | [简体中文](README.zh-Hans.md) | [繁體中文](README.zh-Hant.md)

Turn a Wii Balance Board into a walking controller for VRChat (or any other Windows
application). Stand on the board, shift your weight to walk/turn/jump/crouch, and the app turns
that into keyboard/mouse input, a virtual Xbox 360 controller, or VRChat's own OSC input.

## Quick and easy setup (no technical knowledge required)

1. Click `WiiFitToVRC.exe` at the top of this repository to download it (no installation step).
2. Double-click the downloaded file to run it.
3. Press the **SYNC** button inside the battery compartment of the balance board, then click
   **接続 (Connect)** in the app.
4. Follow the on-screen prompts (**キャリブレーション (Calibrate)** → step off the board and wait
   → step back on and wait) and you're ready to go. Launch VRChat and shift your weight on the
   board to walk.

Note: to confirm the board is connected properly, open Notepad and step on the board -- if it
types w/s/a/d, everything's working. If the game still doesn't respond (for example, in VR mode),
first enable OSC in VRChat itself, then try switching the output mode in Settings to VRChat OSC.

See "Quick start" below for more detail, and the [docs](docs/) folder for a deeper explanation of
each feature if something doesn't work as expected.

## Features

- **Pairs with the balance board over Bluetooth with no PIN prompt** — see
  [docs/BALANCE_BOARD.md](docs/BALANCE_BOARD.md) for how and why.
- **Two-stage calibration**: a one-time sensor zero-point calibration (step off the board), and a
  continuously self-refreshing "resting weight" reference that adapts automatically when a
  different person steps on.
- **Gesture detection** for forward, backward, dash, turn left/right, jump, and crouch — see
  [docs/GESTURE_DETECTION.md](docs/GESTURE_DETECTION.md) for exactly how each is judged and which
  settings tune them.
- **Four output modes**:
  - Keyboard (turning via Q/E)
  - Keyboard + mouse (turning via mouse-look — the default)
  - Virtual Xbox 360 controller, for games that reject synthetic keyboard/mouse input (VRChat
    included) — see [docs/VRCHAT_INPUT.md](docs/VRCHAT_INPUT.md).
  - VRChat OSC input, for VR setups where the game's input focus is locked to the VR device and
    rejects synthetic input entirely, even the virtual controller — see
    [docs/VRCHAT_INPUT.md](docs/VRCHAT_INPUT.md).
- Fully configurable keybinds/controller bindings, turn sensitivity, weight thresholds, and
  timing, all from the in-app settings window.
- Localized UI: auto-detects the Windows display language, with English, Japanese, Simplified &
  Traditional Chinese, Korean, French, German, and Italian built in.

## Works with other games too

The app's output is plain keyboard WASD (or mouse) input, so as long as a game accepts WASD
movement, this app works with it too, whether or not that game officially supports it. Games it's
been tried with:

- Death Stranding
- Resident Evil
- Monster Hunter
- Armored Core IV

## Requirements

- Windows 10/11
- A Wii Balance Board (Bluetooth) — discontinued, but commonly found cheaply secondhand
- A Bluetooth adapter that supports HID devices

### Using the virtual controller (optional)

[ViGEmBus](https://github.com/nefarius/ViGEmBus/releases) (a real kernel driver — this app cannot
install it for you; download and install it yourself)

## Quick start

1. Download `WiiFitToVRC.exe` from the root of this repository (a self-contained build — no
   .NET runtime install needed) and run it.
2. Press the **SYNC** button inside the battery compartment of the balance board, then click
   **接続 (Connect)** in the app.
3. Once connected, click **キャリブレーション (Calibrate)** and step off the board for the
   10-second sensor calibration.
4. Step back on and stand normally for a while — the app needs a stretch of genuinely still
   standing to learn your resting weight before gesture detection turns on (status bar shows
   "体重キャリブレーション中" / "Weight calibrating" until then).
5. Open **設定 (Settings)** to pick an output mode and tune keybinds/sensitivity to taste.

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
dotnet build WiiFitToVRC.sln
```

To produce the self-contained single-file exe shipped at the repo root:

```
powershell -File publish.ps1
```

## Project structure

```
WiiFitToVRC.exe          Prebuilt self-contained executable (see publish.ps1)
publish.ps1               Rebuilds and republishes WiiFitToVRC.exe
src/
  WiiFitToVRC.Core/        Domain logic: Bluetooth pairing, HID I/O, gesture detection,
                           settings, localization, input output (keyboard/mouse/controller/OSC)
  WiiFitToVRC.App/         WinForms UI (monitor window + settings dialog)
tools/
  PairTool/                Standalone console tool for testing balance board pairing in isolation
  ClassifyTest/             Offline replay tool: re-runs the gesture classifiers against a
                           recorded CSV log, for tuning thresholds without live hardware
reference/
  WiiBalanceWalker_v0.4/    InTheHand.Net.Personal.dll (32feet.NET), used for Bluetooth device
                           management — see the accompanying README.txt for attribution
docs/
  BALANCE_BOARD.md          Balance board Bluetooth/HID protocol details
  GESTURE_DETECTION.md      How each gesture is classified, and the settings that tune it
  VRCHAT_INPUT.md           Why plain SendInput doesn't work in VRChat, and the three fixes used
```

## Settings reference

All settings are edited from the in-app settings window (⚙ 設定) and persisted to
`settings.json` next to the exe. Nothing needs to be hand-edited, but a summary:

| Setting | What it does |
|---|---|
| Output mode | Keyboard / Keyboard+Mouse / Virtual Controller / VRChat OSC (see [docs/VRCHAT_INPUT.md](docs/VRCHAT_INPUT.md)) |
| Language | UI language, or Auto to follow Windows |
| Turn sensitivity | Mouse pixels-per-tick (keyboard+mouse mode) or stick deflection % (controller mode), separately for left/right |
| Presence weight threshold | Calibrated total weight that counts as "someone is on the board" |
| Sleep/wake seconds | How long presence must hold (both directions) before output locks/unlocks |
| Footstep threshold % | How far above the learned resting weight a corner must spike to count as a footstep — see [docs/GESTURE_DETECTION.md](docs/GESTURE_DETECTION.md) |
| Dash detection (ms) | Footstep-to-footstep interval fast enough to count as a dash instead of a walk |
| Stride length (ms) | How long a confirmed step persists after its last footstep before releasing back to Idle |
| Gesture sensitivity (Turn/Jump/Crouch) | Independent Weak-to-Strong sliders for how easily each one fires (forward/backward/dash isn't affected). The middle (default) keeps the original detection thresholds unchanged |
| Crouch / Jump enabled | Toggle each gesture off entirely (no key output, no light) |
| Turning enabled | When off, turning isn't detected at all, so no turn-equivalent output is ever sent in any output mode -- mouse, keyboard, controller, or OSC (forward/backward/dash are unaffected) |
| Debug mode | Shows the raw CSV recording controls used to capture logs for `ClassifyTest` |
| Keybinds tab | Per-action key (and the dash modifier key) for keyboard output modes |
| Controller tab | Per-action button and stick deflection for virtual controller mode |

## License

[MIT](LICENSE) for this project's own code. The bundled `InTheHand.Net.Personal.dll` is a
third-party library (32feet.NET) — see [reference/WiiBalanceWalker_v0.4/WiiBalanceWalker_v0.4/README.txt](reference/WiiBalanceWalker_v0.4/WiiBalanceWalker_v0.4/README.txt)
for its own attribution.
