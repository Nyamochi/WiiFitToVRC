using System.ComponentModel;
using System.Diagnostics;
using WiiFitToVRC.Core.Localization;
using WiiFitToVRC.Core.Settings;

namespace WiiFitToVRC.App;

/// <summary>Non-forced "an update is available" notice -- a plain MessageBox can't host a
/// clickable link, so this is a minimal custom dialog instead: message, a LinkLabel for the repo
/// URL, and a single OK button that just closes it. See MonitorForm.CheckForUpdateAsync.</summary>
public sealed class UpdateAvailableForm : Form
{
    public UpdateAvailableForm(AppLanguage language, string repositoryUrl)
    {
        Text = Localizer.Get("Update_Available_Title", language);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(340, 110);

        var messageLabel = new Label
        {
            Text = Localizer.Get("Update_Available_Message", language),
            AutoSize = true,
            Location = new Point(15, 15),
            MaximumSize = new Size(310, 0),
        };

        var linkLabel = new LinkLabel
        {
            Text = repositoryUrl,
            AutoSize = true,
            Location = new Point(15, 45),
        };
        linkLabel.LinkClicked += (_, _) =>
        {
            try
            {
                // UseShellExecute must be explicit here -- unlike .NET Framework, Process.Start on
                // .NET Core/5+ no longer launches URLs via the shell (and thus the OS's registered
                // default browser) by default.
                Process.Start(new ProcessStartInfo(repositoryUrl) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                // No default browser registered, or the shell otherwise couldn't launch the URL --
                // an unhandled exception here would crash the whole app, all over a convenience
                // link. The link text itself is still fully visible and selectable/copyable, so
                // just leave the dialog open rather than doing anything more.
            }
        };

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Location = new Point(245, 75),
        };

        AcceptButton = okButton;
        Controls.AddRange([messageLabel, linkLabel, okButton]);
    }
}
