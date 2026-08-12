using System.ComponentModel;
using System.Diagnostics;
using WiiFitToVRC.Core.Localization;
using WiiFitToVRC.Core.Settings;

namespace WiiFitToVRC.App;

/// <summary>Non-forced "an update is available" notice -- a plain MessageBox can't host a
/// clickable link or a scrollable summary box, so this is a minimal custom dialog instead:
/// message, the latest commit's own message as a summary of what changed, a LinkLabel for the
/// repo URL, and a single OK button that just closes it. See MonitorForm.CheckForUpdateAsync.</summary>
public sealed class UpdateAvailableForm : Form
{
    public UpdateAvailableForm(AppLanguage language, string repositoryUrl, string summary)
    {
        Text = Localizer.Get("Update_Available_Title", language);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 250);

        var messageLabel = new Label
        {
            Text = Localizer.Get("Update_Available_Message", language),
            AutoSize = true,
            Location = new Point(15, 15),
            MaximumSize = new Size(390, 0),
        };

        var summaryCaption = new Label
        {
            Text = Localizer.Get("Update_Available_SummaryLabel", language),
            AutoSize = true,
            Location = new Point(15, 45),
        };

        // Read-only + scrollable rather than an auto-sizing label -- commit messages (this repo's
        // own convention is a title line plus bullet-point body, see git log) can run to a dozen
        // lines, and a fixed-height scrollable box keeps the dialog's own size predictable instead
        // of growing to match whatever the latest commit happened to say.
        var summaryBox = new TextBox
        {
            Text = summary.Replace("\n", Environment.NewLine),
            Location = new Point(15, 65),
            Size = new Size(390, 120),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window,
            // Otherwise this is the first control in tab order and grabs initial focus, which
            // shows its text fully selected (blue highlight) the moment the dialog opens -- OK
            // should have focus by default instead, like any ordinary dialog.
            TabStop = false,
        };

        var linkLabel = new LinkLabel
        {
            Text = repositoryUrl,
            AutoSize = true,
            Location = new Point(15, 195),
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
            Location = new Point(325, 215),
        };

        AcceptButton = okButton;
        Controls.AddRange([messageLabel, summaryCaption, summaryBox, linkLabel, okButton]);
    }
}
