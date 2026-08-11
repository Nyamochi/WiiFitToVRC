# WiiFitToVRC

[日本語](README.md) | **English** | [한국어](README.ko.md) | [简体中文](README.zh-Hans.md) | [繁體中文](README.zh-Hant.md)

Turn a Wii Balance Board into a walking controller for VRChat (or any other Windows
application). Stand on the board, shift your weight to walk/turn/jump/crouch, and the app turns
that into keyboard/mouse input, a virtual Xbox 360 controller, or VRChat's own OSC input.

## Quick and easy setup (no technical knowledge required)

1. Turn on Bluetooth in Windows (if your PC has no built-in Bluetooth antenna, get a USB dongle
   for it).
2. Click `WiiFitToVRC.exe` at the top of this repository to download it (no installation step).
3. Double-click the downloaded file to run it.
4. The app starts searching automatically as soon as it opens. Just press the **SYNC** button
   inside the battery compartment of the balance board and it connects on its own -- no need to
   click the connect button. The plain **POWER** button can't be used for this due to a hardware
   limitation, so you'll need to press SYNC every time you connect.
5. Calibration also starts automatically once connected. A "please place the board on the floor"
   message appears, then calibration begins on its own 5 seconds later -- have the board on the
   floor with nothing on it by then.
6. Once calibration finishes, step on the board and stand normally for a moment and you're ready
   to go. Launch VRChat and shift your weight on the board to walk.

Note: to confirm the board is connected properly, open Notepad and step on the board -- if it
types w/s/a/d, everything's working. If the game still doesn't respond (for example, in VR mode),
first enable OSC in VRChat itself, then try switching the output mode in Settings to VRChat OSC.

See the [docs](docs/) folder for a deeper explanation of each feature if something doesn't work as
expected.

## What it can do

- Walk
- Dash
- Turn left/right
- Jump
- Crouch

## Requirements

- Windows 10/11
- A Wii Balance Board (Bluetooth) — discontinued, but commonly found cheaply secondhand
- A Bluetooth adapter that supports HID devices

### Using the virtual controller (optional)

[ViGEmBus](https://github.com/nefarius/ViGEmBus/releases) (a real kernel driver — this app cannot
install it for you; download and install it yourself)

## Caution

Jumping is outside the Wii Balance Board's original hardware spec. Watch your surroundings for
damage to the board or the floor, and **if you weigh over 100kg (220lb), please don't jump on
it** -- even a light jump is enough to register.

## Works with other games too

The app's output is plain keyboard WASD (or mouse) input, so as long as a game accepts WASD
movement, this app works with it too, whether or not that game officially supports it. Games it's
been tried with:

- Death Stranding
- Resident Evil
- Monster Hunter
- Armored Core IV

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
  - VRChat OSC input, for VR setups where the game's input focus is locked to the VR device and
    rejects synthetic input entirely, even the virtual controller — see
    [docs/VRCHAT_INPUT.md](docs/VRCHAT_INPUT.md).
  - Virtual controller, for games that reject synthetic keyboard/mouse input (VRChat
    included) — see [docs/VRCHAT_INPUT.md](docs/VRCHAT_INPUT.md).
- Fully configurable keybinds/controller bindings, turn speed, weight thresholds, and
  timing, all from the in-app settings window.
- Localized UI: auto-detects the Windows display language, with English, Japanese, Simplified &
  Traditional Chinese, Korean, French, German, and Italian built in.

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
| Output mode | Keyboard / Keyboard+Mouse / VRChat OSC / Virtual Controller (see [docs/VRCHAT_INPUT.md](docs/VRCHAT_INPUT.md)) |
| Language | UI language, or Auto to follow Windows |
| Gesture sensitivity (Walk/Dash/Turn/Jump/Crouch/Stride/Walk-Dash continuation/Steps until continuation) | Independent Weak-to-Strong sliders for how easily each one fires (Stride and Walk/Dash continuation are Narrow-to-Wide instead, and Steps until continuation is a plain 1-5 step count, default 3; forward/backward isn't affected). The middle (default) keeps the original detection thresholds unchanged. For Dash/Turn/Jump/Crouch, dragging all the way to Weak (0) fully disables that gesture -- it never fires regardless of input — see [docs/GESTURE_DETECTION.md](docs/GESTURE_DETECTION.md) |
| Turn mode | Switch the turn detection logic between Hold (lean left/right and hold it) and Footstep (alternate the diagonal panels, the default) |
| Dash input method | Switch how keyboard/keyboard+mouse modes send Dash between Combo key (modifier + forward key, the default) and Double-tap key (tap the forward key once, then hold it) |
| Turn speed | Its absolute value differs per output mode, so the slider's range/value swaps automatically every time you switch the output mode radio -- one shared value for both directions. Hidden (shows "No setting") when Keyboard (Q/E) mode is selected |
| Presence weight threshold | Calibrated total weight that counts as "someone is on the board" |
| Sleep/wake seconds | How long presence must hold (both directions) before output locks/unlocks |
| Debug mode | Shows the raw CSV recording controls used to capture logs for `ClassifyTest` |
| Keybinds tab | Per-action key (and the dash modifier key) for keyboard output modes |
| Controller tab | Per-action button for virtual controller mode (turn speed is set on the General tab's "Turn speed" row) |

## License

[MIT](LICENSE) for this project's own code. The bundled `InTheHand.Net.Personal.dll` is a
third-party library (32feet.NET) — see [reference/WiiBalanceWalker_v0.4/WiiBalanceWalker_v0.4/README.txt](reference/WiiBalanceWalker_v0.4/WiiBalanceWalker_v0.4/README.txt)
for its own attribution.

For feedback or bug reports, contact the creator on X: [@nyamo_chi](https://x.com/nyamo_chi)
