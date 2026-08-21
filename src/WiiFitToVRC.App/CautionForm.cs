using WiiFitToVRC.Core.Localization;
using WiiFitToVRC.Core.Settings;

namespace WiiFitToVRC.App;

/// <summary>Safety notice (floor mat / heavy-player jump warning) shown once per exe build -- see
/// MonitorForm.ShowFirstLaunchCautionIfNeeded. OK is the only way to close it.</summary>
public sealed class CautionForm : Form
{
    public CautionForm(AppLanguage language)
    {
        Text = Localizer.Get("Caution_Title", language);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(380, 150);

        var messageLabel = new Label
        {
            Text = Localizer.Get("Caution_Message", language).Replace("\n", Environment.NewLine),
            AutoSize = true,
            Location = new Point(15, 15),
            MaximumSize = new Size(350, 0),
        };

        var okButton = new Button
        {
            Text = Localizer.Get("Button_OK", language),
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Location = new Point(285, 110),
        };

        AcceptButton = okButton;
        Controls.AddRange([messageLabel, okButton]);
    }
}
