using WiiFitToVRC.Core.Bluetooth;
using WiiFitToVRC.Core.Localization;
using WiiFitToVRC.Core.Settings;

namespace WiiFitToVRC.App;

/// <summary>Shown after a debug "devices" recording stops -- lists every Bluetooth device seen
/// during the scan (name + address) in a read-only, copyable box, so a user whose board isn't
/// recognized (e.g. an unofficial Korean-market model) can identify its exact name/address and
/// report it. See MonitorForm.ToggleRecording and Core.Bluetooth.BluetoothDeviceScanner.</summary>
public sealed class DetectedDevicesForm : Form
{
    public DetectedDevicesForm(AppLanguage language, IReadOnlyList<DetectedBluetoothDevice> devices)
    {
        Text = Localizer.Get("Devices_PopupTitle", language);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 300);

        var headingLabel = new Label
        {
            Text = Localizer.Get("Devices_PopupHeading", language),
            AutoSize = true,
            Location = new Point(15, 15),
        };

        string listText = devices.Count > 0
            ? string.Join(Environment.NewLine, devices
                .OrderBy(d => d.Name)
                .Select(d => string.IsNullOrWhiteSpace(d.Name) ? d.Address : $"{d.Name}  ({d.Address})"))
            : Localizer.Get("Devices_PopupNone", language);

        // Read-only + selectable rather than a Label -- the whole point is letting the user copy
        // an exact (possibly unfamiliar-script) device name out to report it.
        var listBox = new TextBox
        {
            Text = listText,
            Location = new Point(15, 40),
            Size = new Size(390, 210),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window,
            TabStop = false,
        };

        var okButton = new Button
        {
            Text = Localizer.Get("Button_OK", language),
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Location = new Point(325, 260),
        };

        AcceptButton = okButton;
        Controls.AddRange([headingLabel, listBox, okButton]);
    }
}
