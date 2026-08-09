# Wii Balance Board: pairing and protocol

The balance board is a Wii peripheral. It talks Bluetooth HID and looks, from Windows' point of
view, like a generic Bluetooth HID device once paired — no vendor driver required.

## Pairing (no PIN)

The balance board is Bluetooth-discoverable only while its **SYNC** button (inside the battery
compartment) is held/pressed, similar to a Wii Remote. The important, non-obvious part: **it does
not use a PIN**. Naively calling Windows' standard PIN/passkey authentication APIs
(`BluetoothAuthenticateDevice`/`BluetoothAuthenticateDeviceEx`) against it fails or hangs.

The actual working sequence (see
[`BalanceBoardPairing.cs`](../src/WiiFitToVRC.Core/Bluetooth/BalanceBoardPairing.cs)):

1. Check whether Windows already has a *remembered* (bonded) device record matching the board from
   an earlier SYNC pairing. If so, call `SetServiceState(BluetoothService.HumanInterfaceDevice,
   true)` on it directly and stop there -- re-asserting the HID service against an existing bond
   is enough for Windows to reconnect as soon as the board is powered on and in range, with no
   SYNC button needed at all. (An earlier version of this code erased and re-paired from scratch
   on every connect to dodge a stuck HID service registration, but that meant SYNC was required
   every single time, even for a board Windows already knew about.)
2. If no remembered record exists (first-ever pairing, or the stored bond didn't actually
   reconnect), fall back to fresh SYNC-mode pairing: repeatedly call
   `BluetoothClient.DiscoverDevices(255, false, false, true)` (via 32feet.NET's
   `InTheHand.Net.Personal.dll`) until the board shows up in the SYNC-discoverable device list.
   This can take several attempts since the discovery window and the SYNC button's active window
   don't perfectly line up.
3. Call `target.SetServiceState(BluetoothService.HumanInterfaceDevice, true)` on the discovered
   device. That's it — no PIN exchange, no `PairRequest`. This alone gets Windows to install it as
   an HID device, and it's now remembered for step 1 to use next time.

Once paired this way, Windows treats it as a normal Bluetooth HID device, including on future
app/system restarts (the app's startup auto-connect just tries to open the HID device directly,
without repeating the pairing dance, and falls back to the full flow only if that fails).

## Reading sensor data (raw HID, no L2CAP)

No raw L2CAP sockets or vendor SDKs are needed — the board exposes itself as a standard Windows HID
device once paired, so it's just `SetupDiGetClassDevs`/`CreateFile`/`ReadFile`/`WriteFile` against
the HID device path (see [`BalanceBoardDevice.cs`](../src/WiiFitToVRC.Core/Hid/BalanceBoardDevice.cs)).
Vendor ID `0x057E` (Nintendo), product ID `0x0306`.

**Bring-up sequence**, sent once after opening the device:

1. Write report `0x15 0x00` — request status.
2. Write memory `0xA400F0 = 0x55` — enable the extension controller data pipe.
3. Write memory `0xA400FB = 0x00` — set the extension register to un-encrypted mode.
4. Write report `0x12 0x04 0x32` — switch on continuous reporting mode `0x32`.

After that, the board streams unsolicited `0x32` input reports on its own.

**Keep-alive**: the board silently drops the connection a few minutes into a session if the host
never writes anything back, even while it's actively streaming data. The app re-sends the `0x15`
status request every 5 seconds on a timer to keep the link alive.

## Report format (report ID `0x32`)

22-byte HID reports. Byte layout for report ID `0x32`:

| Bytes | Field |
|---|---|
| 0 | Report ID (`0x32`) |
| 1–2 | Core buttons (ignored — the board doesn't have any) |
| 3–4 | Top-right sensor, big-endian `ushort` |
| 5–6 | Bottom-right sensor, big-endian `ushort` |
| 7–8 | Top-left sensor, big-endian `ushort` |
| 9–10 | Bottom-left sensor, big-endian `ushort` |

The 4 raw values are uncalibrated strain-gauge readings, not kilograms — they need a per-board,
per-session zero-point calibration before they mean anything comparable across sessions or
boards (see [`SensorCalibration.cs`](../src/WiiFitToVRC.Core/Hid/SensorCalibration.cs)):

- Sample all 4 corners for 10 seconds with nobody on the board.
- Take the **statistical mode** (most frequently observed raw value) of each corner as its
  zero-point offset — more robust to a stray low outlier during sampling than a plain minimum.
- From then on, `calibrated = max(0, raw - offset)` per corner, plus the percentage each corner
  contributes to the calibrated total.

This calibrated reading (not the raw values) is what all gesture detection and the pressure-panel
display in the app are based on.
