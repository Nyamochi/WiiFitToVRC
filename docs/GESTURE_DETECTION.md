# Gesture detection

All gesture detection works from the **calibrated** 4-corner reading (see
[BALANCE_BOARD.md](BALANCE_BOARD.md)): `TopRight`, `TopLeft`, `BottomRight`, `BottomLeft` (each
≥ 0, zero-offset already removed) plus each corner's percentage share of the total.

One axis is derived from those percentages and used throughout:

```
Y (front-back) = (TopRight% + TopLeft%) - (BottomRight% + BottomLeft%)  positive = weight toward the front
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
- If the two landings are closer together than the dash period (default 300ms, tuned via
  **Gesture sensitivity: Dash** in Settings), it's a **Dash** instead of a plain walk. At Dash
  sensitivity 0 ("Weak"), the period is forced to 0ms -- no landing interval is ever shorter than
  that, so Dash can never fire and every alternation reads as a plain Forward/Backward step
  instead.
- A confirmed direction persists for a short hold (**stride length (ms)**, default 77ms) after its
  last confirming landing; each new landing refreshes the hold, so continuous walking doesn't
  flicker back to Idle between steps.

Leaning forward and holding that lean *without* alternating feet does **not** count as Forward —
it reads as Idle. Only an actual confirmed footstep pair produces movement.

### Forward is sticky against backward/turn noise mid-stride

A real forward footstep sometimes lands hard enough to also light up a corner that belongs to a
*different* pair -- e.g. the back-right panel crossing its own footstep threshold, or (see Turning
below) crossing the diagonal turn threshold -- even though nothing about the actual movement
changed. If that happens while forward is still genuinely in progress (a front corner has been
touched within the last `AlternationWindowMs`, 900ms -- long enough to span normal stride cadence,
not just the much shorter stepHoldMs output-hold window), the competing Backward/TurnLeft/TurnRight
confirmation is treated as noise from the same stride: the forward hold is simply refreshed instead
of switching direction. Once forward actually stops (no front-corner touch for a real pause), the
same signal is trusted normally, so turning around or walking backward still works -- it just has
to follow an actual stop rather than happening mid-stride. This only protects **Forward**, not
Dash, since Dash comes from the same front-corner pair rather than a competing one.

## Turning: a diagonal footstep alternation

Turning right by alternately stepping on the **back-right** and **front-left** panels (in either
order) reads as a right turn; alternately stepping **front-right**/**back-left** reads as a left
turn. This is the same "watch a corner cross a threshold, pair it with the opposite corner's next
crossing within `AlternationWindowMs`" alternation as forward/backward/dash above, just on the
*diagonal* pair of corners instead of the front or back pair -- and unlike forward/backward, the
threshold here is a plain percentage of total board weight on that one corner (**turn threshold
%**, default 50%), not relative to a learned resting reference.

A turn confirms on the **second** step of the pair: a single corner crossing 50% is never enough by
itself, it just becomes the pending first step waiting for the diagonal partner's next crossing.
Once confirmed it behaves exactly like a footstep -- it holds for **stride length (ms)** after its
last confirming step and gets refreshed by continuing to alternate, releasing back to Idle the same
way stepping does once the alternation stops.

**Turning enabled** in Settings turns this off entirely, not just its output: when disabled, any
pending first step is dropped immediately, so a stale one can't pair up the instant it's
re-enabled. No turn-equivalent output is sent in any output mode while disabled -- no turn keys, no
mouse-look movement, no right-stick deflection, and no OSC `LookHorizontal` messages. **Gesture
sensitivity: Turn** at 0 ("Weak") has the identical effect, so either one alone is enough to fully
suppress turning.

The 50% figure above is the value at the default **Gesture sensitivity: Turn** setting (see below)
-- it scales as that setting moves away from its default, the same way Walk/Dash's thresholds do
(lower percentage is easier to trigger, so the display direction is inverted -- 100 is
"Strong"/easiest, 0 is "Weak" and fully disables turning).

An earlier version of this model used a completely different approach: X (left-right weight) had to
swing past a threshold and *stay* there continuously for 400ms. It was replaced outright (not kept
as a second path alongside this one) after real gait logs showed it could false-trigger the
*opposite* turn during this diagonal alternation's own large, brief X swings.

## Jump: rise, then rapid collapse

[`JumpDetector.cs`](../src/WiiFitToVRC.Core/Motion/JumpDetector.cs) tracks a slow-moving baseline
of the calibrated total weight. Firing on the push-off spike alone turned out to be
indistinguishable from a fast crouch (committing weight forward quickly can also spike the total
briefly), so a jump is only confirmed once the spike is *followed by* a rapid collapse toward
near-zero weight (the moment the feet actually leave the board) within half a second. If the spike
never collapses that way, it wasn't a jump, and nothing fires. **Gesture sensitivity: Jump** scales
how large that initial push-off spike must be (relative to the baseline) to arm; the
collapse/settle shape that follows is left fixed, since it describes what a real jump looks like
rather than how hard you have to move. At sensitivity 0 ("Weak"), the spike can never arm at all --
there's no separate "Jump enabled" toggle; this is the only way to disable jump.

## Crouch: slow and sustained, not a spike

[`CrouchDetector.cs`](../src/WiiFitToVRC.Core/Motion/CrouchDetector.cs) watches Y for a sustained
forward lean (`Y > 45`), but — since a jump's fast push-off can also cross that instantaneous
level — only confirms crouch once Y has stayed above the threshold *continuously* for 500ms. A
jump's front-loading is a brief spike immediately followed by the airborne weight collapse (see
above), so it can't sustain the hold; a real crouch settles in and stays there. Standing back up
isn't rate-gated — any drop back below the lower threshold (`Y < 30`) ends the crouch immediately.
These figures are likewise the defaults at **Gesture sensitivity: Crouch** = 50; the enter/exit Y
thresholds and the hold duration all scale together with that setting. At sensitivity 0 ("Weak"),
crouch can never be entered, and immediately releases if it was already active when the setting
changed -- there's no separate "Crouch enabled" toggle; this is the only way to disable crouch.

## Gesture sensitivity: independent dials for walk/dash/turn/jump/crouch/stride

Settings has six of these sliders, grouped under "Gesture sensitivity": **Walk**, **Dash**,
**Turn**, **Jump**, **Crouch**, and **Stride**. Each is shown as a plain 0-100 percentage (Weak to
Strong, except Stride which is Narrow to Wide), independent of the others -- turning up Jump
doesn't touch Turn or Crouch. 50 is always the default/neutral position, reproducing the original
hardcoded values unchanged.

- **Turn/Jump/Crouch** feed directly into
  [`GestureSensitivityScale.cs`](../src/WiiFitToVRC.Core/Motion/GestureSensitivityScale.cs), which
  turns the 0-100 value into a multiplier applied to that gesture's thresholds:
  `1.0 - (sensitivity - 50) * 0.01`. Each point away from 50 is a 1% change, so 100 is 0.5x
  (thresholds/durations 50% smaller, easier to trigger -- "Strong") and moving toward 0 makes them
  larger and harder to trigger -- but 0 itself isn't just "1.5x harder", it's a hard cutoff (see
  `GestureSensitivityScale.IsDisabled`): each detector skips its trigger condition entirely at
  sensitivity 0, so the gesture can never fire no matter how extreme the input is.
- **Walk** and **Dash** scale the same way, but in the *opposite* raw-value direction (a *smaller*
  footstep-threshold-% or dash-period-ms is what's easier to trigger), so the settings UI inverts
  the display so 100 ("Strong") still means "easier" and 0 ("Weak") still means "harder", matching
  the other four. Dash additionally hard-disables at 0 (see the Forward/backward/dash section
  above); Walk does not have a hard-disable, since disabling it would also disable forward/backward
  walking entirely.
- **Stride** scales the stride-length hold duration; it isn't a Weak/Strong "how easily does this
  fire" dial like the other five (nothing about stride makes a gesture more or less likely), so it
  uses Narrow/Wide labels instead and has no disable behavior.

None of the six affect forward/backward, which has its own separate **Footstep threshold %**
raw value (mapped to the "Walk" slider) as described above.

Crouch is also suppressed for 500ms after any forward/backward/turn/jump last fired, since those
can transiently disturb the front-back balance enough to look like the start of a crouch.

### Crouch is a toggle, not a hold

Unlike the movement keys (held down while active), crouch/stand in VRChat is a single **toggle**
binding — one press crouches, the next press stands back up. So instead of holding a key down
while `IsCrouching` is true, the app sends exactly one tap on *each* transition (crouch starting,
and crouch ending). This pairing has to stay exact: if a tap were ever silently dropped, the game
and the app's idea of the crouch state would end up permanently inverted relative to each other.
