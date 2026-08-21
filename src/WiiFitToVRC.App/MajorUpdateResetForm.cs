using WiiFitToVRC.Core.Localization;
using WiiFitToVRC.Core.Settings;

namespace WiiFitToVRC.App;

/// <summary>Notice shown after a forced settings reset (see
/// MonitorForm.ShowMajorUpdateResetIfNeeded) -- unlike the ordinary "初期値" button, this one
/// already happened by the time the dialog appears; OK just acknowledges it.</summary>
public sealed class MajorUpdateResetForm : Form
{
    public MajorUpdateResetForm(AppLanguage language)
    {
        Text = Localizer.Get("MajorUpdateReset_Title", language);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(380, 130);

        var messageLabel = new Label
        {
            Text = Localizer.Get("MajorUpdateReset_Message", language),
            AutoSize = true,
            Location = new Point(15, 15),
            MaximumSize = new Size(350, 0),
        };

        var okButton = new Button
        {
            Text = Localizer.Get("Button_OK", language),
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Location = new Point(285, 90),
        };

        AcceptButton = okButton;
        Controls.AddRange([messageLabel, okButton]);
    }
}
