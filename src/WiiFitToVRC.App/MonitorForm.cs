using WiiFitToVRC.Core.Bluetooth;
using WiiFitToVRC.Core.Hid;
using WiiFitToVRC.Core.Input;
using WiiFitToVRC.Core.Localization;
using WiiFitToVRC.Core.Motion;
using WiiFitToVRC.Core.Settings;
using WiiFitToVRC.Core.Updates;

namespace WiiFitToVRC.App;

public partial class MonitorForm : Form
{
    private readonly AppSettings _settings = AppSettings.Load();
    private AppLanguage CurrentLanguage => _settings.Language == AppLanguage.Auto ? Localizer.ResolveAuto() : _settings.Language;

    private readonly Label _statusLabel = new() { AutoSize = true, Font = new Font("Segoe UI", 9) };
    private readonly Button _connectButton = new() { Location = new Point(10, 30), AutoSize = true };
    private readonly Button _calibrateButton = new() { Location = new Point(210, 29), AutoSize = true, Enabled = false };
    private readonly Button _settingsButton = new() { Location = new Point(560, 10), AutoSize = true };

    private readonly Label _calibratedLabel = new() { AutoSize = true, Font = new Font("Consolas", 10) };
    private readonly SensorCalibration _calibration = new();
    private readonly InputController _inputController;

    private readonly PressurePanel _pressurePanel = new();
    private readonly Label _valuesLabel = new() { AutoSize = true, Font = new Font("Consolas", 10) };

    // Session-only odometer (see InputController.WalkDistanceMeters/DashDistanceMeters/StepCount)
    // -- never persisted, just tallied from launch to exit.
    private readonly Label _distanceLabel = new() { AutoSize = true, Font = new Font("Consolas", 10) };

    private readonly Label _arrowUp = MakeArrow("▲");
    private readonly Label _arrowDown = MakeArrow("▼");
    private readonly Label _arrowLeft = MakeArrow("◀");
    private readonly Label _arrowRight = MakeArrow("▶");
    private readonly Label _jumpLight = MakeLight("JUMP");
    private readonly Label _crouchLight = MakeLight("CROUCH");
    private readonly Label _dashLight = MakeLight("DASH");

    private readonly ComboBox _labelCombo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Location = new Point(10, 280),
        Size = new Size(140, 24),
    };
    private readonly Button _recordButton = new() { Location = new Point(160, 279), AutoSize = true, Enabled = false };
    private readonly Label _recordStatusLabel = new() { AutoSize = true, Location = new Point(270, 283) };

    // Bottom-left, always visible (unlike the debug-only recording row above it) -- see
    // AppSettings.PostureMode/EffectivePresenceWeightThreshold/EffectiveSleepSeconds. Both states
    // stay visible at once as a radio pair, not a single button that relabels itself, so which
    // posture is currently active is obvious at a glance. Isolated in its own Panel (same trick
    // SettingsForm uses for its radio-button rows) so WinForms doesn't auto-group these with any
    // other radio-button set that ends up directly on this form.
    private readonly Label _postureModeCaption = new() { AutoSize = true, Location = new Point(10, 340) };
    private readonly Panel _postureModePanel = new() { Location = new Point(10, 358), Size = new Size(340, 22), BorderStyle = BorderStyle.None };
    private readonly RadioButton _postureModeStandingRadio = new() { Location = new Point(0, 0), AutoSize = true };
    private readonly RadioButton _postureModeSittingRadio = new() { Location = new Point(110, 0), AutoSize = true };

    private BalanceBoardDevice? _device;
    private StreamWriter? _logWriter;
    private int _logRowCount;
    private string? _currentLabel;
    private CancellationTokenSource? _connectCts;
    private long _lastUiUpdateTicks;
    private long _jumpFlashUntilTicks;
    private long _weightCalibrationFlashUntilTicks;

    private static readonly string[] ActionLabels =
        ["baseline", "forward", "backward", "turn_right", "turn_left", "crouch", "jump"];

    public MonitorForm()
    {
        InitializeComponent();
        _inputController = new InputController(_settings);
        _inputController.Jumped += OnJumped;
        _inputController.WeightCalibrationRefreshed += OnWeightCalibrationRefreshed;
        BuildLayout();
        ApplyLocalization();
        _labelCombo.Items.AddRange(ActionLabels);
        _labelCombo.SelectedIndex = 0;
        _connectButton.Click += async (_, _) => await OnConnectButtonClicked();
        _recordButton.Click += (_, _) => ToggleRecording();
        _calibrateButton.Click += async (_, _) => await CalibrateAsync();
        _settingsButton.Click += (_, _) => OpenSettings();
        _postureModeStandingRadio.CheckedChanged += (_, _) => { if (_postureModeStandingRadio.Checked) SetPostureMode(PostureMode.Standing); };
        _postureModeSittingRadio.CheckedChanged += (_, _) => { if (_postureModeSittingRadio.Checked) SetPostureMode(PostureMode.Sitting); };
        // The board's SYNC button and the PC are usually not next to each other, so requiring a
        // click here before searching starts means an extra round trip just to press Connect --
        // search automatically from launch instead. The button stays live throughout: it cancels
        // an in-progress search (see OnConnectButtonClicked), and still starts a fresh one
        // manually after a cancel or a later disconnect.
        Load += async (_, _) =>
        {
            await TryAutoConnectAsync();
            if (_device is null)
            {
                await StartConnectFlowAsync();
            }
        };
        // Fire-and-forget -- an offline/slow update check must never delay or block the
        // board-connect flow above.
        Load += (_, _) => _ = CheckForUpdateAsync();
    }

    private const string RepositoryUrl = "https://github.com/Nyamochi/WiiFitToVRC";

    // Best-effort, silent-on-failure (see UpdateChecker). The published exe is committed
    // directly to the repo root rather than released via GitHub Releases, so "is there an
    // update" is answered by comparing this exe's own file timestamp against the latest commit
    // that touched that path -- not a version number. LastNotifiedUpdateSha (a hidden
    // settings.json field, no Settings UI control) makes sure the popup only ever fires once per
    // update, not on every subsequent launch until the user actually updates.
    private async Task CheckForUpdateAsync()
    {
        var latest = await UpdateChecker.GetLatestExeCommitAsync();
        if (latest is null || latest.Sha == _settings.LastNotifiedUpdateSha)
        {
            return;
        }

        DateTime exeModifiedUtc = File.GetLastWriteTimeUtc(Application.ExecutablePath);
        if (latest.CommittedAt.UtcDateTime <= exeModifiedUtc)
        {
            return;
        }

        using (var form = new UpdateAvailableForm(CurrentLanguage, RepositoryUrl, latest.Message))
        {
            form.ShowDialog(this);
        }

        _settings.LastNotifiedUpdateSha = latest.Sha;
        _settings.Save();
    }

    // Runs once at startup. If the board was already paired in a previous session, Windows may
    // still expose it as an HID device without needing SYNC pressed again -- in that case, skip
    // straight to the connected state. This is only a best-effort probe (a stray HID device with
    // a matching product ID, or a stale/about-to-drop connection, could false-positive), so the
    // connect button stays enabled as an active disconnect button rather than being locked out.
    private async Task TryAutoConnectAsync()
    {
        if (_device is not null)
        {
            return;
        }

        // Briefly hold the connect button off so a click during this ~1s probe can't race the
        // manual pairing flow into opening a second device.
        _connectButton.Enabled = false;
        try
        {
            BalanceBoardDevice? device = null;
            for (int i = 0; i < 5 && device is null; i++)
            {
                device = await Task.Run(() => BalanceBoardDevice.TryOpen());
                if (device is null)
                {
                    await Task.Delay(200);
                }
            }

            if (device is null)
            {
                return;
            }

            try
            {
                await AttachDeviceAsync(device);
                // Connecting silently in the background means there was no explicit user action
                // to hang a "now press Calibrate" prompt on -- run it once automatically instead,
                // so the app is immediately usable without requiring a manual step first.
                await AutoCalibrateAsync();
            }
            catch (Exception)
            {
                device.Dispose();
            }
        }
        finally
        {
            _connectButton.Enabled = true;
        }
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_settings, CurrentLanguage, _inputController);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            ApplyLocalization();
        }
    }

    // Calibration triggered automatically right after a connection (whether from the startup
    // probe or a full search-and-pair), rather than requiring a manual click of Calibrate. Shows
    // an explicit "place it on the floor" prompt and waits before starting, since a connection
    // completing doesn't mean the board is actually in position yet.
    private async Task AutoCalibrateAsync()
    {
        _statusLabel.Text = Localizer.Get("Status_CalibrationAutoPrompt", CurrentLanguage);
        await Task.Delay(5000);
        await CalibrateAsync();
    }

    private async Task CalibrateAsync()
    {
        _calibrateButton.Enabled = false;
        _statusLabel.Text = Localizer.Get("Status_CalibratingPrompt", CurrentLanguage);

        // Grace period before sampling actually starts. Calibration can now also fire
        // automatically right after a silent startup auto-connect, with no explicit user click to
        // hang the "step off the board" warning on -- if someone's already standing on the board
        // at that moment, calibrating immediately would zero out their own body weight as the
        // baseline, and nothing would ever read as "present" again. This gives a moment to react.
        await Task.Delay(3000);

        // Starting a new sample window makes IsCalibrated false again immediately, which cuts
        // off key/mouse output on the next sample (see OnSensorsReported) -- but if a key was
        // being held at that instant, Update() would simply stop being called for it rather than
        // releasing it, leaving it stuck down. Release explicitly before that happens.
        _inputController.ReleaseAll();
        // The zero-point is about to change, so any previously-learned weight reference (see
        // ReferenceWeightCalibrator) is now meaningless -- start that over too.
        _inputController.ResetWeightCalibration();
        _calibration.BeginSampling();
        await Task.Delay(10000);
        _calibration.FinishSampling();

        _statusLabel.Text = Localizer.Get("Status_CalibrationDone", CurrentLanguage);
        _calibrateButton.Enabled = true;
    }

    private async Task OnConnectButtonClicked()
    {
        if (_connectCts is not null)
        {
            _statusLabel.Text = Localizer.Get("Status_Aborting", CurrentLanguage);
            _connectButton.Enabled = false;
            _connectCts.Cancel();
            return;
        }

        if (_device is not null)
        {
            Disconnect();
            return;
        }

        await StartConnectFlowAsync();
    }

    // Shared by the initial auto-search on launch and a manual click of the connect button once
    // idle (after a cancel or a disconnect).
    private async Task StartConnectFlowAsync()
    {
        _connectCts = new CancellationTokenSource();
        try
        {
            await ConnectAsync(_connectCts.Token);
        }
        finally
        {
            _connectCts.Dispose();
            _connectCts = null;
        }
    }

    // Manual disconnect (button click while connected, whether auto-detected or explicitly
    // paired). Distinct from OnDeviceDisconnected, which handles the board dropping on its own.
    private void Disconnect()
    {
        if (_logWriter is not null)
        {
            ToggleRecording();
        }

        _inputController.ReleaseAll();
        _device?.Dispose();
        _device = null;

        var lang = CurrentLanguage;
        _recordButton.Enabled = false;
        _calibrateButton.Enabled = false;
        _statusLabel.Text = Localizer.Get("Status_NotConnected", lang);
        _connectButton.Text = Localizer.Get("Button_ConnectPrompt", lang);
        _connectButton.Enabled = true;
    }

    private static Label MakeArrow(string text) => new()
    {
        Text = text,
        Font = new Font("Segoe UI", 20),
        ForeColor = Color.LightGray,
        AutoSize = true,
        TextAlign = ContentAlignment.MiddleCenter,
    };

    private static Label MakeLight(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Size = new Size(70, 28),
        BackColor = Color.LightGray,
        ForeColor = Color.White,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI", 9, FontStyle.Bold),
    };

    private Label _pressureCaption = null!;
    private Label _directionCaption = null!;
    private Label _valuesCaption = null!;
    private Label _calibratedCaption = null!;
    private Label _distanceCaption = null!;

    private void BuildLayout()
    {
        _statusLabel.Location = new Point(10, 10);
        _connectButton.Location = new Point(10, 32);
        _calibrateButton.Location = new Point(210, 32);

        // Leave enough clearance below the connect/calibrate row (its text can wrap to a second
        // line at some DPIs) before the next row of captions starts, so they never overlap.
        const int captionY = 72;
        const int rowY = captionY + 20;

        _pressurePanel.Location = new Point(10, rowY);
        _pressurePanel.Size = new Size(180, 180);
        _pressureCaption = new Label { AutoSize = true, Location = new Point(10, captionY) };

        _arrowUp.Location = new Point(300, rowY);
        _arrowDown.Location = new Point(300, rowY + 100);
        _arrowLeft.Location = new Point(250, rowY + 50);
        _arrowRight.Location = new Point(350, rowY + 50);
        _jumpLight.Location = new Point(250, rowY + 140);
        _crouchLight.Location = new Point(330, rowY + 140);
        _dashLight.Location = new Point(250, rowY + 173);
        _directionCaption = new Label { AutoSize = true, Location = new Point(280, captionY) };

        _valuesLabel.Location = new Point(430, rowY);
        _valuesLabel.Text = "TR:     0\nBR:     0\nTL:     0\nBL:     0\n合計:    0";
        _valuesCaption = new Label { AutoSize = true, Location = new Point(430, captionY) };

        _calibratedLabel.Location = new Point(430, rowY + 100);
        _calibratedCaption = new Label { AutoSize = true, Location = new Point(430, rowY + 83) };

        // Bottom-right, below the calibrated-values block -- session odometer, see
        // InputController.WalkDistanceMeters/DashDistanceMeters/StepCount. Only actually refreshed
        // once sensor reports start flowing in (see UpdateUi); the placeholder zeros here are just
        // so the row doesn't look broken/blank before a device connects.
        _distanceCaption = new Label { AutoSize = true, Location = new Point(430, 285) };
        _distanceLabel.Location = new Point(430, 305);
        _distanceLabel.Text =
            Localizer.GetFormatted("Stat_WalkDistance", CurrentLanguage, 0.0) + "\n" +
            Localizer.GetFormatted("Stat_DashDistance", CurrentLanguage, 0.0) + "\n\n" +
            Localizer.GetFormatted("Stat_StepCount", CurrentLanguage, 0);

        _labelCombo.Location = new Point(10, rowY + 220);
        _recordButton.Location = new Point(160, rowY + 219);
        _recordStatusLabel.Location = new Point(270, rowY + 223);

        // Raw-data recording controls are only useful while tuning gesture thresholds, not for
        // ordinary use -- hidden unless debug mode is on (see AppSettings.DebugMode).
        _labelCombo.Visible = _settings.DebugMode;
        _recordButton.Visible = _settings.DebugMode;
        _recordStatusLabel.Visible = _settings.DebugMode;

        _postureModePanel.Controls.AddRange([_postureModeStandingRadio, _postureModeSittingRadio]);
        // CheckedChanged wiring (see the constructor) only fires SetPostureMode for the radio
        // becoming checked, not the one becoming unchecked -- setting this one directly avoids
        // firing it (and thus an extra redundant Save()) during initial layout.
        (_settings.PostureMode == PostureMode.Sitting ? _postureModeSittingRadio : _postureModeStandingRadio).Checked = true;

        Controls.AddRange([
            _statusLabel, _connectButton, _calibrateButton, _settingsButton,
            _pressureCaption, _pressurePanel,
            _directionCaption, _arrowUp, _arrowDown, _arrowLeft, _arrowRight, _jumpLight, _crouchLight, _dashLight,
            _valuesCaption, _valuesLabel,
            _calibratedCaption, _calibratedLabel,
            _distanceCaption, _distanceLabel,
            _labelCombo, _recordButton, _recordStatusLabel,
            _postureModeCaption, _postureModePanel,
        ]);
    }

    private void SetPostureMode(PostureMode mode)
    {
        _settings.PostureMode = mode;
        _settings.Save();
        // The old reference weight (whichever posture it was learned under) no longer applies to
        // the new one -- standing and sitting put very different weight on the board. Resetting
        // makes DirectionClassifier.Update re-establish it from scratch: instantly if the new mode
        // is Sitting (see instantWeightCalibration), or via the normal ~20+ second "stand still"
        // process if it's Standing.
        _inputController.ResetWeightCalibration();
    }

    /// <summary>Re-applies all static UI text for the current language -- called at startup and
    /// whenever the settings dialog closes with changes (language may have just changed).</summary>
    private void ApplyLocalization()
    {
        var lang = CurrentLanguage;
        _statusLabel.Text = _device is not null
            ? Localizer.Get("Button_Connected", lang)
            : Localizer.Get("Status_NotConnected", lang);
        _connectButton.Text = _device is not null
            ? Localizer.Get("Button_Disconnect", lang)
            : Localizer.Get("Button_ConnectPrompt", lang);
        _calibrateButton.Text = Localizer.Get("Button_Calibrate", lang);
        _settingsButton.Text = Localizer.Get("Button_Settings", lang);

        // Debug mode may have just been toggled in the settings dialog -- refresh visibility now
        // rather than requiring a restart.
        _labelCombo.Visible = _settings.DebugMode;
        _recordButton.Visible = _settings.DebugMode;
        _recordStatusLabel.Visible = _settings.DebugMode;
        _pressureCaption.Text = Localizer.Get("Caption_PressureDistribution", lang);
        _directionCaption.Text = Localizer.Get("Caption_DetectedDirection", lang);
        _valuesCaption.Text = Localizer.Get("Caption_RawValues", lang);
        _calibratedCaption.Text = Localizer.Get("Caption_CalibratedValues", lang);
        _distanceCaption.Text = Localizer.Get("Caption_DistanceTraveled", lang);
        _postureModeCaption.Text = Localizer.Get("Caption_PostureMode", lang);
        _postureModeStandingRadio.Text = Localizer.Get("PostureMode_Standing", lang);
        _postureModeSittingRadio.Text = Localizer.Get("PostureMode_Sitting", lang);
        // Only re-stamps the placeholder zeros -- once a device is connected, UpdateUi refreshes
        // this with real (already-correctly-localized) values on every UI tick anyway.
        if (_device is null)
        {
            _distanceLabel.Text =
                Localizer.GetFormatted("Stat_WalkDistance", lang, 0.0) + "\n" +
                Localizer.GetFormatted("Stat_DashDistance", lang, 0.0) + "\n\n" +
                Localizer.GetFormatted("Stat_StepCount", lang, 0);
        }
        _recordButton.Text = _logWriter is null ? Localizer.Get("Button_RecordStart", lang) : Localizer.Get("Button_RecordStop", lang);
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var lang = CurrentLanguage;
        _connectButton.Text = Localizer.Get("Button_ConnectAbort", lang);
        _statusLabel.Text = Localizer.Get("Status_Pairing", lang);
        BalanceBoardDevice? device = null;

        try
        {
            var outcome = await Task.Run(() => BalanceBoardPairing.PairAndInstall(cancellationToken: cancellationToken), cancellationToken);
            if (outcome.Result != PairingResult.Success)
            {
                _statusLabel.Text = Localizer.GetFormatted("Status_PairFail", lang, outcome.Result, outcome.Message ?? "");
                return;
            }

            _statusLabel.Text = Localizer.Get("Status_HidConnecting", lang);
            // No attempt cap or timeout here either -- keep polling for the HID interface to
            // appear until it does or the user cancels via the abort button.
            while (device is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                device = await Task.Run(() => BalanceBoardDevice.TryOpen(), cancellationToken);
                if (device is null)
                {
                    await Task.Delay(300, cancellationToken);
                }
            }

            await AttachDeviceAsync(device, cancellationToken);
            await AutoCalibrateAsync();
            return;
        }
        catch (OperationCanceledException)
        {
            device?.Dispose();
            _statusLabel.Text = Localizer.Get("Status_Aborted", lang);
        }
        catch (Exception ex)
        {
            device?.Dispose();
            _statusLabel.Text = Localizer.GetFormatted("Status_Error", lang, ex.Message);
        }

        _connectButton.Text = Localizer.Get("Button_ConnectPrompt", lang);
        _connectButton.Enabled = true;
    }

    // Wires up a freshly opened device (whether from an explicit pairing flow or the startup
    // auto-connect probe) and enters the connected state. The connect button becomes an active
    // disconnect button rather than being locked out, since auto-detection can false-positive.
    private async Task AttachDeviceAsync(BalanceBoardDevice device, CancellationToken cancellationToken = default)
    {
        device.SensorsReported += OnSensorsReported;
        device.Disconnected += OnDeviceDisconnected;
        await Task.Run(device.Start, cancellationToken);

        _device = device;
        var lang = CurrentLanguage;
        _statusLabel.Text = Localizer.Get("Button_Connected", lang);
        _recordButton.Enabled = true;
        _calibrateButton.Enabled = true;
        _connectButton.Text = Localizer.Get("Button_Disconnect", lang);
        _connectButton.Enabled = true;
    }

    private void OnDeviceDisconnected(string reason)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnDeviceDisconnected(reason));
            return;
        }

        if (_logWriter is not null)
        {
            ToggleRecording();
        }

        try
        {
            string folder = ResolveDebugFolder();
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "disconnect_log.txt"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {reason}\n");
        }
        catch (IOException) { }

        _inputController.ReleaseAll();

        _statusLabel.Text = Localizer.GetFormatted("Status_Disconnected", CurrentLanguage, reason);
        _recordButton.Enabled = false;
        _calibrateButton.Enabled = false;
        _connectButton.Text = Localizer.Get("Button_ConnectPrompt", CurrentLanguage);
        _connectButton.Enabled = true;
        // The device never disposes itself on an externally-detected disconnect (only Dispose()
        // does), so the old handles/keep-alive timer would otherwise leak and can block the next
        // CreateFile on the same device path.
        _device?.Dispose();
        _device = null;
    }

    private void ToggleRecording()
    {
        var lang = CurrentLanguage;
        if (_logWriter is null)
        {
            string folder = ResolveDebugFolder();
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            // AutoFlush would force a synchronous disk write on every single HID report (up to
            // ~100/sec) and was stalling the read thread badly enough to freeze the app -- flush
            // in batches instead (see OnSensorsReported) and once more when recording stops.
            _logWriter = new StreamWriter(path, append: false) { AutoFlush = false };
            _logRowCount = 0;
            _logWriter.WriteLine("unix_ms,label,top_right,bottom_right,top_left,bottom_left,"
                + "cal_top_right,cal_bottom_right,cal_top_left,cal_bottom_left,cal_total,"
                + "pct_top_right,pct_bottom_right,pct_top_left,pct_bottom_left");
            _currentLabel = (string)_labelCombo.SelectedItem!;
            _recordButton.Text = Localizer.Get("Button_RecordStop", lang);
            _labelCombo.Enabled = false;
            _recordStatusLabel.Text = Localizer.GetFormatted("Record_Recording", lang, _currentLabel, Path.GetFileName(path));
        }
        else
        {
            _logWriter.Flush();
            _logWriter.Dispose();
            _logWriter = null;
            _currentLabel = null;
            _recordButton.Text = Localizer.Get("Button_RecordStart", lang);
            _labelCombo.Enabled = true;
            _recordStatusLabel.Text = Localizer.Get("Record_Stopped", lang);
        }
    }

    // A relative DebugOutputFolder is resolved against the exe's own folder (not the current
    // working directory, which callers may have changed); an absolute path chosen via the
    // settings dialog's Browse button is used as-is.
    private string ResolveDebugFolder()
    {
        string folder = string.IsNullOrWhiteSpace(_settings.DebugOutputFolder) ? "debug" : _settings.DebugOutputFolder;
        return Path.IsPathRooted(folder) ? folder : Path.Combine(AppContext.BaseDirectory, folder);
    }

    // Fires on the background HID thread (via InputController.Jumped) each time a jump is
    // detected. Just remembers when to keep the JUMP light lit for a moment; the throttled UI
    // repaint below reads the timestamp.
    private void OnJumped()
    {
        _jumpFlashUntilTicks = Environment.TickCount64 + 200;
    }

    // Fires on the background HID thread (via InputController.WeightCalibrationRefreshed) when
    // the weight reference updates mid-session (e.g. someone else stepped on and stood still).
    // Only fires for refreshes after the first calibration -- that one already has its own
    // "calibrating..." -> "connected" status transition.
    private void OnWeightCalibrationRefreshed()
    {
        _weightCalibrationFlashUntilTicks = Environment.TickCount64 + 3000;
    }

    // Runs on the background HID read thread (~60-100 samples/sec) -- NOT marshaled to the UI
    // thread. Calibration bookkeeping, the CSV write, and the real key/mouse output all stay on
    // this thread and run for every sample (full rate matters for smooth mouse-look turning and
    // a complete log). Only the on-screen repaint, which is comparatively expensive, is throttled.
    private void OnSensorsReported(BalanceBoardSensors s)
    {
        _calibration.Sample(s);
        CalibratedReading? cal = _calibration.IsCalibrated ? _calibration.Apply(s) : null;
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Gate: no key/mouse output is ever sent before the first calibration completes (cal is
        // null until then, and InputController.Update no-ops on a null reading).
        _inputController.Update(s, cal, nowMs);

        if (_logWriter is not null)
        {
            string calCols = cal is not null
                ? $"{cal.TopRight},{cal.BottomRight},{cal.TopLeft},{cal.BottomLeft},{cal.Total},{cal.PctTopRight:F2},{cal.PctBottomRight:F2},{cal.PctTopLeft:F2},{cal.PctBottomLeft:F2}"
                : ",,,,,,,,";
            _logWriter.WriteLine($"{nowMs},{_currentLabel},{s.TopRight},{s.BottomRight},{s.TopLeft},{s.BottomLeft},{calCols}");
            if (++_logRowCount % 20 == 0)
            {
                _logWriter.Flush();
            }
        }

        long nowTicks = Environment.TickCount64;
        if (nowTicks - _lastUiUpdateTicks < 33) // ~30fps cap on the repaint itself
        {
            return;
        }
        _lastUiUpdateTicks = nowTicks;
        BeginInvoke(() => UpdateUi(s, cal));
    }

    private void UpdateUi(BalanceBoardSensors s, CalibratedReading? cal)
    {
        _pressurePanel.SetValues(cal, _settings.EffectivePresenceWeightThreshold);

        if (cal is not null)
        {
            _calibratedLabel.Text =
                $"合計: {cal.Total,6}\nTR: {cal.PctTopRight,5:F1}%\nBR: {cal.PctBottomRight,5:F1}%\nTL: {cal.PctTopLeft,5:F1}%\nBL: {cal.PctBottomLeft,5:F1}%";

            // Only while connected and sensor-calibrated (cal is non-null) -- during connecting,
            // aborting, or disconnected states cal stays null, so this never fights those messages.
            _statusLabel.Text = Environment.TickCount64 < _weightCalibrationFlashUntilTicks
                ? Localizer.Get("Status_WeightCalibrationRefreshed", CurrentLanguage)
                : _inputController.IsWeightCalibrated
                    ? Localizer.Get("Button_Connected", CurrentLanguage)
                    : Localizer.Get(
                        _settings.PostureMode == PostureMode.Sitting ? "Status_WeightCalibrating_Sitting" : "Status_WeightCalibrating",
                        CurrentLanguage);
        }

        int total = s.TopLeft + s.TopRight + s.BottomLeft + s.BottomRight;

        _valuesLabel.Text =
            $"TR: {s.TopRight,6}\nBR: {s.BottomRight,6}\nTL: {s.TopLeft,6}\nBL: {s.BottomLeft,6}\n合計: {total,6}";

        var lang = CurrentLanguage;
        _distanceLabel.Text =
            Localizer.GetFormatted("Stat_WalkDistance", lang, _inputController.WalkDistanceMeters) + "\n" +
            Localizer.GetFormatted("Stat_DashDistance", lang, _inputController.DashDistanceMeters) + "\n\n" +
            Localizer.GetFormatted("Stat_StepCount", lang, _inputController.StepCount);

        var direction = _inputController.LastDirection;
        SetArrowLit(_arrowUp, direction is Direction.Forward or Direction.Dash);
        SetArrowLit(_arrowDown, direction == Direction.Backward);
        SetArrowLit(_arrowRight, direction == Direction.TurnRight);
        SetArrowLit(_arrowLeft, direction == Direction.TurnLeft);
        SetLightLit(_dashLight, direction == Direction.Dash);
        SetLightLit(_crouchLight, _inputController.IsCrouching);
        SetLightLit(_jumpLight, Environment.TickCount64 < _jumpFlashUntilTicks);
    }

    private static void SetArrowLit(Label arrow, bool lit) => arrow.ForeColor = lit ? Color.DodgerBlue : Color.LightGray;

    private static void SetLightLit(Label light, bool lit)
    {
        light.BackColor = lit ? Color.OrangeRed : Color.LightGray;
    }
}
