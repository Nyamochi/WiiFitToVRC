# Getting input into VRChat

Synthetic input (`SendInput`) that works perfectly in most Windows applications can be silently
ignored by VRChat. This app has three different fixes for that, used depending on output mode.

## The problem

A naive `SendInput` `KEYBDINPUT` populated with only a virtual-key code (`wVk`) and no scan code
reached applications like Notepad just fine, but VRChat never reacted to it at all — no movement,
no jump, nothing — even though the exact same code path worked for other apps. Mouse-move input
(`MOUSEEVENTF_MOVE`) was **not** affected; only keyboard input was filtered.

A real physical keyboard driver always reports a hardware scan code alongside (or instead of) a
virtual-key code. Software that only sets `wVk` and leaves `wScan` at 0 produces an event that's
missing information a genuine keystroke would always carry — which is apparently enough for
VRChat (or the input layer underneath it) to treat it as synthetic and drop it.

## Fix 1: scan-code-based `SendInput` (keyboard / keyboard+mouse output modes)

[`KeySender.cs`](../src/WiiFitToVRC.Core/Input/KeySender.cs) sends every key event with
`wVk = 0`, `wScan` set via `MapVirtualKey(vk, MAPVK_VK_TO_VSC)`, and the `KEYEVENTF_SCANCODE` flag
(plus `KEYEVENTF_EXTENDEDKEY` for the arrow-cluster keys, whose scan codes are in the extended
range). This is the same technique long-established macro/remapping tools (e.g. JoyToKey) use, and
it's accepted by VRChat the same way a real keyboard's input is.

Mouse movement was already working with plain `SendInput` `MOUSEEVENTF_MOVE`, so
[`MouseSender.cs`](../src/WiiFitToVRC.Core/Input/MouseSender.cs) didn't need any change.

### One more wrinkle: taps must have real duration

Jump and crouch are momentary presses (down, then immediately up), unlike the held movement keys.
Sending the down and up back-to-back with no gap between them meant the "down" edge could come and
go within the same frame VRChat polls input on, so it was never observed. Jump/crouch key events
now hold the key down for a short, real duration (`TapHoldMs` in
[`InputController.cs`](../src/WiiFitToVRC.Core/Input/InputController.cs)) before releasing it,
driven off the same per-sample update loop that handles everything else (no extra threads, so it
can't race the key state bookkeeping in `KeySender`).

## Fix 2: virtual controller (controller output mode)

Even with correct scan codes, keyboard/mouse `SendInput` is still, structurally, synthetic input —
some environments filter it regardless of how convincing it looks. The more robust fix is to not
send keyboard/mouse input at all, and instead present a **virtual Xbox 360 controller** to Windows,
via the [ViGEmBus](https://github.com/nefarius/ViGEmBus) kernel driver and the
`Nefarius.ViGEm.Client` NuGet package
([`VirtualControllerSender.cs`](../src/WiiFitToVRC.Core/Input/VirtualControllerSender.cs)). From
Windows' (and VRChat's) perspective this is indistinguishable from a real gamepad.

- Movement maps to the left stick (Y axis: forward/backward/dash).
- Turning maps to the right stick (X axis), with separately configurable left/right deflection.
- Jump, crouch, and dash (sprint) map to configurable buttons, defaulting to VRChat's own gamepad
  conventions (A = jump, left-stick click = sprint).

**This requires ViGEmBus to actually be installed** — it's a real kernel driver, and this app
cannot install it on your behalf. Grab it from the
[ViGEmBus releases page](https://github.com/nefarius/ViGEmBus/releases). If it's missing, the
Controller tab in Settings shows the specific reason instead of silently doing nothing.

### Steam Input can still get in the way

If Steam is running, it may intercept the virtual Xbox controller through **Steam Input** before
VRChat ever sees it, especially if VRChat is launched through Steam. If the app connects the
virtual controller successfully (Windows/Steam both show it as connected) but VRChat still doesn't
respond, disable Steam Input specifically for VRChat: in your Steam library, right-click
**VRChat → Properties → Controller**, and set the per-game override to **Disable Steam Input**.

## Fix 3: VRChat's own OSC input (OSC output mode)

Some VR headset/runtime setups lock the game's input focus to the VR device entirely, and reject
`SendInput` no matter what it looks like -- keyboard, mouse, or even the virtual controller from
Fix 2. For that case, [`OscSender.cs`](../src/WiiFitToVRC.Core/Input/OscSender.cs) skips
`SendInput` altogether and talks to
[VRChat's own OSC input feature](https://docs.vrchat.com/docs/osc-as-input-controller) instead --
a local UDP message VRChat listens for directly, independent of window focus or input-device
locking.

- `/input/Vertical` / `/input/Horizontal` (float axes) carry forward/backward movement. Forward's
  own magnitude isn't a flat 1.0 -- like a real analog stick's tilt, it scales continuously with
  actual stride cadence (see `InputController.ComputeOscForwardMagnitude`), from 0.5 at ordinary
  walking pace up to 1.0 at a full dash, tuned against real recordings' median stride gaps
  (~450ms walking, ~230ms dashing). Backward stays a flat -1.0 either way.
- `/input/LookLeft` / `/input/LookRight` (bool) carry turning. VRChat's OSC address list also
  defines a `/input/LookHorizontal` float axis, but it didn't reliably turn the character in
  practice even while the value was visibly changing in VRChat's own OSC debug view -- the
  discrete LookLeft/LookRight buttons (the same ones an actual VR controller's turn input maps to)
  work reliably instead.
- `/input/Run` (bool) is set while dashing, and `/input/Jump` (bool) for jump.
- Messages are sent to `127.0.0.1:9000`, VRChat's default local OSC receive port, and only when a
  value actually changes (not every sample), matching VRChat's own OSC address list exactly.

**VRChat has no OSC address for crouch.** It isn't part of VRChat's official OSC input list at
all, so crouch is not sent through this path -- `InputController` always falls back to a plain
`CrouchKey` press for it, even while OSC output mode is otherwise active. If your environment
blocks `SendInput` entirely, crouch may not work while everything else does; this is a VRChat OSC
limitation, not something this app can work around.

**VRChat's OSC input must be enabled** in-game: Action Menu → Options → OSC → Enabled (it's on by
default in recent VRChat versions, but worth checking if OSC mode doesn't seem to do anything).
