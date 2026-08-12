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
- **Front-right then front-left** (or vice versa) within a short window (**Walk/Dash continuation**,
  default 800ms) is a confirmed walking step → **Forward**. The same pairing on the back corners →
  **Backward**.
- If the two landings are closer together than the dash period (default 300ms, tuned via
  **Gesture sensitivity: Dash** in Settings), it's a **Dash** instead of a plain walk. At Dash
  sensitivity 0 ("Insensitive"), the period is forced to 0ms -- no landing interval is ever shorter than
  that, so Dash can never fire and every alternation reads as a plain Forward/Backward step
  instead.

### Forward/Backward/Dash: the first few steps tap, later steps hold

A confirmed Forward/Backward/Dash direction doesn't hold for the same duration every time -- it
depends on whether this is an isolated short sequence or a genuinely ongoing one, tracked per
mechanism (front-corner, back-corner) as a streak count that resets once the gap since that
mechanism's last corner touch exceeds **Walk/Dash continuation** (`DirectionClassifier.HoldMsForStreak`).
Turn does *not* use this mechanism at all -- see the Footstep turn model below.

- The first **Steps until continuation** confirming steps of a fresh sequence (default 7) each hold
  for just **stride length (ms)** (default 70ms) -- a brief tap. This matters for someone who
  genuinely only wants to take a few steps: without it, those steps would hold the key down far
  longer than the steps themselves.
- Every confirming step from **Steps until continuation** onward holds for **stride length (ms) +
  Walk/Dash continuation** (default 70ms + 800ms). The continuation component is what actually
  matters here: ordinary stride cadence (several hundred ms between landings) is far slower than
  stride length alone, so holding for only stride length would release and re-press the key between
  literally every step of a real walk. Once enough steps confirm that this is a real ongoing
  sequence (not just a few deliberate steps), the long hold takes over and bridges the gaps between
  steps, keeping continuous walking/dashing held as one unbroken output for as long as the steps
  keep coming -- and stride length alone tunes the short coast *after* the sequence actually ends.

**Steps until continuation** (Settings, directly under **Walk/Dash continuation**) is a plain 1-15
step count, default 7 -- not an Insensitive/Sensitive or Narrow/Wide dial like the other Gesture
sensitivity sliders, since it's a count rather than a threshold.

**Walk/Dash continuation** (Settings, directly under **Stride**) is a Narrow/Wide 0-100 slider like
Stride, backed by a 400-1400ms raw range (default 800ms, displayed at 40% rather than the usual
50% midpoint -- this default was tuned from real gait logs rather than picked to land at the
range's center; see AppSettings.StepContinuationMs). Its raw value is
added on top of stride length to form the long-hold duration described above, and independently
sets how long a gap is still considered part of the same sequence for the alternation pairing (of
Forward/Backward *and* Turn, see below) and, for Forward/Backward/Dash only, the streak reset.

Leaning forward and holding that lean *without* alternating feet does **not** count as Forward —
it reads as Idle. Only an actual confirmed footstep pair produces movement.

### Dash input method: combo key or double-tap

Keyboard and keyboard+mouse output modes have a **Dash input method** switch (Settings, above the
**Turn speed** row): **Combo key** (the default) holds the forward key plus a modifier (e.g.
Shift+W); **Double-tap key** instead taps the forward key once and then holds it, for games whose
sprint binding is "double-tap forward" rather than a modifier key. The tap uses the same
`ForwardKey` binding from the Keybinds tab, not the separate Dash key/modifier bindings (which only
apply in Combo key mode).

The double-tap only happens once per Dash *episode* -- the moment Direction first becomes Dash, not
on every sample or every step of a continuing dash. It presses the forward key, waits
`TapHoldMs` (60ms, the same constant used for the jump/crouch tap pulse), then releases and
immediately re-presses it, holding from there on for as long as Dash keeps being confirmed (the
same continuous-hold mechanism described above). Controller and OSC output modes are unaffected by
this setting -- they always use the dash button / OSC `Run` regardless.

### Forward is sticky against backward/turn noise mid-stride

A real forward footstep sometimes lands hard enough to also light up a corner that belongs to a
*different* pair -- e.g. the back-right panel crossing its own footstep threshold, or (see Turning
below) crossing the diagonal turn threshold -- even though nothing about the actual movement
changed. If that happens while forward is still genuinely in progress (a front corner has been
touched within the last **Walk/Dash continuation** window, default 800ms -- long enough to span
normal stride cadence, not just the much shorter stride-length output-hold window), the competing
Backward/TurnLeft/TurnRight
confirmation is treated as noise from the same stride: the forward hold is simply refreshed instead
of switching direction. Once forward actually stops (no front-corner touch for a real pause), the
same signal is trusted normally, so turning around or walking backward still works -- it just has
to follow an actual stop rather than happening mid-stride. This only protects **Forward**, not
Dash, since Dash comes from the same front-corner pair rather than a competing one.

## Turning: two selectable models

Settings has a **Footstep / Hold** switch directly under the Turn sensitivity row (Footstep is the
default). The two models are mutually exclusive, not simultaneous -- see below for why.

### Footstep (default)

Turning right by alternately stepping on the **back-right** and **front-left** panels (in either
order) reads as a right turn; alternately stepping **front-right**/**back-left** reads as a left
turn. This is the same "watch a corner cross a threshold, pair it with the opposite corner's next
crossing within **Walk/Dash continuation**" alternation as forward/backward/dash above, just on the
*diagonal* pair of corners instead of the front or back pair -- and unlike forward/backward, the
threshold here is a plain percentage of total board weight on that one corner (**turn threshold
%**, default 50%), not relative to a learned resting reference.

A turn confirms on the **second** step of the pair: a single corner crossing the threshold is never
enough by itself, it just becomes the pending first step waiting for the diagonal partner's next
crossing. Unlike Forward/Backward/Dash (see "Forward/Backward/Dash: the first few steps tap, later
steps hold" above), a confirmed turn step *always* holds for just **stride length (ms)** alone --
it never escalates to a longer, continuously-bridged hold, no matter how many times the diagonal
alternation repeats in a row. Each turn step releases back to Idle on its own after stride length
elapses, and a fresh alternating pair has to confirm again to produce the next one. This was a
deliberate choice: continuous turning felt like it was coasting past where the player actually
wanted to stop, so every turn step reads as one deliberate action instead. (Hold below never had
this kind of continuation to begin with -- it's a sustained lean, not a sequence of steps.)

The 50% figure above is the *baseline* threshold, at what the sliders elsewhere call sensitivity 50
(`GestureSensitivityScale`'s neutral multiplier of 1.0x). **Gesture sensitivity: Turn**'s own
*default* isn't actually that neutral point, though -- real-world testing found it too hard to
trigger, so the underlying value defaults to a raw 60 instead (a 0.9x multiplier, making the
default threshold 45%, not 50%). The Settings slider still shows this default as a plain 50, same
as every other Gesture sensitivity slider, via its own raw/display mapping (`SettingsForm`'s
`TurnRawMin`/`TurnRawMax`) -- moving the slider scales the applied threshold the same way Walk/
Dash's thresholds do (lower percentage is easier to trigger, so the display direction is inverted --
100 is "Sensitive"/easiest, 0 is "Insensitive" and fully disables turning).

### Hold

X (left-right weight) has to swing past a threshold (**±40** at the sensitivity-50 baseline, **±36**
at the actual default -- see above) and *stay* past it continuously for a sustained stretch
(**400ms** baseline, **360ms** at the actual default) before a turn is confirmed -- an ordinary
step's natural left-right sway crosses that instantaneous threshold plenty, but flips side to side
too quickly to ever hold it for the full duration, which is exactly what filters it out. Once
confirmed, a turn only releases once X falls back under a lower bound (**±25**, hysteresis), and a
confirmed turn always wins over stepping while it's active. The ±40/±25/400ms figures scale
together with **Gesture sensitivity: Turn** the same way Footstep's threshold does.

This was the original turn model; Footstep was added after real gait logs showed Hold could
false-trigger the *opposite* turn during Footstep's own large, brief X swings when both ran
simultaneously -- so rather than keep them layered, they became an either/or choice, and Footstep
(the more reliable one against ordinary walking) became the default.

### Both models

**Gesture sensitivity: Turn** at 0 ("Insensitive") turns whichever model is selected off entirely, not
just its output -- there's no separate "turning enabled" toggle; this is the only way to disable
turning, matching how Jump/Crouch sensitivity already work. When disabled, any pending state
(Footstep's first step, or Hold's in-progress/confirmed lean) is dropped immediately, so nothing
stale carries over to when sensitivity is raised again, and switching between Footstep and Hold
likewise drops the other model's state right away. No turn-equivalent output is sent in any output
mode while disabled -- no turn keys, no mouse-look movement, no right-stick deflection, and no OSC
`LookLeft`/`LookRight` messages.

## Jump: rise, then rapid collapse

[`JumpDetector.cs`](../src/WiiFitToVRC.Core/Motion/JumpDetector.cs) tracks a slow-moving baseline
of the calibrated total weight. Firing on the push-off spike alone turned out to be
indistinguishable from a fast crouch (committing weight forward quickly can also spike the total
briefly), so a jump is only confirmed once the spike is *followed by* a rapid collapse toward
near-zero weight (the moment the feet actually leave the board) within half a second. If the spike
never collapses that way, it wasn't a jump, and nothing fires. **Gesture sensitivity: Jump** scales
how large that initial push-off spike must be (relative to the baseline) to arm; the
collapse/settle shape that follows is left fixed, since it describes what a real jump looks like
rather than how hard you have to move. At sensitivity 0 ("Insensitive"), the spike can never arm at all --
there's no separate "Jump enabled" toggle; this is the only way to disable jump.

## Crouch: slow and sustained, not a spike

[`CrouchDetector.cs`](../src/WiiFitToVRC.Core/Motion/CrouchDetector.cs) watches Y for a sustained
forward lean (`Y > 45`), but — since a jump's fast push-off can also cross that instantaneous
level — only confirms crouch once Y has stayed above the threshold *continuously* for 500ms. A
jump's front-loading is a brief spike immediately followed by the airborne weight collapse (see
above), so it can't sustain the hold; a real crouch settles in and stays there. Standing back up
isn't rate-gated — any drop back below the lower threshold (`Y < 30`) ends the crouch immediately.
These figures are likewise the defaults at **Gesture sensitivity: Crouch** = 50; the enter/exit Y
thresholds and the hold duration all scale together with that setting. At sensitivity 0 ("Insensitive"),
crouch can never be entered, and immediately releases if it was already active when the setting
changed -- there's no separate "Crouch enabled" toggle; this is the only way to disable crouch.

## Gesture sensitivity: independent dials for walk/dash/turn/jump/crouch/stride/continuation

Settings has seven Insensitive/Sensitive-or-Narrow/Wide sliders plus one plain step count, grouped under
"Gesture sensitivity": **Walk**, **Dash**, **Turn**, **Jump**, **Crouch**, **Stride**, **Walk/Dash
continuation**, and **Steps until continuation**. The first seven are each shown as a plain 0-100
percentage (Insensitive to Sensitive, except Stride and Walk/Dash continuation which are Narrow to Wide),
independent of the others -- turning up Jump doesn't touch Turn or Crouch. 50 is always the default
*displayed* position for those seven -- for Jump/Crouch/Walk/Dash/Stride/Walk-Dash-continuation
that's also the neutral value underneath, reproducing the original hardcoded thresholds unchanged,
but **Turn** is the one exception: see the Footstep turn model above for why its default is
displayed as 50 while the value actually applied underneath is stronger than neutral.
**Steps until continuation** is a plain 1-15 count instead (default 7), described in "Forward/
Backward/Dash: the first few steps tap, later steps hold" above.

- **Turn/Jump/Crouch** feed directly into
  [`GestureSensitivityScale.cs`](../src/WiiFitToVRC.Core/Motion/GestureSensitivityScale.cs), which
  turns the 0-100 value into a multiplier applied to that gesture's thresholds:
  `1.0 - (sensitivity - 50) * 0.01`. Each point away from 50 is a 1% change, so 100 is 0.5x
  (thresholds/durations 50% smaller, easier to trigger -- "Sensitive") and moving toward 0 makes them
  larger and harder to trigger -- but 0 itself isn't just "1.5x harder", it's a hard cutoff (see
  `GestureSensitivityScale.IsDisabled`): each detector skips its trigger condition entirely at
  sensitivity 0, so the gesture can never fire no matter how extreme the input is.
- **Walk** and **Dash** scale the same way, but in the *opposite* raw-value direction (a *smaller*
  footstep-threshold-% or dash-period-ms is what's easier to trigger), so the settings UI inverts
  the display so 100 ("Sensitive") still means "easier" and 0 ("Insensitive") still means "harder", matching
  the other four. Dash additionally hard-disables at 0 (see the Forward/backward/dash section
  above); Walk does not have a hard-disable, since disabling it would also disable forward/backward
  walking entirely.
- **Stride** scales the stride-length hold duration; it isn't an Insensitive/Sensitive "how easily
  does this fire" dial like the other five (nothing about stride makes a gesture more or less
  likely), so it
  uses Narrow/Wide labels instead and has no disable behavior.
- **Walk/Dash continuation** scales the alternation-pairing window described above (Forward/
  Backward's *and* Turn's) and, for Forward/Backward/Dash only, the streak-reset window -- likewise
  Narrow/Wide with no disable behavior, and independent of Stride even though its raw value is
  added on top of stride length for the long-hold duration.
- **Steps until continuation** sets the streak count that Forward/Backward/Dash switch over at --
  Turn doesn't use it at all, always holding for stride length alone regardless. A plain 1-15 count
  rather than an Insensitive/Sensitive/Narrow/Wide dial, and has no disable behavior either.

None of these eight affect forward/backward, which has its own separate **Footstep threshold %**
raw value (mapped to the "Walk" slider) as described above.

Crouch is also suppressed for 500ms after any forward/backward/turn/jump last fired, since those
can transiently disturb the front-back balance enough to look like the start of a crouch.

### Crouch is a toggle, not a hold

Unlike the movement keys (held down while active), crouch/stand in VRChat is a single **toggle**
binding — one press crouches, the next press stands back up. So instead of holding a key down
while `IsCrouching` is true, the app sends exactly one tap on *each* transition (crouch starting,
and crouch ending). This pairing has to stay exact: if a tap were ever silently dropped, the game
and the app's idea of the crouch state would end up permanently inverted relative to each other.

## Sitting posture: overrides, not new settings

Settings has no separate "sitting" screen — there wasn't room to add one, and someone using the
board seated still wants to tune Gesture sensitivity, Dash input method, and everything else the
same way a standing user does. Instead, the main window's **Play posture** row (立ち/Standing,
座り/Sitting — `AppSettings.PostureMode`, `MonitorForm`'s posture radio pair) reinterprets a
handful of the settings and detectors described above for a body resting on a chair rather than
standing on the board, without adding anything new to the Settings dialog.

### Presence and calibration

Sitting overrides two settings outright (`AppSettings.EffectivePresenceWeightThreshold`/
`EffectiveSleepSeconds`, see the `PostureMode` doc comment) rather than exposing separate sliders
for them:

- **Presence weight threshold**: 500 instead of the configured value — someone seated rests only a
  fraction of their body weight on the board, the chair carries the rest.
- **Sleep/wake seconds**: 0 — a seated session tends to start and stop in short, quick bursts, and
  the normal hysteresis delay just gets in the way.

The weight reference (see above) also works completely differently while sitting: instead of the
normal ~20+ second "stand still" flatness-window wait, `ReferenceWeightCalibrator.
CalibrateImmediately` seeds it straight from whatever reading is on hand the instant presence is
first detected, and the usual ongoing auto-refresh is suspended entirely for as long as sitting
stays on (`DirectionClassifier.Update`'s `instantWeightCalibration` parameter) — a seated resting
weight is light and inconsistent enough that the flat-window process isn't a good fit for it
either way. Switching **Play posture** in either direction resets the reference (and
`JumpDetector`'s own standing baseline) so it re-establishes fresh for the new posture
(`InputController.ResetWeightCalibration`, called from `MonitorForm.SetPostureMode`).

### Forward/backward/dash: a fixed percentage instead of a learned reference

The reference-relative footstep detection described above doesn't hold up seated: with presence
now unlocking at 500 instead of thousands, the reference can seed mid-step, or seed 0 for a corner
someone's seated stance never rests any weight on at all (a 0 reference permanently blocks that
corner — see `CornerPeakTracker`'s doc comment). Sitting forward/backward/dash instead reuses the
exact same front/back corner-pair alternation, dash-period, hold, and continuation logic described
above, but feeds it from a plain percentage-of-total threshold on those corners — the same
mechanism the Footstep turn model already uses for the diagonal pair
(`DirectionClassifier.BaselineSittingFrontBackThresholdPct`, 35% at the baseline, scaled by the
same **Footstep threshold %** ratio Standing's Walk slider controls). This was tuned against real
seated gait recordings (`debug/sit_*.csv`): front/back corner percentages during an actual step
cluster in the low-to-high 40s% at the default 120% ratio, resting well under that between steps.

### Turn: unchanged

Turn already uses that same fixed-percentage-of-total mechanism for its diagonal corner pair (see
above), so it needed no changes at all for sitting — it behaves identically in both postures.

### Crouch: top and bottom panels swapped

A seated crouch gesture is recorded on the **bottom** panels instead of the top ones standing
leans onto (feet pushed forward under the board rather than weight shifted onto the toes).
`InputController.Update` negates Y before calling `CrouchDetector.Update` while sitting — since Y
is top-minus-bottom, negating it is exactly that swap — with every other part of
`CrouchDetector`'s own logic (hold duration, hysteresis, sensitivity scaling) left completely
untouched.

### Jump: near-zero weight instead of a push-off spike

A seated push-off is far too subtle and inconsistent to arm off the way Standing's spike-then-
collapse state machine does (see above). What's reliable instead is the feet actually lifting
clear of the board, which reads as total weight collapsing toward zero relative to the reference
established above (`JumpDetector`'s `sittingPosture` path). **Gesture sensitivity: Jump** still
drives this off the same slider as standing, but maps it to a different underlying range: 0-40% of
the reference weight instead of a spike multiplier (`JumpDetector.SittingMaxThresholdFraction`),
so the default 50 arms at 20% — comfortably above the near-zero readings a real lifted-leg moment
produces, and comfortably below ordinary seated weight-bearing.

Because this near-zero moment is exactly what Sitting's zero-second sleep/wake override would
otherwise read as "no longer present" and gate out, `InputController.Update` runs sitting jump
detection as a fully self-contained step *ahead of* the presence gate, rather than after it like
every other detector.
