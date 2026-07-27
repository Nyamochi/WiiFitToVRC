# Gesture detection

All gesture detection works from the **calibrated** 4-corner reading (see
[BALANCE_BOARD.md](BALANCE_BOARD.md)): `TopRight`, `TopLeft`, `BottomRight`, `BottomLeft` (each
≥ 0, zero-offset already removed) plus each corner's percentage share of the total.

Two axes are derived from those percentages and used throughout:

```
X (left-right) = (TopRight% + BottomRight%) - (TopLeft% + BottomLeft%)   positive = weight toward the right
Y (front-back) = (TopRight% + TopLeft%)     - (BottomRight% + BottomLeft%)  positive = weight toward the front
```

## Presence: is anyone even on the board?

[`PresenceGate.cs`](../src/WiiFitToVRC.Core/Motion/PresenceGate.cs) gates *all* output (including
the arrow lights in the app window) on the calibrated total weight crossing the **presence weight
threshold** setting. Both directions share one **sleep/wake seconds** delay: the board must stay
above the threshold continuously for that long before output unlocks (so a stray brush against the
board doesn't start sending input), and must stay below it continuously for the same duration
before output re-locks (so momentary weight dips during normal play don't cut output, and stepping
off takes a moment to register).

## The weight reference: what does "resting" look like right now?

Forward/backward detection (below) needs to know what a *quiet, resting* corner value looks like,
so it can tell a real footstep apart from background noise. Rather than a fixed number, the app
learns this continuously: [`ReferenceWeightCalibrator.cs`](../src/WiiFitToVRC.Core/Motion/ReferenceWeightCalibrator.cs)
samples the calibrated reading every 5 seconds while the board is present and nothing is currently
detected (i.e. the person is just standing there). Once 5 samples (25 seconds) have accumulated,
it checks whether that window is essentially flat — the standard deviation of the total weight is
under a fixed threshold. Any window that qualifies becomes the new reference outright, replacing
whatever was there before.

This is deliberately *not* "lock onto the single steadiest moment ever seen" — walking spikes the
total on every step, so a window spanning any real movement fails the flatness check and the
reference holds; standing still keeps refreshing it. That means if one person steps off and a
different (lighter or heavier) person steps on and stands still, the reference naturally drifts to
match them within the next 25-second quiet stretch, rather than staying anchored to whoever
happened to stand stillest first. The status bar shows "体重キャリブレーション中" / "Weight
calibrating" until the first reference is established, and briefly shows a confirmation message
each time it's refreshed afterward.

A fresh sensor calibration (stepping off and recalibrating the zero-point) invalidates the weight
reference too, since every value in it is only meaningful relative to that offset — it resets
automatically alongside a sensor recalibration.

## Forward / backward / dash: footstep alternation

Each of the 4 corners independently watches for the moment its value crosses **footstep
threshold %** of the weight reference (e.g. 120%) — a discrete "this foot just landed" event.

- A landing on a **front** corner (top-right or top-left) only counts while `Y ≥ 0` (leaning at
  least slightly forward); a landing on a **back** corner only counts while `Y ≤ 0`. This keeps a
  step from being attributed to the wrong direction.
- **Front-right then front-left** (or vice versa) within a short window (`AlternationWindowMs`,
  900ms) is a confirmed walking step → **Forward**. The same pairing on the back corners →
  **Backward**.
- If the two landings are closer together than **dash detection (ms)** (default 300ms), it's a
  **Dash** instead of a plain walk.
- A confirmed direction persists for a short hold (`HoldMs`) after its last confirming landing;
  each new landing refreshes the hold, so continuous walking doesn't flicker back to Idle between
  steps.

Leaning forward and holding that lean *without* alternating feet does **not** count as Forward —
it reads as Idle. Only an actual confirmed footstep pair produces movement.

## Turning: a sustained, deliberate lean

Turning uses a completely different, narrower model from stepping: X has to swing past **±40**
and *stay* past it continuously for **400ms** before a turn is confirmed — an ordinary step's
natural left-right sway crosses that instantaneous threshold plenty, but flips side to side too
quickly to ever hold it for the full duration, which is exactly what filters it out. Once
confirmed, a turn only releases once X falls back under a lower bound (**±25**, hysteresis), and a
confirmed turn always wins over stepping while it's active. Once one side fires, the opposite side
is blocked from firing for 500ms (rebound-cooldown), since the recoil of a hard weight shift can
briefly nudge the other side past its own threshold too.

## Jump: rise, then rapid collapse

[`JumpDetector.cs`](../src/WiiFitToVRC.Core/Motion/JumpDetector.cs) tracks a slow-moving baseline
of the calibrated total weight. Firing on the push-off spike alone turned out to be
indistinguishable from a fast crouch (committing weight forward quickly can also spike the total
briefly), so a jump is only confirmed once the spike is *followed by* a rapid collapse toward
near-zero weight (the moment the feet actually leave the board) within half a second. If the spike
never collapses that way, it wasn't a jump, and nothing fires.

## Crouch: slow and sustained, not a spike

[`CrouchDetector.cs`](../src/WiiFitToVRC.Core/Motion/CrouchDetector.cs) watches Y for a sustained
forward lean (`Y > 45`), but — since a jump's fast push-off can also cross that instantaneous
level — only confirms crouch once Y has stayed above the threshold *continuously* for 500ms. A
jump's front-loading is a brief spike immediately followed by the airborne weight collapse (see
above), so it can't sustain the hold; a real crouch settles in and stays there. Standing back up
isn't rate-gated — any drop back below the lower threshold (`Y < 30`) ends the crouch immediately.

Crouch is also suppressed for 500ms after any forward/backward/turn/jump last fired, since those
can transiently disturb the front-back balance enough to look like the start of a crouch.

### Crouch is a toggle, not a hold

Unlike the movement keys (held down while active), crouch/stand in VRChat is a single **toggle**
binding — one press crouches, the next press stands back up. So instead of holding a key down
while `IsCrouching` is true, the app sends exactly one tap on *each* transition (crouch starting,
and crouch ending). This pairing has to stay exact: if a tap were ever silently dropped, the game
and the app's idea of the crouch state would end up permanently inverted relative to each other.
