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

    private readonly TrackBar _strokeRightSlider = new() { Minimum = 1, Maximum = 50, Location = new Point(ValueColumnX, 149), Size = new Size(180, 40), TickFrequency = 5 };
    private readonly Label _strokeRightValueLabel = new() { Location = new Point(ValueColumnX + 190, 157), AutoSize = true };
    private readonly TrackBar _strokeLeftSlider = new() { Minimum = 1, Maximum = 50, Location = new Point(ValueColumnX, 189), Size = new Size(180, 40), TickFrequency = 5 };
    private readonly Label _strokeLeftValueLabel = new() { Location = new Point(ValueColumnX + 190, 197), AutoSize = true };

    // 1000-10000 in steps of 100 -- a plain TrackBar steps by 1 per dragged unit, so the control
    // itself covers 10-100 (hundreds of weight) and the real value is *100.
    private readonly TrackBar _presenceSlider = new() { Minimum = 10, Maximum = 100, Location = new Point(ValueColumnX, 229), Size = new Size(180, 40), TickFrequency = 10 };
    private readonly Label _presenceValueLabel = new() { Location = new Point(ValueColumnX + 190, 237), AutoSize = true };

    private readonly NumericUpDown _sleepSecondsInput = new() { Minimum = 1, Maximum = 30, Location = new Point(ValueColumnX, 273), Size = new Size(70, 24) };
    private readonly NumericUpDown _footstepThresholdInput = new() { Minimum = 101, Maximum = 300, Location = new Point(ValueColumnX, 307), Size = new Size(70, 24) };
    private readonly NumericUpDown _dashPeriodInput = new() { Minimum = 100, Maximum = 1000, Increment = 10, Location = new Point(ValueColumnX, 341), Size = new Size(70, 24) };
    private readonly NumericUpDown _stepHoldInput = new() { Minimum = 20, Maximum = 1000, Increment = 10, Location = new Point(ValueColumnX, 375), Size = new Size(70, 24) };

    // Turn/jump/crouch sensitivity, each independently adjustable (0-100, 50 = today's original
    // thresholds unchanged). Flanked by qualitative "Weak"/"Strong" labels rather than a live
    // numeric readout -- the underlying value is an internal percentage-difference scale that
    // doesn't need to be shown.
    private readonly TrackBar _turnSensitivitySlider = new() { Minimum = 0, Maximum = 100, Location = new Point(ValueColumnX + 55, 435), Size = new Size(140, 40), TickFrequency = 10 };
    private readonly Label _turnSensitivityWeakLabel = new() { Location = new Point(ValueColumnX, 443), AutoSize = true };
    private readonly Label _turnSensitivityStrongLabel = new() { Location = new Point(ValueColumnX + 201, 443), AutoSize = true };

    private readonly TrackBar _jumpSensitivitySlider = new() { Minimum = 0, Maximum = 100, Location = new Point(ValueColumnX + 55, 475), Size = new Size(140, 40), TickFrequency = 10 };
    private readonly Label _jumpSensitivityWeakLabel = new() { Location = new Point(ValueColumnX, 483), AutoSize = true };
    private readonly Label _jumpSensitivityStrongLabel = new() { Location = new Point(ValueColumnX + 201, 483), AutoSize = true };

    private readonly TrackBar _crouchSensitivitySlider = new() { Minimum = 0, Maximum = 100, Location = new Point(ValueColumnX + 55, 515), Size = new Size(140, 40), TickFrequency = 10 };
    private readonly Label _crouchSensitivityWeakLabel = new() { Location = new Point(ValueColumnX, 523), AutoSize = true };
    private readonly Label _crouchSensitivityStrongLabel = new() { Location = new Point(ValueColumnX + 201, 523), AutoSize = true };

    // Separate rows, not side by side -- the Japanese labels for these are long enough that two
    // AutoSize checkboxes on one row ran into each other and got visually clipped.
    private readonly CheckBox _crouchEnabledCheck = new() { Location = new Point(ValueColumnX, 577), AutoSize = true };
    private readonly CheckBox _jumpEnabledCheck = new() { Location = new Point(ValueColumnX, 601), AutoSize = true };
    private readonly CheckBox _turnEnabledCheck = new() { Location = new Point(ValueColumnX, 625), AutoSize = true };
    private readonly CheckBox _debugModeCheck = new() { Location = new Point(ValueColumnX, 649), AutoSize = true };

    private readonly TextBox _debugFolderInput = new() { Location = new Point(ValueColumnX, 673), Size = new Size(180, 24) };
    private readonly Button _debugFolderBrowseButton = new() { Location = new Point(ValueColumnX + 186, 672), Size = new Size(34, 24) };

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
        LoadFromSettings();

        // WinForms auto-scrolls a scrollable container to keep whatever control ends up with
        // initial focus in view -- with this many controls stacked in the General tab, that could
        // land anywhere, so force it back to the top once the dialog has actually been shown.
        Shown += (_, _) => _generalTab.AutoScrollPosition = new Point(0, 0);

        _strokeRightSlider.ValueChanged += (_, _) => _strokeRightValueLabel.Text = _strokeRightSlider.Value.ToString();
        _strokeLeftSlider.ValueChanged += (_, _) => _strokeLeftValueLabel.Text = _strokeLeftSlider.Value.ToString();
        _presenceSlider.ValueChanged += (_, _) => _presenceValueLabel.Text = (_presenceSlider.Value * 100).ToString();
        _controllerStrokeRightSlider.ValueChanged += (_, _) => _controllerStrokeRightValueLabel.Text = _controllerStrokeRightSlider.Value.ToString();
        _controllerStrokeLeftSlider.ValueChanged += (_, _) => _controllerStrokeLeftValueLabel.Text = _controllerStrokeLeftSlider.Value.ToString();
        _saveButton.Click += (_, _) => Save();
        _cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
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

        _saveButton.Text = Localizer.Get("Settings_Save", _uiLanguage);
        _cancelButton.Text = Localizer.Get("Settings_Cancel", _uiLanguage);
        Controls.AddRange([_saveButton, _cancelButton]);
    }

    private void BuildGeneralTab()
    {
        _generalTab.Text = Localizer.Get("Settings_Tab_General", _uiLanguage);

        var outputModeLabel = new Label { Text = Localizer.Get("Settings_OutputMode", _uiLanguage), Location = new Point(10, 12), AutoSize = true };
        var languageLabel = new Label { Text = Localizer.Get("Settings_Language", _uiLanguage), Location = new Point(10, 113), AutoSize = true };
        var strokeRightLabel = new Label { Text = Localizer.Get("Settings_MouseStrokeRight", _uiLanguage), Location = new Point(10, 157), AutoSize = true };
        var strokeLeftLabel = new Label { Text = Localizer.Get("Settings_MouseStrokeLeft", _uiLanguage), Location = new Point(10, 197), AutoSize = true };
        var presenceLabel = new Label { Text = Localizer.Get("Settings_PresenceThreshold", _uiLanguage), Location = new Point(10, 237), AutoSize = true };
        var sleepLabel = new Label { Text = Localizer.Get("Settings_SleepSeconds", _uiLanguage), Location = new Point(10, 275), AutoSize = true };
        var footstepLabel = new Label { Text = Localizer.Get("Settings_FootstepThreshold", _uiLanguage), Location = new Point(10, 309), AutoSize = true };
        var dashPeriodLabel = new Label { Text = Localizer.Get("Settings_DashPeriod", _uiLanguage), Location = new Point(10, 343), AutoSize = true };
        var stepHoldLabel = new Label { Text = Localizer.Get("Settings_StepHold", _uiLanguage), Location = new Point(10, 377), AutoSize = true };
        var gestureSensitivityGroupLabel = new Label { Text = Localizer.Get("Settings_GestureSensitivity_Group", _uiLanguage), Location = new Point(10, 409), AutoSize = true };
        var turnSensitivityLabel = new Label { Text = Localizer.Get("Settings_GestureSensitivity_Turn", _uiLanguage), Location = new Point(10, 443), AutoSize = true };
        var jumpSensitivityLabel = new Label { Text = Localizer.Get("Settings_GestureSensitivity_Jump", _uiLanguage), Location = new Point(10, 483), AutoSize = true };
        var crouchSensitivityLabel = new Label { Text = Localizer.Get("Settings_GestureSensitivity_Crouch", _uiLanguage), Location = new Point(10, 523), AutoSize = true };
        var debugFolderLabel = new Label { Text = Localizer.Get("Settings_DebugFolder", _uiLanguage), Location = new Point(10, 677), AutoSize = true };

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
        _turnSensitivityWeakLabel.Text = weak;
        _turnSensitivityStrongLabel.Text = strong;
        _jumpSensitivityWeakLabel.Text = weak;
        _jumpSensitivityStrongLabel.Text = strong;
        _crouchSensitivityWeakLabel.Text = weak;
        _crouchSensitivityStrongLabel.Text = strong;

        foreach (var (language, nativeName) in Localizer.SelectableLanguages)
        {
            string display = language == AppLanguage.Auto ? Localizer.Get("Settings_LanguageAuto", _uiLanguage) : nativeName;
            _languageCombo.Items.Add(new LanguageItem(language, display));
        }

        _generalTab.Controls.AddRange([
            outputModeLabel, _outputKeyboardRadio, _outputKeyboardMouseRadio, _outputControllerRadio, _outputOscRadio,
            languageLabel, _languageCombo,
            strokeRightLabel, _strokeRightSlider, _strokeRightValueLabel,
            strokeLeftLabel, _strokeLeftSlider, _strokeLeftValueLabel,
            presenceLabel, _presenceSlider, _presenceValueLabel,
            sleepLabel, _sleepSecondsInput,
            footstepLabel, _footstepThresholdInput,
            dashPeriodLabel, _dashPeriodInput,
            stepHoldLabel, _stepHoldInput,
            gestureSensitivityGroupLabel,
            turnSensitivityLabel, _turnSensitivityWeakLabel, _turnSensitivitySlider, _turnSensitivityStrongLabel,
            jumpSensitivityLabel, _jumpSensitivityWeakLabel, _jumpSensitivitySlider, _jumpSensitivityStrongLabel,
            crouchSensitivityLabel, _crouchSensitivityWeakLabel, _crouchSensitivitySlider, _crouchSensitivityStrongLabel,
            _crouchEnabledCheck, _jumpEnabledCheck, _turnEnabledCheck, _debugModeCheck,
            debugFolderLabel, _debugFolderInput, _debugFolderBrowseButton,
        ]);

        // The tab's content now extends well past its fixed visible height -- scroll internally
        // (a vertical scrollbar appears automatically) rather than growing the window without bound.
        _generalTab.AutoScroll = true;
        _generalTab.AutoScrollMinSize = new Size(0, 720);
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

    private void LoadFromSettings()
    {
        foreach (LanguageItem item in _languageCombo.Items)
        {
            if (item.Language == _settings.Language)
            {
                _languageCombo.SelectedItem = item;
                break;
            }
        }
        _languageCombo.SelectedItem ??= _languageCombo.Items[0];

        _outputKeyboardRadio.Checked = _settings.OutputMode == OutputMode.Keyboard;
        _outputKeyboardMouseRadio.Checked = _settings.OutputMode == OutputMode.KeyboardMouse;
        _outputControllerRadio.Checked = _settings.OutputMode == OutputMode.Controller;
        _outputOscRadio.Checked = _settings.OutputMode == OutputMode.Osc;

        _strokeRightSlider.Value = Math.Clamp(_settings.MouseTurnStrokeRight, _strokeRightSlider.Minimum, _strokeRightSlider.Maximum);
        _strokeRightValueLabel.Text = _strokeRightSlider.Value.ToString();
        _strokeLeftSlider.Value = Math.Clamp(_settings.MouseTurnStrokeLeft, _strokeLeftSlider.Minimum, _strokeLeftSlider.Maximum);
        _strokeLeftValueLabel.Text = _strokeLeftSlider.Value.ToString();

        int presenceSteps = Math.Clamp(_settings.PresenceWeightThreshold / 100, _presenceSlider.Minimum, _presenceSlider.Maximum);
        _presenceSlider.Value = presenceSteps;
        _presenceValueLabel.Text = (presenceSteps * 100).ToString();

        _sleepSecondsInput.Value = Math.Clamp(_settings.SleepSeconds, (int)_sleepSecondsInput.Minimum, (int)_sleepSecondsInput.Maximum);
        _footstepThresholdInput.Value = Math.Clamp(_settings.FootstepThresholdPercent, (int)_footstepThresholdInput.Minimum, (int)_footstepThresholdInput.Maximum);
        _dashPeriodInput.Value = Math.Clamp(_settings.DashPeriodMs, (int)_dashPeriodInput.Minimum, (int)_dashPeriodInput.Maximum);
        _stepHoldInput.Value = Math.Clamp(_settings.StepHoldMs, (int)_stepHoldInput.Minimum, (int)_stepHoldInput.Maximum);
        _turnSensitivitySlider.Value = Math.Clamp(_settings.TurnSensitivity, _turnSensitivitySlider.Minimum, _turnSensitivitySlider.Maximum);
        _jumpSensitivitySlider.Value = Math.Clamp(_settings.JumpSensitivity, _jumpSensitivitySlider.Minimum, _jumpSensitivitySlider.Maximum);
        _crouchSensitivitySlider.Value = Math.Clamp(_settings.CrouchSensitivity, _crouchSensitivitySlider.Minimum, _crouchSensitivitySlider.Maximum);

        _crouchEnabledCheck.Checked = _settings.CrouchEnabled;
        _jumpEnabledCheck.Checked = _settings.JumpEnabled;
        _turnEnabledCheck.Checked = _settings.TurnEnabled;
        _debugModeCheck.Checked = _settings.DebugMode;
        _debugFolderInput.Text = _settings.DebugOutputFolder;

        _forwardKeyCombo.SelectedItem = _settings.ForwardKey;
        _dashKeyCombo.SelectedItem = _settings.DashKey;
        _dashModifierKeyCombo.SelectedItem = _settings.DashModifierKey;
        _backwardKeyCombo.SelectedItem = _settings.BackwardKey;
        _turnRightKeyCombo.SelectedItem = _settings.TurnRightKey;
        _turnLeftKeyCombo.SelectedItem = _settings.TurnLeftKey;
        _jumpKeyCombo.SelectedItem = _settings.JumpKey;
        _crouchKeyCombo.SelectedItem = _settings.CrouchKey;

        _controllerStrokeRightSlider.Value = Math.Clamp(_settings.ControllerTurnStrokeRight, _controllerStrokeRightSlider.Minimum, _controllerStrokeRightSlider.Maximum);
        _controllerStrokeRightValueLabel.Text = _controllerStrokeRightSlider.Value.ToString();
        _controllerStrokeLeftSlider.Value = Math.Clamp(_settings.ControllerTurnStrokeLeft, _controllerStrokeLeftSlider.Minimum, _controllerStrokeLeftSlider.Maximum);
        _controllerStrokeLeftValueLabel.Text = _controllerStrokeLeftSlider.Value.ToString();
        _jumpButtonCombo.SelectedItem = _settings.JumpButton;
        _crouchButtonCombo.SelectedItem = _settings.CrouchButton;
        _dashButtonCombo.SelectedItem = _settings.DashButton;
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
        _settings.FootstepThresholdPercent = (int)_footstepThresholdInput.Value;
        _settings.DashPeriodMs = (int)_dashPeriodInput.Value;
        _settings.StepHoldMs = (int)_stepHoldInput.Value;
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
