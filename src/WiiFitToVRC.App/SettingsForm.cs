using System.Linq;
using WiiFitToVRC.Core.Input;
using WiiFitToVRC.Core.Localization;
using WiiFitToVRC.Core.Settings;

namespace WiiFitToVRC.App;

public sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly AppLanguage _uiLanguage;
    private readonly InputController _inputController;

    // Column where every "value" control (radio/combo/slider/checkbox) starts -- wide enough that
    // the longest Japanese label in this tab ("足踏み検知のしきい値" etc.) never runs into it.
    private const int ValueColumnX = 230;

    // Raw-unit ranges backing the walk/dash/stride 0-100 sliders (see RawToDisplay*/DisplayToRaw*
    // below). Walk and dash are inverted: a *lower* raw value (less weight needed, shorter period)
    // is easier to trigger, so raw-min maps to display 100 ("strong") and raw-max to display 0
    // ("weak") -- matching the direction turn/jump/crouch already use. Stride is not inverted: a
    // longer hold is "wide", so raw-min maps to display 0 and raw-max to display 100.
    private const int WalkRawMin = 100, WalkRawMax = 140;
    private const int DashRawMin = 200, DashRawMax = 400;
    private const int StrideRawMin = 30, StrideRawMax = 110;
    private const int StepContinuationRawMin = 400, StepContinuationRawMax = 1400;
    private const int TurnHoldRawMin = 500, TurnHoldRawMax = 1500;

    // Turn is fed directly into GestureSensitivityScale like Jump/Crouch, but its raw default
    // (AppSettings.TurnSensitivity = 60) isn't the neutral 50 -- real-world testing found the
    // neutral threshold too hard to trigger. Not inverted (higher raw = higher display = "strong",
    // same direction as the raw value itself), just shifted +10 from a plain 0-100 range so that
    // raw 60 lands at display 50, keeping every other slider's "50 is the default" convention.
    private const int TurnRawMin = 10, TurnRawMax = 110;

    // Walk/Dash/Turn/Jump/Crouch are the five sliders where 0 is a hard disable (see
    // GestureSensitivityScale.IsDisabled) rather than just the weakest end of a continuous range --
    // showing a plain "0" next to them reads as "still a little responsive", so the value label
    // spells out "OFF" there instead. Stride/Walk-Dash-continuation/Steps-until-continuation have
    // no disable behavior at all and keep showing their plain numeric value, so this is only ever
    // called for those five.
    private static string FormatSensitivityValue(int display) => display <= 0 ? "OFF" : display.ToString();

    private static int RawToDisplayInverted(int raw, int rawMin, int rawMax) =>
        (int)Math.Round((rawMax - raw) * 100.0 / (rawMax - rawMin));

    private static int DisplayToRawInverted(int display, int rawMin, int rawMax) =>
        rawMax - (int)Math.Round(display * (rawMax - rawMin) / 100.0);

    private static int RawToDisplay(int raw, int rawMin, int rawMax) =>
        (int)Math.Round((raw - rawMin) * 100.0 / (rawMax - rawMin));

    private static int DisplayToRaw(int display, int rawMin, int rawMax) =>
        rawMin + (int)Math.Round(display * (rawMax - rawMin) / 100.0);

    // Dash sensitivity 0 ("Insensitive") is a hard cutoff, not just the far end of the 200-400ms range --
    // DirectionClassifier treats DashPeriodMs = 0 as an unreachable interval floor, so dash can
    // never fire, matching turn/jump/crouch's sensitivity-0-fully-disables behavior. So 0 maps to
    // the raw sentinel 0 instead of DashRawMax, both ways.
    private static int DashDisplayToRaw(int display) =>
        display <= 0 ? 0 : DisplayToRawInverted(display, DashRawMin, DashRawMax);

    private static int DashRawToDisplay(int raw) =>
        raw <= 0 ? 0 : Math.Clamp(RawToDisplayInverted(Math.Clamp(raw, DashRawMin, DashRawMax), DashRawMin, DashRawMax), 0, 100);

    // Turn sensitivity 0 ("Insensitive") is the same hard disable cutoff as Dash -- see
    // GestureSensitivityScale.IsDisabled -- so 0 maps to the raw sentinel 0 instead of TurnRawMin,
    // both ways, exactly like DashDisplayToRaw/DashRawToDisplay above.
    private static int TurnDisplayToRaw(int display) =>
        display <= 0 ? 0 : DisplayToRaw(display, TurnRawMin, TurnRawMax);

    private static int TurnRawToDisplay(int raw) =>
        raw <= 0 ? 0 : Math.Clamp(RawToDisplay(Math.Clamp(raw, TurnRawMin, TurnRawMax), TurnRawMin, TurnRawMax), 0, 100);

    // Fixed height -- the General tab has grown taller than comfortably fits on screen, so it
    // scrolls internally (see AutoScroll below) instead of the window growing without bound.
    private readonly TabControl _tabs = new() { Location = new Point(10, 10), Size = new Size(560, 480) };
    private readonly TabPage _generalTab = new();
    private readonly TabPage _keybindsTab = new();
    private readonly TabPage _controllerTab = new();

    // Keyboard = turn via Q/E, KeyboardMouse = turn via mouse-look, Osc = VRChat's own OSC input
    // endpoint (for VR setups that lock out SendInput entirely), Controller = virtual gamepad.
    private readonly RadioButton _outputKeyboardRadio = new() { Location = new Point(ValueColumnX, 8), AutoSize = true };
    private readonly RadioButton _outputKeyboardMouseRadio = new() { Location = new Point(ValueColumnX, 31), AutoSize = true };
    private readonly RadioButton _outputOscRadio = new() { Location = new Point(ValueColumnX, 54), AutoSize = true };
    private readonly RadioButton _outputControllerRadio = new() { Location = new Point(ValueColumnX, 77), AutoSize = true };

    // Wide enough for the longest entry, e.g. "简体中文 (Chinese Simplified)".
    private readonly ComboBox _languageCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(ValueColumnX, 109), Size = new Size(250, 24) };

    // Walk/dash/turn/jump/crouch/stride sensitivity, each independently adjustable (0-100, shown
    // as a plain 0-100 percentage; the underlying raw units -- e.g. footstep threshold %, dash
    // period ms -- are converted to/from that display scale, see DisplayToRaw/RawToDisplay below).
    // Flanked by qualitative labels, plus the raw 0-100 value after the "strong"/"wide" end so it's
    // clear where the slider currently sits. Walk and dash are inverted relative to their raw units
    // (a lower raw threshold/period is *easier* to trigger, i.e. "strong"), so the display direction
    // matches turn/jump/crouch/stride, where higher display = easier to trigger.
    private readonly TrackBar _walkSensitivitySlider = new() { Minimum = 0, Maximum = 100, Location = new Point(ValueColumnX + 55, 165), Size = new Size(140, 40), TickFrequency = 10 };
    private readonly Label _walkSensitivityWeakLabel = new() { Location = new Point(ValueColumnX, 173), AutoSize = true };
    private readonly Label _walkSensitivityStrongLabel = new() { Location = new Point(ValueColumnX + 201, 173), AutoSize = true };
    private readonly Label _walkSensitivityValueLabel = new() { Location = new Point(ValueColumnX + 255, 173), AutoSize = true };

    private readonly TrackBar _dashSensitivitySlider = new() { Minimum = 0, Maximum = 100, Location = new Point(ValueColumnX + 55, 205), Size = new Size(140, 40), TickFrequency = 10 };
    private readonly Label _dashSensitivityWeakLabel = new() { Location = new Point(ValueColumnX, 213), AutoSize = true };
    private readonly Label _dashSensitivityStrongLabel = new() { Location = new Point(ValueColumnX + 201, 213), AutoSize = true };
    private readonly Label _dashSensitivityValueLabel = new() { Location = new Point(ValueColumnX + 255, 213), AutoSize = true };

    private readonly TrackBar _turnSensitivitySlider = new() { Minimum = 0, Maximum = 100, Location = new Point(ValueColumnX + 55, 245), Size = new Size(140, 40), TickFrequency = 10 };
    private readonly Label _turnSensitivityWeakLabel = new() { Location = new Point(ValueColumnX, 253), AutoSize = true };
    private readonly Label _turnSensitivityStrongLabel = new() { Location = new Point(ValueColumnX + 201, 253), AutoSize = true };
    private readonly Label _turnSensitivityValueLabel = new() { Location = new Point(ValueColumnX + 255, 253), AutoSize = true };

    // Isolated in their own Panel, not added straight to the tab like everything else here --
    // WinForms auto-groups same-parent RadioButtons into one mutually-exclusive set, and without
    // this they'd fight with the output-mode radios above for which one is checked.
    private readonly Panel _turnModePanel = new() { Location = new Point(ValueColumnX, 294), Size = new Size(340, 24), BorderStyle = BorderStyle.None };
    private readonly RadioButton _turnModeHoldRadio = new() { Location = new Point(0, 0), AutoSize = true };
    private readonly RadioButton _turnModeFootstepRadio = new() { Location = new Point(110, 0), AutoSize = true };

    private readonly TrackBar _jumpSensitivitySlider = new() { Minimum = 0, Maximum = 100, Location = new Point(ValueColumnX + 55, 325), Size = new Size(140, 40), TickFrequency = 10 };
    private readonly Label _jumpSensitivityWeakLabel = new() { Location = new Point(ValueColumnX, 333), AutoSize = true };
    private readonly Label _jumpSensitivityStrongLabel = new() { Location = new Point(ValueColumnX + 201, 333), AutoSize = true };
    private readonly Label _jumpSensitivityValueLabel = new() { Location = new Point(ValueColumnX + 255, 333), AutoSize = true };

    private readonly TrackBar _crouchSensitivitySlider = new() { Minimum = 0, Maximum = 100, Location = new Point(ValueColumnX + 55, 365), Size = new Size(140, 40), TickFrequency = 10 };
    private readonly Label _crouchSensitivityWeakLabel = new() { Location = new Point(ValueColumnX, 373), AutoSize = true };
    private readonly Label _crouchSensitivityStrongLabel = new() { Location = new Point(ValueColumnX + 201, 373), AutoSize = true };
    private readonly Label _crouchSensitivityValueLabel = new() { Location = new Point(ValueColumnX + 255, 373), AutoSize = true };

    private readonly TrackBar _strideSlider = new() { Minimum = 0, Maximum = 100, Location = new Point(ValueColumnX + 55, 405), Size = new Size(140, 40), TickFrequency = 10 };
    private readonly Label _strideNarrowLabel = new() { Location = new Point(ValueColumnX, 413), AutoSize = true };
    private readonly Label _strideWideLabel = new() { Location = new Point(ValueColumnX + 201, 413), AutoSize = true };
    private readonly Label _strideValueLabel = new() { Location = new Point(ValueColumnX + 255, 413), AutoSize = true };

    private readonly TrackBar _stepContinuationSlider = new() { Minimum = 0, Maximum = 100, Location = new Point(ValueColumnX + 55, 445), Size = new Size(140, 40), TickFrequency = 10 };
    private readonly Label _stepContinuationNarrowLabel = new() { Location = new Point(ValueColumnX, 453), AutoSize = true };
    private readonly Label _stepContinuationWideLabel = new() { Location = new Point(ValueColumnX + 201, 453), AutoSize = true };
    private readonly Label _stepContinuationValueLabel = new() { Location = new Point(ValueColumnX + 255, 453), AutoSize = true };

    // A plain step count (1-15), not an Insensitive/Sensitive or Narrow/Wide dial -- no flanking qualitative
    // labels, just the slider and its raw value, same layout as the mouse-stroke/presence sliders
    // below.
    private readonly TrackBar _continuationStepCountSlider = new() { Minimum = 1, Maximum = 15, Location = new Point(ValueColumnX, 485), Size = new Size(140, 40), TickFrequency = 1 };
    private readonly Label _continuationStepCountValueLabel = new() { Location = new Point(ValueColumnX + 150, 493), AutoSize = true };

    // Same isolated-Panel trick as _turnModePanel -- keeps this radio pair from joining the
    // output-mode / turn-mode mutually-exclusive groups.
    private readonly Panel _dashInputModePanel = new() { Location = new Point(ValueColumnX, 545), Size = new Size(340, 24), BorderStyle = BorderStyle.None };
    private readonly RadioButton _dashInputModeComboKeyRadio = new() { Location = new Point(0, 0), AutoSize = true };
    private readonly RadioButton _dashInputModeDoubleTapRadio = new() { Location = new Point(110, 0), AutoSize = true };

    // Turn speed's absolute value/range differs per output mode (mouse pixels-per-tick, controller
    // 0-100%), and Keyboard/Osc have no speed concept at all -- Keyboard turns via a discrete Q/E
    // key press, and Osc turns via the plain LookLeft/LookRight buttons (see OscSender), which have
    // no magnitude either, just held or not (its *duration* is still configurable, see
    // _turnHoldSlider below). One physical slider is reused across the two speed-having modes --
    // its Minimum/Maximum/Value get reassigned by UpdateTurnSpeedControlForMode whenever the output
    // mode radio changes, swapping in that mode's own staged value (see _mouseTurnSpeedValue /
    // _controllerTurnSpeedValue) -- and it's hidden entirely (in favor of _turnSpeedNoSettingLabel)
    // for Keyboard/Osc. Left/right used to be independently tunable per mode; now one shared value
    // drives both directions, matching how the other gesture sliders work.
    private readonly TrackBar _turnSpeedSlider = new() { Minimum = 1, Maximum = 50, Location = new Point(ValueColumnX, 585), Size = new Size(180, 40), TickFrequency = 5 };
    private readonly Label _turnSpeedValueLabel = new() { Location = new Point(ValueColumnX + 190, 593), AutoSize = true };
    private readonly Label _turnSpeedNoSettingLabel = new() { Location = new Point(ValueColumnX, 593), AutoSize = true };
    private int _mouseTurnSpeedValue;
    private int _controllerTurnSpeedValue;

    // How long (ms) a single confirmed turn step's output is independently held for -- only meaningful
    // for Osc/Controller (see InputController.ResolveHeldTurnDirection); Keyboard/KeyboardMouse turn
    // via a discrete Q/E press or a one-shot mouse-look delta, neither of which needs this, so the
    // whole row (name label included, hence it being a field rather than a BuildGeneralTab-local
    // like most other row labels) is hidden entirely for those two modes -- see
    // UpdateTurnHoldRowVisibility.
    private readonly Label _turnHoldLabel = new() { Location = new Point(10, 633), AutoSize = true };
    private readonly TrackBar _turnHoldSlider = new() { Minimum = 0, Maximum = 100, Location = new Point(ValueColumnX + 55, 625), Size = new Size(140, 40), TickFrequency = 10 };
    private readonly Label _turnHoldNarrowLabel = new() { Location = new Point(ValueColumnX, 633), AutoSize = true };
    private readonly Label _turnHoldWideLabel = new() { Location = new Point(ValueColumnX + 201, 633), AutoSize = true };
    private readonly Label _turnHoldValueLabel = new() { Location = new Point(ValueColumnX + 255, 633), AutoSize = true };

    // 1000-10000 in steps of 100 -- a plain TrackBar steps by 1 per dragged unit, so the control
    // itself covers 10-100 (hundreds of weight) and the real value is *100.
    private readonly TrackBar _presenceSlider = new() { Minimum = 10, Maximum = 100, Location = new Point(ValueColumnX, 665), Size = new Size(180, 40), TickFrequency = 10 };
    private readonly Label _presenceValueLabel = new() { Location = new Point(ValueColumnX + 190, 673), AutoSize = true };

    private readonly NumericUpDown _sleepSecondsInput = new() { Minimum = 1, Maximum = 30, Location = new Point(ValueColumnX, 709), Size = new Size(70, 24) };

    // Explains what the checkbox below is for -- the setting name alone ("強制補正") doesn't
    // convey when someone would actually want to turn it on, so this spells it out right above it.
    private readonly Label _forcedControllerCorrectionHintLabel = new() { Location = new Point(10, 743), AutoSize = true, MaximumSize = new Size(520, 0) };
    private readonly CheckBox _forcedControllerCorrectionCheck = new() { Location = new Point(ValueColumnX, 765), AutoSize = true };

    // Jump/crouch/turn all disable via their own sensitivity slider's "Insensitive" (0) end now -- see
    // GestureSensitivityScale.IsDisabled -- so none of the three need a separate enabled checkbox
    // any more.
    private readonly CheckBox _debugModeCheck = new() { Location = new Point(ValueColumnX, 799), AutoSize = true };

    private readonly TextBox _debugFolderInput = new() { Location = new Point(ValueColumnX, 823), Size = new Size(180, 24) };
    private readonly Button _debugFolderBrowseButton = new() { Location = new Point(ValueColumnX + 186, 822), Size = new Size(34, 24) };

    private readonly ComboBox _forwardKeyCombo = MakeCombo<VirtualKey>();
    private readonly ComboBox _dashKeyCombo = MakeCombo<VirtualKey>();
    private readonly ComboBox _dashModifierKeyCombo = MakeCombo<VirtualKey>();
    private readonly ComboBox _backwardKeyCombo = MakeCombo<VirtualKey>();
    private readonly ComboBox _turnRightKeyCombo = MakeCombo<VirtualKey>();
    private readonly ComboBox _turnLeftKeyCombo = MakeCombo<VirtualKey>();
    private readonly ComboBox _jumpKeyCombo = MakeCombo<VirtualKey>();
    private readonly ComboBox _crouchKeyCombo = MakeCombo<VirtualKey>();

    // Turn speed for this mode now lives on the General tab's shared, mode-swapping slider (see
    // _turnSpeedSlider) instead of its own pair of sliders here.
    private readonly Label _controllerStatusLabel = new() { Location = new Point(10, 10), AutoSize = true, MaximumSize = new Size(380, 0) };
    private readonly ComboBox _jumpButtonCombo = MakeCombo<ControllerButton>();
    private readonly ComboBox _crouchButtonCombo = MakeCombo<ControllerButton>();
    private readonly ComboBox _dashButtonCombo = MakeCombo<ControllerButton>();

    private readonly Button _resetButton = new() { Location = new Point(260, 500), AutoSize = true };
    private readonly Button _saveButton = new() { Location = new Point(380, 500), AutoSize = true };
    private readonly Button _cancelButton = new() { Location = new Point(470, 500), AutoSize = true };

    public bool SettingsChanged { get; private set; }

    public SettingsForm(AppSettings settings, AppLanguage uiLanguage, InputController inputController)
    {
        _settings = settings;
        _uiLanguage = uiLanguage;
        _inputController = inputController;

        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(580, 540);
        Text = Localizer.Get("Button_Settings", _uiLanguage);

        BuildLayout();
        LoadFromSettings(_settings);

        // WinForms auto-scrolls a scrollable container to keep whatever control ends up with
        // initial focus in view -- with this many controls stacked in the General tab, that could
        // land anywhere, so force it back to the top once the dialog has actually been shown.
        Shown += (_, _) => _generalTab.AutoScrollPosition = new Point(0, 0);

        // Updates the currently-active mode's staged value too -- by the time this fires, the
        // Value has already been reassigned by either the user dragging the slider (active mode
        // unchanged) or UpdateTurnSpeedControlForMode switching modes (writes the new mode's own
        // value right back to itself, a harmless no-op).
        _turnSpeedSlider.ValueChanged += (_, _) =>
        {
            _turnSpeedValueLabel.Text = _turnSpeedSlider.Value.ToString();
            if (_outputKeyboardMouseRadio.Checked)
            {
                _mouseTurnSpeedValue = _turnSpeedSlider.Value;
            }
            else if (_outputControllerRadio.Checked)
            {
                _controllerTurnSpeedValue = _turnSpeedSlider.Value;
            }
        };
        _outputKeyboardRadio.CheckedChanged += (_, _) => { UpdateTurnSpeedControlForMode(); UpdateTurnHoldRowVisibility(); };
        _outputKeyboardMouseRadio.CheckedChanged += (_, _) => { UpdateTurnSpeedControlForMode(); UpdateTurnHoldRowVisibility(); };
        _outputOscRadio.CheckedChanged += (_, _) => { UpdateTurnSpeedControlForMode(); UpdateTurnHoldRowVisibility(); };
        _outputControllerRadio.CheckedChanged += (_, _) => { UpdateTurnSpeedControlForMode(); UpdateTurnHoldRowVisibility(); };
        _turnHoldSlider.ValueChanged += (_, _) => _turnHoldValueLabel.Text = _turnHoldSlider.Value.ToString();
        _presenceSlider.ValueChanged += (_, _) => _presenceValueLabel.Text = (_presenceSlider.Value * 100).ToString();
        _walkSensitivitySlider.ValueChanged += (_, _) => _walkSensitivityValueLabel.Text = FormatSensitivityValue(_walkSensitivitySlider.Value);
        _dashSensitivitySlider.ValueChanged += (_, _) => _dashSensitivityValueLabel.Text = FormatSensitivityValue(_dashSensitivitySlider.Value);
        _turnSensitivitySlider.ValueChanged += (_, _) => _turnSensitivityValueLabel.Text = FormatSensitivityValue(_turnSensitivitySlider.Value);
        _jumpSensitivitySlider.ValueChanged += (_, _) => _jumpSensitivityValueLabel.Text = FormatSensitivityValue(_jumpSensitivitySlider.Value);
        _crouchSensitivitySlider.ValueChanged += (_, _) => _crouchSensitivityValueLabel.Text = FormatSensitivityValue(_crouchSensitivitySlider.Value);
        _strideSlider.ValueChanged += (_, _) => _strideValueLabel.Text = _strideSlider.Value.ToString();
        _stepContinuationSlider.ValueChanged += (_, _) => _stepContinuationValueLabel.Text = _stepContinuationSlider.Value.ToString();
        _continuationStepCountSlider.ValueChanged += (_, _) => _continuationStepCountValueLabel.Text = _continuationStepCountSlider.Value.ToString();
        // The Dash input mode radio labels spell out the actual bound keys ("W + Shift", "W
        // double-tap"), so they need to stay in sync with the Keybinds tab's combo boxes live,
        // not just at dialog-open time.
        _forwardKeyCombo.SelectedIndexChanged += (_, _) => UpdateDashInputModeLabels();
        _dashKeyCombo.SelectedIndexChanged += (_, _) => UpdateDashInputModeLabels();
        _dashModifierKeyCombo.SelectedIndexChanged += (_, _) => UpdateDashInputModeLabels();
        _saveButton.Click += (_, _) => Save();
        _cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        // Loads the hardcoded AppSettings defaults into the form fields only -- Cancel still
        // discards them, Save is still required to actually persist a reset.
        _resetButton.Click += (_, _) => LoadFromSettings(new AppSettings());
        _debugFolderBrowseButton.Click += (_, _) => BrowseDebugFolder();
    }

    // Keyboard mode has no turn-speed concept (Q/E is a discrete key press), and neither does Osc
    // (LookLeft/LookRight are plain buttons, no magnitude -- see OscSender) -- hide the slider and
    // show "No setting" for both. KeyboardMouse and Controller each swap in their own staged value
    // and range: mouse pixels-per-tick (1-50), controller stick deflection 10-100%.
    private void UpdateTurnSpeedControlForMode()
    {
        if (_outputKeyboardRadio.Checked || _outputOscRadio.Checked)
        {
            _turnSpeedSlider.Visible = false;
            _turnSpeedValueLabel.Visible = false;
            _turnSpeedNoSettingLabel.Visible = true;
            return;
        }

        _turnSpeedNoSettingLabel.Visible = false;
        _turnSpeedSlider.Visible = true;
        _turnSpeedValueLabel.Visible = true;

        if (_outputControllerRadio.Checked)
        {
            _turnSpeedSlider.Minimum = 10;
            _turnSpeedSlider.Maximum = 100;
            _turnSpeedSlider.Value = Math.Clamp(_controllerTurnSpeedValue, 10, 100);
        }
        else
        {
            _turnSpeedSlider.Minimum = 1;
            _turnSpeedSlider.Maximum = 50;
            _turnSpeedSlider.Value = Math.Clamp(_mouseTurnSpeedValue, 1, 50);
        }
        _turnSpeedValueLabel.Text = _turnSpeedSlider.Value.ToString();
    }

    // Turn hold length is only meaningful for Osc/Controller -- Keyboard/KeyboardMouse turn via a
    // discrete Q/E press or a one-shot mouse-look delta, neither of which uses it -- so the whole
    // row (including its own name label) is hidden entirely rather than showing a "No setting"
    // placeholder like _turnSpeedNoSettingLabel does; there's nothing to point at for those modes.
    private void UpdateTurnHoldRowVisibility()
    {
        bool visible = _outputOscRadio.Checked || _outputControllerRadio.Checked;
        _turnHoldLabel.Visible = visible;
        _turnHoldSlider.Visible = visible;
        _turnHoldNarrowLabel.Visible = visible;
        _turnHoldWideLabel.Visible = visible;
        _turnHoldValueLabel.Visible = visible;
    }

    // The two Dash input mode radios spell out the actual keys involved rather than a generic
    // "Combo key"/"Double-tap key" label -- e.g. "W + Shift" and "W double-tap" with the defaults.
    // ComboKey needs no separate word at all (the "+" already reads as "these combine"); DoubleTap
    // keeps its descriptor as a format-string suffix (Settings_DashInputMode_DoubleTap). Key names
    // vary a lot in length (e.g. "Backspace" vs "W"), so the second radio is repositioned relative
    // to the first radio's actual rendered width every time, instead of a fixed offset -- otherwise
    // a long key name would run the two labels into each other.
    private void UpdateDashInputModeLabels()
    {
        var dashKey = (VirtualKey)_dashKeyCombo.SelectedItem!;
        var dashModifierKey = (VirtualKey)_dashModifierKeyCombo.SelectedItem!;
        var forwardKey = (VirtualKey)_forwardKeyCombo.SelectedItem!;

        _dashInputModeComboKeyRadio.Text = $"{dashKey} + {dashModifierKey}";
        _dashInputModeDoubleTapRadio.Text = Localizer.GetFormatted("Settings_DashInputMode_DoubleTap", _uiLanguage, forwardKey);
        _dashInputModeDoubleTapRadio.Location = new Point(_dashInputModeComboKeyRadio.Right + 20, 0);
    }

    private void BrowseDebugFolder()
    {
        string current = _debugFolderInput.Text;
        string startPath = Path.IsPathRooted(current) ? current : Path.Combine(AppContext.BaseDirectory, current);

        using var dialog = new FolderBrowserDialog
        {
            SelectedPath = Directory.Exists(startPath) ? startPath : AppContext.BaseDirectory,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _debugFolderInput.Text = dialog.SelectedPath;
        }
    }

    // Items.AddRange, not DataSource -- a data-bound ComboBox auto-selects its first item as soon
    // as it's bound, and plain "SelectedItem = x" assignment afterward was silently failing to
    // override that. The unbound Items list doesn't have that behavior.
    private static ComboBox MakeCombo<TEnum>() where TEnum : struct, Enum
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(200, 0), // row Y set per-control where used
            Size = new Size(140, 24),
        };
        combo.Items.AddRange(Enum.GetValues<TEnum>().Cast<object>().ToArray());
        return combo;
    }

    private void BuildLayout()
    {
        _tabs.TabPages.AddRange([_generalTab, _keybindsTab, _controllerTab]);
        Controls.Add(_tabs);

        BuildGeneralTab();
        BuildKeybindsTab();
        BuildControllerTab();

        _resetButton.Text = Localizer.Get("Settings_ResetToDefaults", _uiLanguage);
        _saveButton.Text = Localizer.Get("Settings_Save", _uiLanguage);
        _cancelButton.Text = Localizer.Get("Settings_Cancel", _uiLanguage);
        Controls.AddRange([_resetButton, _saveButton, _cancelButton]);
    }

    private void BuildGeneralTab()
    {
        _generalTab.Text = Localizer.Get("Settings_Tab_General", _uiLanguage);

        var outputModeLabel = new Label { Text = Localizer.Get("Settings_OutputMode", _uiLanguage), Location = new Point(10, 12), AutoSize = true };
        var languageLabel = new Label { Text = Localizer.Get("Settings_Language", _uiLanguage), Location = new Point(10, 113), AutoSize = true };
        var gestureSensitivityGroupLabel = new Label { Text = Localizer.Get("Settings_GestureSensitivity_Group", _uiLanguage), Location = new Point(10, 141), AutoSize = true };
        // Two half-width spaces before each sub-item's label, so it visually reads as indented
        // under the group heading above it rather than a plain top-level row.
        var walkSensitivityLabel = new Label { Text = "  " + Localizer.Get("Settings_GestureSensitivity_Walk", _uiLanguage), Location = new Point(10, 173), AutoSize = true };
        var dashSensitivityLabel = new Label { Text = "  " + Localizer.Get("Settings_GestureSensitivity_Dash", _uiLanguage), Location = new Point(10, 213), AutoSize = true };
        var turnSensitivityLabel = new Label { Text = "  " + Localizer.Get("Settings_GestureSensitivity_Turn", _uiLanguage), Location = new Point(10, 253), AutoSize = true };
        var jumpSensitivityLabel = new Label { Text = "  " + Localizer.Get("Settings_GestureSensitivity_Jump", _uiLanguage), Location = new Point(10, 333), AutoSize = true };
        var crouchSensitivityLabel = new Label { Text = "  " + Localizer.Get("Settings_GestureSensitivity_Crouch", _uiLanguage), Location = new Point(10, 373), AutoSize = true };
        var strideLabel = new Label { Text = "  " + Localizer.Get("Settings_GestureSensitivity_Stride", _uiLanguage), Location = new Point(10, 413), AutoSize = true };
        var stepContinuationLabel = new Label { Text = "  " + Localizer.Get("Settings_GestureSensitivity_StepContinuation", _uiLanguage), Location = new Point(10, 453), AutoSize = true };
        var continuationStepCountLabel = new Label { Text = "  " + Localizer.Get("Settings_GestureSensitivity_ContinuationStepCount", _uiLanguage), Location = new Point(10, 493), AutoSize = true };
        var dashInputModeLabel = new Label { Text = Localizer.Get("Settings_DashInputMode", _uiLanguage), Location = new Point(10, 553), AutoSize = true };
        var turnSpeedLabel = new Label { Text = Localizer.Get("Settings_TurnSpeed", _uiLanguage), Location = new Point(10, 593), AutoSize = true };
        var presenceLabel = new Label { Text = Localizer.Get("Settings_PresenceThreshold", _uiLanguage), Location = new Point(10, 673), AutoSize = true };
        var sleepLabel = new Label { Text = Localizer.Get("Settings_SleepSeconds", _uiLanguage), Location = new Point(10, 711), AutoSize = true };
        var debugFolderLabel = new Label { Text = Localizer.Get("Settings_DebugFolder", _uiLanguage), Location = new Point(10, 827), AutoSize = true };

        _outputKeyboardRadio.Text = Localizer.Get("Settings_OutputMode_Keyboard", _uiLanguage);
        _outputKeyboardMouseRadio.Text = Localizer.Get("Settings_OutputMode_KeyboardMouse", _uiLanguage);
        _outputControllerRadio.Text = Localizer.Get("Settings_OutputMode_Controller", _uiLanguage);
        _outputOscRadio.Text = Localizer.Get("Settings_OutputMode_Osc", _uiLanguage);
        _turnSpeedNoSettingLabel.Text = Localizer.Get("Settings_NoSetting", _uiLanguage);
        _turnHoldLabel.Text = Localizer.Get("Settings_TurnHold", _uiLanguage);
        _turnModeHoldRadio.Text = Localizer.Get("Settings_TurnMode_Hold", _uiLanguage);
        _turnModeFootstepRadio.Text = Localizer.Get("Settings_TurnMode_Footstep", _uiLanguage);
        _forcedControllerCorrectionHintLabel.Text = Localizer.Get("Settings_ForcedControllerCorrectionHint", _uiLanguage);
        _forcedControllerCorrectionCheck.Text = Localizer.Get("Settings_ForcedControllerCorrection", _uiLanguage);
        _debugModeCheck.Text = Localizer.Get("Settings_DebugMode", _uiLanguage);
        // Plain "..." rather than a localized "Browse" label -- a universal enough convention to
        // fit the button's fixed, deliberately compact width in every language.
        _debugFolderBrowseButton.Text = "...";

        string insensitive = Localizer.Get("Settings_GestureSensitivity_Insensitive", _uiLanguage);
        string sensitive = Localizer.Get("Settings_GestureSensitivity_Sensitive", _uiLanguage);
        _walkSensitivityWeakLabel.Text = insensitive;
        _walkSensitivityStrongLabel.Text = sensitive;
        _dashSensitivityWeakLabel.Text = insensitive;
        _dashSensitivityStrongLabel.Text = sensitive;
        _turnSensitivityWeakLabel.Text = insensitive;
        _turnSensitivityStrongLabel.Text = sensitive;
        _jumpSensitivityWeakLabel.Text = insensitive;
        _jumpSensitivityStrongLabel.Text = sensitive;
        _crouchSensitivityWeakLabel.Text = insensitive;
        _crouchSensitivityStrongLabel.Text = sensitive;
        _strideNarrowLabel.Text = Localizer.Get("Settings_GestureSensitivity_Narrow", _uiLanguage);
        _strideWideLabel.Text = Localizer.Get("Settings_GestureSensitivity_Wide", _uiLanguage);
        _stepContinuationNarrowLabel.Text = Localizer.Get("Settings_GestureSensitivity_Narrow", _uiLanguage);
        _stepContinuationWideLabel.Text = Localizer.Get("Settings_GestureSensitivity_Wide", _uiLanguage);
        _turnHoldNarrowLabel.Text = Localizer.Get("Settings_GestureSensitivity_Narrow", _uiLanguage);
        _turnHoldWideLabel.Text = Localizer.Get("Settings_GestureSensitivity_Wide", _uiLanguage);

        foreach (var (language, nativeName) in Localizer.SelectableLanguages)
        {
            string display = language == AppLanguage.Auto ? Localizer.Get("Settings_LanguageAuto", _uiLanguage) : nativeName;
            _languageCombo.Items.Add(new LanguageItem(language, display));
        }

        _turnModePanel.Controls.AddRange([_turnModeHoldRadio, _turnModeFootstepRadio]);
        _dashInputModePanel.Controls.AddRange([_dashInputModeComboKeyRadio, _dashInputModeDoubleTapRadio]);

        _generalTab.Controls.AddRange([
            outputModeLabel, _outputKeyboardRadio, _outputKeyboardMouseRadio, _outputOscRadio, _outputControllerRadio,
            languageLabel, _languageCombo,
            gestureSensitivityGroupLabel,
            walkSensitivityLabel, _walkSensitivityWeakLabel, _walkSensitivitySlider, _walkSensitivityStrongLabel, _walkSensitivityValueLabel,
            dashSensitivityLabel, _dashSensitivityWeakLabel, _dashSensitivitySlider, _dashSensitivityStrongLabel, _dashSensitivityValueLabel,
            turnSensitivityLabel, _turnSensitivityWeakLabel, _turnSensitivitySlider, _turnSensitivityStrongLabel, _turnSensitivityValueLabel,
            _turnModePanel,
            jumpSensitivityLabel, _jumpSensitivityWeakLabel, _jumpSensitivitySlider, _jumpSensitivityStrongLabel, _jumpSensitivityValueLabel,
            crouchSensitivityLabel, _crouchSensitivityWeakLabel, _crouchSensitivitySlider, _crouchSensitivityStrongLabel, _crouchSensitivityValueLabel,
            strideLabel, _strideNarrowLabel, _strideSlider, _strideWideLabel, _strideValueLabel,
            stepContinuationLabel, _stepContinuationNarrowLabel, _stepContinuationSlider, _stepContinuationWideLabel, _stepContinuationValueLabel,
            continuationStepCountLabel, _continuationStepCountSlider, _continuationStepCountValueLabel,
            dashInputModeLabel, _dashInputModePanel,
            turnSpeedLabel, _turnSpeedSlider, _turnSpeedValueLabel, _turnSpeedNoSettingLabel,
            _turnHoldLabel, _turnHoldSlider, _turnHoldNarrowLabel, _turnHoldWideLabel, _turnHoldValueLabel,
            presenceLabel, _presenceSlider, _presenceValueLabel,
            sleepLabel, _sleepSecondsInput,
            _forcedControllerCorrectionHintLabel, _forcedControllerCorrectionCheck,
            _debugModeCheck,
            debugFolderLabel, _debugFolderInput, _debugFolderBrowseButton,
        ]);

        // The tab's content now extends well past its fixed visible height -- scroll internally
        // (a vertical scrollbar appears automatically) rather than growing the window without bound.
        _generalTab.AutoScroll = true;
        _generalTab.AutoScrollMinSize = new Size(0, 904);
    }

    private void BuildKeybindsTab()
    {
        _keybindsTab.Text = Localizer.Get("Settings_Tab_Keybinds", _uiLanguage);

        (string labelKey, ComboBox combo)[] rows =
        [
            ("Settings_Key_Forward", _forwardKeyCombo),
            ("Settings_Key_Dash", _dashKeyCombo),
            ("Settings_Key_DashModifier", _dashModifierKeyCombo),
            ("Settings_Key_Backward", _backwardKeyCombo),
            ("Settings_Key_TurnRight", _turnRightKeyCombo),
            ("Settings_Key_TurnLeft", _turnLeftKeyCombo),
            ("Settings_Key_Jump", _jumpKeyCombo),
            ("Settings_Key_Crouch", _crouchKeyCombo),
        ];

        int y = 10;
        foreach (var (labelKey, combo) in rows)
        {
            var label = new Label { Text = Localizer.Get(labelKey, _uiLanguage), Location = new Point(10, y + 4), AutoSize = true };
            combo.Location = new Point(200, y);
            _keybindsTab.Controls.Add(label);
            _keybindsTab.Controls.Add(combo);
            y += 32;
        }
    }

    private void BuildControllerTab()
    {
        _controllerTab.Text = Localizer.Get("Settings_Tab_Controller", _uiLanguage);

        _controllerStatusLabel.Text = _inputController.IsControllerAvailable
            ? Localizer.Get("Settings_ControllerStatus_OK", _uiLanguage)
            : _inputController.ControllerUnavailableReason is { } reason
                ? Localizer.GetFormatted("Settings_ControllerStatus_Unavailable", _uiLanguage, reason)
                : Localizer.Get("Settings_ControllerStatus_NotConnectedYet", _uiLanguage);

        var jumpButtonLabel = new Label { Text = Localizer.Get("Settings_ControllerButton_Jump", _uiLanguage), Location = new Point(10, 140), AutoSize = true };
        var crouchButtonLabel = new Label { Text = Localizer.Get("Settings_ControllerButton_Crouch", _uiLanguage), Location = new Point(10, 172), AutoSize = true };
        var dashButtonLabel = new Label { Text = Localizer.Get("Settings_ControllerButton_Dash", _uiLanguage), Location = new Point(10, 204), AutoSize = true };

        _jumpButtonCombo.Location = new Point(200, 136);
        _crouchButtonCombo.Location = new Point(200, 168);
        _dashButtonCombo.Location = new Point(200, 200);

        _controllerTab.Controls.AddRange([
            _controllerStatusLabel,
            jumpButtonLabel, _jumpButtonCombo,
            crouchButtonLabel, _crouchButtonCombo,
            dashButtonLabel, _dashButtonCombo,
        ]);
    }

    // Populates every control from the given settings snapshot -- used both for the real initial
    // load (from _settings) and for the Reset-to-defaults button (from a fresh AppSettings()),
    // which only touches the form fields, not _settings itself, so Cancel still discards it.
    private void LoadFromSettings(AppSettings source)
    {
        foreach (LanguageItem item in _languageCombo.Items)
        {
            if (item.Language == source.Language)
            {
                _languageCombo.SelectedItem = item;
                break;
            }
        }
        _languageCombo.SelectedItem ??= _languageCombo.Items[0];

        _outputKeyboardRadio.Checked = source.OutputMode == OutputMode.Keyboard;
        _outputKeyboardMouseRadio.Checked = source.OutputMode == OutputMode.KeyboardMouse;
        _outputControllerRadio.Checked = source.OutputMode == OutputMode.Controller;
        _outputOscRadio.Checked = source.OutputMode == OutputMode.Osc;

        _mouseTurnSpeedValue = source.MouseTurnSpeed;
        _controllerTurnSpeedValue = source.ControllerTurnSpeed;
        UpdateTurnSpeedControlForMode();
        UpdateTurnHoldRowVisibility();

        int turnHoldRaw = Math.Clamp(source.TurnHoldMs, TurnHoldRawMin, TurnHoldRawMax);
        _turnHoldSlider.Value = Math.Clamp(RawToDisplay(turnHoldRaw, TurnHoldRawMin, TurnHoldRawMax), _turnHoldSlider.Minimum, _turnHoldSlider.Maximum);
        _turnHoldValueLabel.Text = _turnHoldSlider.Value.ToString();

        int presenceSteps = Math.Clamp(source.PresenceWeightThreshold / 100, _presenceSlider.Minimum, _presenceSlider.Maximum);
        _presenceSlider.Value = presenceSteps;
        _presenceValueLabel.Text = (presenceSteps * 100).ToString();

        _sleepSecondsInput.Value = Math.Clamp(source.SleepSeconds, (int)_sleepSecondsInput.Minimum, (int)_sleepSecondsInput.Maximum);

        int walkRaw = Math.Clamp(source.FootstepThresholdPercent, WalkRawMin, WalkRawMax);
        _walkSensitivitySlider.Value = Math.Clamp(RawToDisplayInverted(walkRaw, WalkRawMin, WalkRawMax), _walkSensitivitySlider.Minimum, _walkSensitivitySlider.Maximum);
        _walkSensitivityValueLabel.Text = FormatSensitivityValue(_walkSensitivitySlider.Value);

        _dashSensitivitySlider.Value = Math.Clamp(DashRawToDisplay(source.DashPeriodMs), _dashSensitivitySlider.Minimum, _dashSensitivitySlider.Maximum);
        _dashSensitivityValueLabel.Text = FormatSensitivityValue(_dashSensitivitySlider.Value);

        int strideRaw = Math.Clamp(source.StepHoldMs, StrideRawMin, StrideRawMax);
        _strideSlider.Value = Math.Clamp(RawToDisplay(strideRaw, StrideRawMin, StrideRawMax), _strideSlider.Minimum, _strideSlider.Maximum);
        _strideValueLabel.Text = _strideSlider.Value.ToString();

        int stepContinuationRaw = Math.Clamp(source.StepContinuationMs, StepContinuationRawMin, StepContinuationRawMax);
        _stepContinuationSlider.Value = Math.Clamp(RawToDisplay(stepContinuationRaw, StepContinuationRawMin, StepContinuationRawMax), _stepContinuationSlider.Minimum, _stepContinuationSlider.Maximum);
        _stepContinuationValueLabel.Text = _stepContinuationSlider.Value.ToString();

        _continuationStepCountSlider.Value = Math.Clamp(source.ContinuationStepCount, _continuationStepCountSlider.Minimum, _continuationStepCountSlider.Maximum);
        _continuationStepCountValueLabel.Text = _continuationStepCountSlider.Value.ToString();

        _turnSensitivitySlider.Value = Math.Clamp(TurnRawToDisplay(source.TurnSensitivity), _turnSensitivitySlider.Minimum, _turnSensitivitySlider.Maximum);
        _turnSensitivityValueLabel.Text = FormatSensitivityValue(_turnSensitivitySlider.Value);
        _jumpSensitivitySlider.Value = Math.Clamp(source.JumpSensitivity, _jumpSensitivitySlider.Minimum, _jumpSensitivitySlider.Maximum);
        _jumpSensitivityValueLabel.Text = FormatSensitivityValue(_jumpSensitivitySlider.Value);
        _crouchSensitivitySlider.Value = Math.Clamp(source.CrouchSensitivity, _crouchSensitivitySlider.Minimum, _crouchSensitivitySlider.Maximum);
        _crouchSensitivityValueLabel.Text = FormatSensitivityValue(_crouchSensitivitySlider.Value);

        _turnModeHoldRadio.Checked = source.TurnMode == TurnMode.Hold;
        _turnModeFootstepRadio.Checked = source.TurnMode == TurnMode.Footstep;
        _dashInputModeComboKeyRadio.Checked = source.DashInputMode == DashInputMode.ComboKey;
        _dashInputModeDoubleTapRadio.Checked = source.DashInputMode == DashInputMode.DoubleTap;
        _forcedControllerCorrectionCheck.Checked = source.ForcedControllerCorrection;
        _debugModeCheck.Checked = source.DebugMode;
        _debugFolderInput.Text = source.DebugOutputFolder;

        _forwardKeyCombo.SelectedItem = source.ForwardKey;
        _dashKeyCombo.SelectedItem = source.DashKey;
        _dashModifierKeyCombo.SelectedItem = source.DashModifierKey;
        // Explicit call rather than relying solely on the combos' SelectedIndexChanged wiring --
        // during the very first LoadFromSettings call (from the constructor), that wiring hasn't
        // been attached yet.
        UpdateDashInputModeLabels();
        _backwardKeyCombo.SelectedItem = source.BackwardKey;
        _turnRightKeyCombo.SelectedItem = source.TurnRightKey;
        _turnLeftKeyCombo.SelectedItem = source.TurnLeftKey;
        _jumpKeyCombo.SelectedItem = source.JumpKey;
        _crouchKeyCombo.SelectedItem = source.CrouchKey;

        _jumpButtonCombo.SelectedItem = source.JumpButton;
        _crouchButtonCombo.SelectedItem = source.CrouchButton;
        _dashButtonCombo.SelectedItem = source.DashButton;
    }

    private void Save()
    {
        _settings.Language = ((LanguageItem)_languageCombo.SelectedItem!).Language;
        _settings.OutputMode = _outputOscRadio.Checked ? OutputMode.Osc
            : _outputControllerRadio.Checked ? OutputMode.Controller
            : _outputKeyboardMouseRadio.Checked ? OutputMode.KeyboardMouse
            : OutputMode.Keyboard;
        _settings.MouseTurnSpeed = _mouseTurnSpeedValue;
        _settings.ControllerTurnSpeed = _controllerTurnSpeedValue;
        _settings.TurnHoldMs = DisplayToRaw(_turnHoldSlider.Value, TurnHoldRawMin, TurnHoldRawMax);
        _settings.PresenceWeightThreshold = _presenceSlider.Value * 100;
        _settings.SleepSeconds = (int)_sleepSecondsInput.Value;
        _settings.FootstepThresholdPercent = DisplayToRawInverted(_walkSensitivitySlider.Value, WalkRawMin, WalkRawMax);
        _settings.DashPeriodMs = DashDisplayToRaw(_dashSensitivitySlider.Value);
        _settings.StepHoldMs = DisplayToRaw(_strideSlider.Value, StrideRawMin, StrideRawMax);
        _settings.StepContinuationMs = DisplayToRaw(_stepContinuationSlider.Value, StepContinuationRawMin, StepContinuationRawMax);
        _settings.ContinuationStepCount = _continuationStepCountSlider.Value;
        _settings.TurnSensitivity = TurnDisplayToRaw(_turnSensitivitySlider.Value);
        _settings.JumpSensitivity = _jumpSensitivitySlider.Value;
        _settings.CrouchSensitivity = _crouchSensitivitySlider.Value;
        _settings.TurnMode = _turnModeHoldRadio.Checked ? TurnMode.Hold : TurnMode.Footstep;
        _settings.DashInputMode = _dashInputModeDoubleTapRadio.Checked ? DashInputMode.DoubleTap : DashInputMode.ComboKey;
        _settings.ForcedControllerCorrection = _forcedControllerCorrectionCheck.Checked;
        _settings.DebugMode = _debugModeCheck.Checked;
        _settings.DebugOutputFolder = string.IsNullOrWhiteSpace(_debugFolderInput.Text) ? "debug" : _debugFolderInput.Text.Trim();

        _settings.ForwardKey = (VirtualKey)_forwardKeyCombo.SelectedItem!;
        _settings.DashKey = (VirtualKey)_dashKeyCombo.SelectedItem!;
        _settings.DashModifierKey = (VirtualKey)_dashModifierKeyCombo.SelectedItem!;
        _settings.BackwardKey = (VirtualKey)_backwardKeyCombo.SelectedItem!;
        _settings.TurnRightKey = (VirtualKey)_turnRightKeyCombo.SelectedItem!;
        _settings.TurnLeftKey = (VirtualKey)_turnLeftKeyCombo.SelectedItem!;
        _settings.JumpKey = (VirtualKey)_jumpKeyCombo.SelectedItem!;
        _settings.CrouchKey = (VirtualKey)_crouchKeyCombo.SelectedItem!;

        _settings.JumpButton = (ControllerButton)_jumpButtonCombo.SelectedItem!;
        _settings.CrouchButton = (ControllerButton)_crouchButtonCombo.SelectedItem!;
        _settings.DashButton = (ControllerButton)_dashButtonCombo.SelectedItem!;

        _settings.Save();
        SettingsChanged = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record LanguageItem(AppLanguage Language, string Display)
    {
        public override string ToString() => Display;
    }
}
