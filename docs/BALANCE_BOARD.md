# Wii Balance Board: pairing and protocol

The balance board is a Wii peripheral. It talks Bluetooth HID and looks, from Windows' point of
view, like a generic Bluetooth HID device once paired — no vendor driver required.

## Pairing (no PIN)

The balance board is Bluetooth-discoverable only while its **SYNC** button (inside the battery
compartment) is held/pressed, similar to a Wii Remote. The important, non-obvious part: **it does
not use a PIN**. Naively calling Windows' standard PIN/passkey authentication APIs
(`BluetoothAuthenticateDevice`/`BluetoothAuthenticateDeviceEx`) against it fails or hangs.

The actual working sequence (see
[`BalanceBoardPairing.cs`](../src/WiiFitToVRC.Core/Bluetooth/BalanceBoardPairing.cs)) alternates
between two strategies indefinitely, since there's no way to know in advance which one (or when)
will actually catch the board's connectable window:

1. **Remembered-profile burst**: if Windows already has a *remembered* (bonded) device record
   matching the board from an earlier SYNC pairing, re-assert the HID service on it
   (`SetServiceState(BluetoothService.HumanInterfaceDevice, true)`) and check
   `BluetoothDeviceInfo.Connected` (after `Refresh()`, since it's cached at discovery time) a
   short moment later. **A single attempt essentially never works**, though: the board is only
   Bluetooth-connectable for roughly 2 seconds after its plain power button is pressed -- far
   shorter than a discovery scan takes to run -- so this repeats the nudge-and-check up to 8 times,
   300ms apart, before moving on. (An earlier version of this code erased and re-paired from
   scratch on every connect, and only tried the nudge once, which meant SYNC was effectively
   required every single time even for a board Windows already knew about, since a lone attempt
   almost never landed inside that brief window.)
2. **SYNC-mode scan**: one call to `BluetoothClient.DiscoverDevices(255, false, false, true)` (via
   32feet.NET's `InTheHand.Net.Personal.dll`), which only sees the board if it's actively in SYNC
   mode *during* that scan. If it shows up, call `SetServiceState(BluetoothService.HumanInterfaceDevice,
   true)` on it -- that's it, no PIN exchange, no `PairRequest`. This alone gets Windows to install
   it as an HID device, and remembers it for step 1 to use on future connects.

If neither strategy finds the board, the whole cycle repeats from step 1, with no attempt cap or
overall timeout -- the app just keeps trying until the board actually shows up or the user cancels.

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
