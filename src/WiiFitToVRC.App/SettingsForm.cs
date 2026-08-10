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

    private static int RawToDisplayInverted(int raw, int rawMin, int rawMax) =>
        (int)Math.Round((rawMax - raw) * 100.0 / (rawMax - rawMin));

    private static int DisplayToRawInverted(int display, int rawMin, int rawMax) =>
        rawMax - (int)Math.Round(display * (rawMax - rawMin) / 100.0);

    private static int RawToDisplay(int raw, int rawMin, int rawMax) =>
        (int)Math.Round((raw - rawMin) * 100.0 / (rawMax - rawMin));

    private static int DisplayToRaw(int display, int rawMin, int rawMax) =>
        rawMin + (int)Math.Round(display * (rawMax - rawMin) / 100.0);

    // Fixed height -- the General tab has grown taller than comfortably fits on screen, so it
    // scrolls internally (see AutoScroll below) instead of the window growing without bound.
    private readonly TabControl _tabs = new() { Location = new Point(10, 10), Size = new Size(560, 480) };
    private readonly TabPage _generalTab = new();
    private readonly TabPage _keybindsTab = new();
    private readonly TabPage _controllerTab = new();

    // Keyboard = turn via Q/E, KeyboardMouse = turn via mouse-look, Controller = virtual gamepad,
    // Osc = VRChat's own OSC input endpoint (for VR setups that lock out SendInput entirely).
    private readonly RadioButton _outputKeyboardRadio = new() { Location = new Point(ValueColumnX, 8), AutoSize = true };
    private readonly RadioButton _outputKeyboardMouseRadio = new() { Location = new Point(ValueColumnX, 31), AutoSize = true };
    private readonly RadioButton _outputControllerRadio = new() { Location = new Point(ValueColumnX, 54), AutoSize = true };
    private readonly RadioButton _outputOscRadio = new() { Location = new Point(ValueColumnX, 77), AutoSize = true };

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

    private readonly TrackBar _jumpSensitivitySlider = new() { Minimum = 0, Maximum = 100, Location = new Point(ValueColumnX + 55, 285), Size = new Size(140, 40), TickFrequency = 10 };
    private readonly Label _jumpSensitivityWeakLabel = new() { Location = new Point(ValueColumnX, 293), AutoSize = true };
    private readonly Label _jumpSensitivityStrongLabel = new() { Location = new Point(ValueColumnX + 201, 293), AutoSize = true };
    private readonly Label _jumpSensitivityValueLabel = new() { Location = new Point(ValueColumnX + 255, 293), AutoSize = true };

    private readonly TrackBar _crouchSensitivitySlider = new() { Minimum = 0, Maximum = 100, Location = new Point(ValueColumnX + 55, 325), Size = new Size(140, 40), TickFrequency = 10 };
    private readonly Label _crouchSensitivityWeakLabel = new() { Location = new Point(ValueColumnX, 333), AutoSize = true };
    private readonly Label _crouchSensitivityStrongLabel = new() { Location = new Point(ValueColumnX + 201, 333), AutoSize = true };
    private readonly Label _crouchSensitivityValueLabel = new() { Location = new Point(ValueColumnX + 255, 333), AutoSize = true };

    private readonly TrackBar _strideSlider = new() { Minimum = 0, Maximum = 100, Location = new Point(ValueColumnX + 55, 365), Size = new Size(140, 40), TickFrequency = 10 };
    private readonly Label _strideNarrowLabel = new() { Location = new Point(ValueColumnX, 373), AutoSize = true };
    private readonly Label _strideWideLabel = new() { Location = new Point(ValueColumnX + 201, 373), AutoSize = true };
    private readonly Label _strideValueLabel = new() { Location = new Point(ValueColumnX + 255, 373), AutoSize = true };

    private readonly TrackBar _strokeRightSlider = new() { Minimum = 1, Maximum = 50, Location = new Point(ValueColumnX, 425), Size = new Size(180, 40), TickFrequency = 5 };
    private readonly Label _strokeRightValueLabel = new() { Location = new Point(ValueColumnX + 190, 433), AutoSize = true };
    private readonly TrackBar _strokeLeftSlider = new() { Minimum = 1, Maximum = 50, Location = new Point(ValueColumnX, 465), Size = new Size(180, 40), TickFrequency = 5 };
    private readonly Label _strokeLeftValueLabel = new() { Location = new Point(ValueColumnX + 190, 473), AutoSize = true };

    // 1000-10000 in steps of 100 -- a plain TrackBar steps by 1 per dragged unit, so the control
    // itself covers 10-100 (hundreds of weight) and the real value is *100.
    private readonly TrackBar _presenceSlider = new() { Minimum = 10, Maximum = 100, Location = new Point(ValueColumnX, 505), Size = new Size(180, 40), TickFrequency = 10 };
    private readonly Label _presenceValueLabel = new() { Location = new Point(ValueColumnX + 190, 513), AutoSize = true };

    private readonly NumericUpDown _sleepSecondsInput = new() { Minimum = 1, Maximum = 30, Location = new Point(ValueColumnX, 549), Size = new Size(70, 24) };

    // Separate rows, not side by side -- the Japanese labels for these are long enough that two
    // AutoSize checkboxes on one row ran into each other and got visually clipped.
    private readonly CheckBox _crouchEnabledCheck = new() { Location = new Point(ValueColumnX, 583), AutoSize = true };
    private readonly CheckBox _jumpEnabledCheck = new() { Location = new Point(ValueColumnX, 607), AutoSize = true };
    private readonly CheckBox _turnEnabledCheck = new() { Location = new Point(ValueColumnX, 631), AutoSize = true };
    private readonly CheckBox _debugModeCheck = new() { Location = new Point(ValueColumnX, 655), AutoSize = true };

    private readonly TextBox _debugFolderInput = new() { Location = new Point(ValueColumnX, 679), Size = new Size(180, 24) };
    private readonly Button _debugFolderBrowseButton = new() { Location = new Point(ValueColumnX + 186, 678), Size = new Size(34, 24) };

    private readonly ComboBox _forwardKeyCombo = MakeCombo<VirtualKey>();
    private readonly ComboBox _dashKeyCombo = MakeCombo<VirtualKey>();
    private readonly ComboBox _dashModifierKeyCombo = MakeCombo<VirtualKey>();
    private readonly ComboBox _backwardKeyCombo = MakeCombo<VirtualKey>();
    private readonly ComboBox _turnRightKeyCombo = MakeCombo<VirtualKey>();
    private readonly ComboBox _turnLeftKeyCombo = MakeCombo<VirtualKey>();
    private readonly ComboBox _jumpKeyCombo = MakeCombo<VirtualKey>();
    private readonly ComboBox _crouchKeyCombo = MakeCombo<VirtualKey>();

    private readonly Label _controllerStatusLabel = new() { Location = new Point(10, 10), AutoSize = true, MaximumSize = new Size(380, 0) };
    private readonly TrackBar _controllerStrokeRightSlider = new() { Minimum = 10, Maximum = 100, Location = new Point(200, 50), Size = new Size(160, 40), TickFrequency = 10 };
    private readonly Label _controllerStrokeRightValueLabel = new() { Location = new Point(365, 58), AutoSize = true };
    private readonly TrackBar _controllerStrokeLeftSlider = new() { Minimum = 10, Maximum = 100, Location = new Point(200, 90), Size = new Size(160, 40), TickFrequency = 10 };
    private readonly Label _controllerStrokeLeftValueLabel = new() { Location = new Point(365, 98), AutoSize = true };
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

        _strokeRightSlider.ValueChanged += (_, _) => _strokeRightValueLabel.Text = _strokeRightSlider.Value.ToString();
        _strokeLeftSlider.ValueChanged += (_, _) => _strokeLeftValueLabel.Text = _strokeLeftSlider.Value.ToString();
        _presenceSlider.ValueChanged += (_, _) => _presenceValueLabel.Text = (_presenceSlider.Value * 100).ToString();
        _walkSensitivitySlider.ValueChanged += (_, _) => _walkSensitivityValueLabel.Text = _walkSensitivitySlider.Value.ToString();
        _dashSensitivitySlider.ValueChanged += (_, _) => _dashSensitivityValueLabel.Text = _dashSensitivitySlider.Value.ToString();
        _turnSensitivitySlider.ValueChanged += (_, _) => _turnSensitivityValueLabel.Text = _turnSensitivitySlider.Value.ToString();
        _jumpSensitivitySlider.ValueChanged += (_, _) => _jumpSensitivityValueLabel.Text = _jumpSensitivitySlider.Value.ToString();
        _crouchSensitivitySlider.ValueChanged += (_, _) => _crouchSensitivityValueLabel.Text = _crouchSensitivitySlider.Value.ToString();
        _strideSlider.ValueChanged += (_, _) => _strideValueLabel.Text = _strideSlider.Value.ToString();
        _controllerStrokeRightSlider.ValueChanged += (_, _) => _controllerStrokeRightValueLabel.Text = _controllerStrokeRightSlider.Value.ToString();
        _controllerStrokeLeftSlider.ValueChanged += (_, _) => _controllerStrokeLeftValueLabel.Text = _controllerStrokeLeftSlider.Value.ToString();
        _saveButton.Click += (_, _) => Save();
        _cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        // Loads the hardcoded AppSettings defaults into the form fields only -- Cancel still
        // discards them, Save is still required to actually persist a reset.
        _resetButton.Click += (_, _) => LoadFromSettings(new AppSettings());
        _debugFolderBrowseButton.Click += (_, _) => BrowseDebugFolder();
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
        var jumpSensitivityLabel = new Label { Text = "  " + Localizer.Get("Settings_GestureSensitivity_Jump", _uiLanguage), Location = new Point(10, 293), AutoSize = true };
        var crouchSensitivityLabel = new Label { Text = "  " + Localizer.Get("Settings_GestureSensitivity_Crouch", _uiLanguage), Location = new Point(10, 333), AutoSize = true };
        var strideLabel = new Label { Text = "  " + Localizer.Get("Settings_GestureSensitivity_Stride", _uiLanguage), Location = new Point(10, 373), AutoSize = true };
        var strokeRightLabel = new Label { Text = Localizer.Get("Settings_MouseStrokeRight", _uiLanguage), Location = new Point(10, 433), AutoSize = true };
        var strokeLeftLabel = new Label { Text = Localizer.Get("Settings_MouseStrokeLeft", _uiLanguage), Location = new Point(10, 473), AutoSize = true };
        var presenceLabel = new Label { Text = Localizer.Get("Settings_PresenceThreshold", _uiLanguage), Location = new Point(10, 513), AutoSize = true };
        var sleepLabel = new Label { Text = Localizer.Get("Settings_SleepSeconds", _uiLanguage), Location = new Point(10, 551), AutoSize = true };
        var debugFolderLabel = new Label { Text = Localizer.Get("Settings_DebugFolder", _uiLanguage), Location = new Point(10, 683), AutoSize = true };

        _outputKeyboardRadio.Text = Localizer.Get("Settings_OutputMode_Keyboard", _uiLanguage);
        _outputKeyboardMouseRadio.Text = Localizer.Get("Settings_OutputMode_KeyboardMouse", _uiLanguage);
        _outputControllerRadio.Text = Localizer.Get("Settings_OutputMode_Controller", _uiLanguage);
        _outputOscRadio.Text = Localizer.Get("Settings_OutputMode_Osc", _uiLanguage);
        _crouchEnabledCheck.Text = Localizer.Get("Settings_CrouchEnabled", _uiLanguage);
        _jumpEnabledCheck.Text = Localizer.Get("Settings_JumpEnabled", _uiLanguage);
        _turnEnabledCheck.Text = Localizer.Get("Settings_TurnEnabled", _uiLanguage);
        _debugModeCheck.Text = Localizer.Get("Settings_DebugMode", _uiLanguage);
        // Plain "..." rather than a localized "Browse" label -- a universal enough convention to
        // fit the button's fixed, deliberately compact width in every language.
        _debugFolderBrowseButton.Text = "...";

        string weak = Localizer.Get("Settings_GestureSensitivity_Weak", _uiLanguage);
        string strong = Localizer.Get("Settings_GestureSensitivity_Strong", _uiLanguage);
        _walkSensitivityWeakLabel.Text = weak;
        _walkSensitivityStrongLabel.Text = strong;
        _dashSensitivityWeakLabel.Text = weak;
        _dashSensitivityStrongLabel.Text = strong;
        _turnSensitivityWeakLabel.Text = weak;
        _turnSensitivityStrongLabel.Text = strong;
        _jumpSensitivityWeakLabel.Text = weak;
        _jumpSensitivityStrongLabel.Text = strong;
        _crouchSensitivityWeakLabel.Text = weak;
        _crouchSensitivityStrongLabel.Text = strong;
        _strideNarrowLabel.Text = Localizer.Get("Settings_GestureSensitivity_Narrow", _uiLanguage);
        _strideWideLabel.Text = Localizer.Get("Settings_GestureSensitivity_Wide", _uiLanguage);

        foreach (var (language, nativeName) in Localizer.SelectableLanguages)
        {
            string display = language == AppLanguage.Auto ? Localizer.Get("Settings_LanguageAuto", _uiLanguage) : nativeName;
            _languageCombo.Items.Add(new LanguageItem(language, display));
        }

        _generalTab.Controls.AddRange([
            outputModeLabel, _outputKeyboardRadio, _outputKeyboardMouseRadio, _outputControllerRadio, _outputOscRadio,
            languageLabel, _languageCombo,
            gestureSensitivityGroupLabel,
            walkSensitivityLabel, _walkSensitivityWeakLabel, _walkSensitivitySlider, _walkSensitivityStrongLabel, _walkSensitivityValueLabel,
            dashSensitivityLabel, _dashSensitivityWeakLabel, _dashSensitivitySlider, _dashSensitivityStrongLabel, _dashSensitivityValueLabel,
            turnSensitivityLabel, _turnSensitivityWeakLabel, _turnSensitivitySlider, _turnSensitivityStrongLabel, _turnSensitivityValueLabel,
            jumpSensitivityLabel, _jumpSensitivityWeakLabel, _jumpSensitivitySlider, _jumpSensitivityStrongLabel, _jumpSensitivityValueLabel,
            crouchSensitivityLabel, _crouchSensitivityWeakLabel, _crouchSensitivitySlider, _crouchSensitivityStrongLabel, _crouchSensitivityValueLabel,
            strideLabel, _strideNarrowLabel, _strideSlider, _strideWideLabel, _strideValueLabel,
            strokeRightLabel, _strokeRightSlider, _strokeRightValueLabel,
            strokeLeftLabel, _strokeLeftSlider, _strokeLeftValueLabel,
            presenceLabel, _presenceSlider, _presenceValueLabel,
            sleepLabel, _sleepSecondsInput,
            _crouchEnabledCheck, _jumpEnabledCheck, _turnEnabledCheck, _debugModeCheck,
            debugFolderLabel, _debugFolderInput, _debugFolderBrowseButton,
        ]);

        // The tab's content now extends well past its fixed visible height -- scroll internally
        // (a vertical scrollbar appears automatically) rather than growing the window without bound.
        _generalTab.AutoScroll = true;
        _generalTab.AutoScrollMinSize = new Size(0, 760);
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

        var strokeRightLabel = new Label { Text = Localizer.Get("Settings_ControllerStrokeRight", _uiLanguage), Location = new Point(10, 58), AutoSize = true };
        var strokeLeftLabel = new Label { Text = Localizer.Get("Settings_ControllerStrokeLeft", _uiLanguage), Location = new Point(10, 98), AutoSize = true };
        var jumpButtonLabel = new Label { Text = Localizer.Get("Settings_ControllerButton_Jump", _uiLanguage), Location = new Point(10, 140), AutoSize = true };
        var crouchButtonLabel = new Label { Text = Localizer.Get("Settings_ControllerButton_Crouch", _uiLanguage), Location = new Point(10, 172), AutoSize = true };
        var dashButtonLabel = new Label { Text = Localizer.Get("Settings_ControllerButton_Dash", _uiLanguage), Location = new Point(10, 204), AutoSize = true };

        _jumpButtonCombo.Location = new Point(200, 136);
        _crouchButtonCombo.Location = new Point(200, 168);
        _dashButtonCombo.Location = new Point(200, 200);

        _controllerTab.Controls.AddRange([
            _controllerStatusLabel,
            strokeRightLabel, _controllerStrokeRightSlider, _controllerStrokeRightValueLabel,
            strokeLeftLabel, _controllerStrokeLeftSlider, _controllerStrokeLeftValueLabel,
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

        _strokeRightSlider.Value = Math.Clamp(source.MouseTurnStrokeRight, _strokeRightSlider.Minimum, _strokeRightSlider.Maximum);
        _strokeRightValueLabel.Text = _strokeRightSlider.Value.ToString();
        _strokeLeftSlider.Value = Math.Clamp(source.MouseTurnStrokeLeft, _strokeLeftSlider.Minimum, _strokeLeftSlider.Maximum);
        _strokeLeftValueLabel.Text = _strokeLeftSlider.Value.ToString();

        int presenceSteps = Math.Clamp(source.PresenceWeightThreshold / 100, _presenceSlider.Minimum, _presenceSlider.Maximum);
        _presenceSlider.Value = presenceSteps;
        _presenceValueLabel.Text = (presenceSteps * 100).ToString();

        _sleepSecondsInput.Value = Math.Clamp(source.SleepSeconds, (int)_sleepSecondsInput.Minimum, (int)_sleepSecondsInput.Maximum);

        int walkRaw = Math.Clamp(source.FootstepThresholdPercent, WalkRawMin, WalkRawMax);
        _walkSensitivitySlider.Value = Math.Clamp(RawToDisplayInverted(walkRaw, WalkRawMin, WalkRawMax), _walkSensitivitySlider.Minimum, _walkSensitivitySlider.Maximum);
        _walkSensitivityValueLabel.Text = _walkSensitivitySlider.Value.ToString();

        int dashRaw = Math.Clamp(source.DashPeriodMs, DashRawMin, DashRawMax);
        _dashSensitivitySlider.Value = Math.Clamp(RawToDisplayInverted(dashRaw, DashRawMin, DashRawMax), _dashSensitivitySlider.Minimum, _dashSensitivitySlider.Maximum);
        _dashSensitivityValueLabel.Text = _dashSensitivitySlider.Value.ToString();

        int strideRaw = Math.Clamp(source.StepHoldMs, StrideRawMin, StrideRawMax);
        _strideSlider.Value = Math.Clamp(RawToDisplay(strideRaw, StrideRawMin, StrideRawMax), _strideSlider.Minimum, _strideSlider.Maximum);
        _strideValueLabel.Text = _strideSlider.Value.ToString();

        _turnSensitivitySlider.Value = Math.Clamp(source.TurnSensitivity, _turnSensitivitySlider.Minimum, _turnSensitivitySlider.Maximum);
        _turnSensitivityValueLabel.Text = _turnSensitivitySlider.Value.ToString();
        _jumpSensitivitySlider.Value = Math.Clamp(source.JumpSensitivity, _jumpSensitivitySlider.Minimum, _jumpSensitivitySlider.Maximum);
        _jumpSensitivityValueLabel.Text = _jumpSensitivitySlider.Value.ToString();
        _crouchSensitivitySlider.Value = Math.Clamp(source.CrouchSensitivity, _crouchSensitivitySlider.Minimum, _crouchSensitivitySlider.Maximum);
        _crouchSensitivityValueLabel.Text = _crouchSensitivitySlider.Value.ToString();

        _crouchEnabledCheck.Checked = source.CrouchEnabled;
        _jumpEnabledCheck.Checked = source.JumpEnabled;
        _turnEnabledCheck.Checked = source.TurnEnabled;
        _debugModeCheck.Checked = source.DebugMode;
        _debugFolderInput.Text = source.DebugOutputFolder;

        _forwardKeyCombo.SelectedItem = source.ForwardKey;
        _dashKeyCombo.SelectedItem = source.DashKey;
        _dashModifierKeyCombo.SelectedItem = source.DashModifierKey;
        _backwardKeyCombo.SelectedItem = source.BackwardKey;
        _turnRightKeyCombo.SelectedItem = source.TurnRightKey;
        _turnLeftKeyCombo.SelectedItem = source.TurnLeftKey;
        _jumpKeyCombo.SelectedItem = source.JumpKey;
        _crouchKeyCombo.SelectedItem = source.CrouchKey;

        _controllerStrokeRightSlider.Value = Math.Clamp(source.ControllerTurnStrokeRight, _controllerStrokeRightSlider.Minimum, _controllerStrokeRightSlider.Maximum);
        _controllerStrokeRightValueLabel.Text = _controllerStrokeRightSlider.Value.ToString();
        _controllerStrokeLeftSlider.Value = Math.Clamp(source.ControllerTurnStrokeLeft, _controllerStrokeLeftSlider.Minimum, _controllerStrokeLeftSlider.Maximum);
        _controllerStrokeLeftValueLabel.Text = _controllerStrokeLeftSlider.Value.ToString();
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
        _settings.MouseTurnStrokeRight = _strokeRightSlider.Value;
        _settings.MouseTurnStrokeLeft = _strokeLeftSlider.Value;
        _settings.PresenceWeightThreshold = _presenceSlider.Value * 100;
        _settings.SleepSeconds = (int)_sleepSecondsInput.Value;
        _settings.FootstepThresholdPercent = DisplayToRawInverted(_walkSensitivitySlider.Value, WalkRawMin, WalkRawMax);
        _settings.DashPeriodMs = DisplayToRawInverted(_dashSensitivitySlider.Value, DashRawMin, DashRawMax);
        _settings.StepHoldMs = DisplayToRaw(_strideSlider.Value, StrideRawMin, StrideRawMax);
        _settings.TurnSensitivity = _turnSensitivitySlider.Value;
        _settings.JumpSensitivity = _jumpSensitivitySlider.Value;
        _settings.CrouchSensitivity = _crouchSensitivitySlider.Value;
        _settings.CrouchEnabled = _crouchEnabledCheck.Checked;
        _settings.JumpEnabled = _jumpEnabledCheck.Checked;
        _settings.TurnEnabled = _turnEnabledCheck.Checked;
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

        _settings.ControllerTurnStrokeRight = _controllerStrokeRightSlider.Value;
        _settings.ControllerTurnStrokeLeft = _controllerStrokeLeftSlider.Value;
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
