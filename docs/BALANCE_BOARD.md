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

1. Remove any existing (possibly stale) device record for the board.
2. Repeatedly call `BluetoothClient.DiscoverDevices(255, false, false, true)` (via 32feet.NET's
   `InTheHand.Net.Personal.dll`) until the board shows up in the SYNC-discoverable device list.
   This can take several attempts since the discovery window and the SYNC button's active window
   don't perfectly line up. No attempt cap or overall timeout -- the app just keeps scanning until
   the board shows up or the user cancels.
3. Call `target.SetServiceState(BluetoothService.HumanInterfaceDevice, true)` on the discovered
   device. That's it — no PIN exchange, no `PairRequest`. This alone gets Windows to install it as
   an HID device.

Once paired this way, Windows treats it as a normal Bluetooth HID device, including on future
app/system restarts (the app's startup auto-connect just tries to open the HID device directly,
without repeating the pairing dance, and falls back to the full flow only if that fails).

### SYNC is required every time -- reconnecting via the remembered profile alone doesn't work

It's tempting to skip SYNC entirely once Windows already has a bonded profile for the board (a
plain power-on ought to be enough for an ordinary Bluetooth HID peripheral to reconnect). Two
different approaches to that were tried and abandoned after diagnostic capture with
[`tools/BluetoothMonitor`](../tools/BluetoothMonitor) showed neither ever actually reconnects, even
nudged continuously across multiple power-button presses:

- Repeatedly re-asserting `SetServiceState(BluetoothService.HumanInterfaceDevice, true)` on the
  remembered device and polling `BluetoothDeviceInfo.Connected`.
- The same, but also trying to open the HID device directly on each poll.

The recorded profile for this board shows `Authenticated = false` (unlike a normal Bluetooth
peripheral's remembered profile) -- consistent with the no-PIN pairing trick above, which never
performs real Bluetooth authentication. Windows' background HID auto-reconnect for a bonded device
appears to require a genuinely authenticated bond, which this board's profile structurally isn't,
so no amount of nudging brings it back on its own. SYNC (steps 1-3 above) is required every time.

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
