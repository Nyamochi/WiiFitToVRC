using System.ComponentModel;
using System.Diagnostics;
using WiiFitToVRC.Core.Localization;
using WiiFitToVRC.Core.Settings;
using WiiFitToVRC.Core.Updates;

namespace WiiFitToVRC.App;

/// <summary>Non-forced "an update is available" notice -- a plain MessageBox can't host a
/// clickable link, a scrollable summary box, or an in-place download button, so this is a minimal
/// custom dialog instead: message, the latest commit's own message as a summary of what changed, a
/// LinkLabel for the repo URL, and either "Update now" (downloads and applies it -- see
/// AutoUpdater) or "Later" (just closes). See MonitorForm.CheckForUpdateAsync.</summary>
public sealed class UpdateAvailableForm : Form
{
    private readonly AppLanguage _language;
    private readonly string _sha;
    private readonly Label _statusLabel;
    private readonly Button _performUpdateButton;
    private readonly Button _laterButton;

    /// <summary>Non-null once a download has completed successfully -- the caller (see
    /// MonitorForm.CheckForUpdateAsync) hands this to AutoUpdater.ApplyAndRestart after the dialog
    /// closes. Left null for every other outcome (dismissed via Later, or a failed download that
    /// the user gave up retrying by closing the dialog).</summary>
    public string? DownloadedExePath { get; private set; }

    public UpdateAvailableForm(AppLanguage language, string repositoryUrl, string sha, string summary)
    {
        _language = language;
        _sha = sha;

        Text = Localizer.Get("Update_Available_Title", language);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 285);

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
            Size = new Size(390, 110),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window,
            // Otherwise this is the first control in tab order and grabs initial focus, which
            // shows its text fully selected (blue highlight) the moment the dialog opens -- Later
            // should have focus by default instead, like any ordinary dialog.
            TabStop = false,
        };

        var linkLabel = new LinkLabel
        {
            Text = repositoryUrl,
            AutoSize = true,
            Location = new Point(15, 183),
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

        _statusLabel = new Label
        {
            AutoSize = true,
            Location = new Point(15, 210),
            MaximumSize = new Size(390, 0),
            ForeColor = Color.DarkRed,
            Visible = false,
        };

        _performUpdateButton = new Button
        {
            Text = Localizer.Get("Update_PerformButton", language),
            AutoSize = true,
            Location = new Point(15, 240),
        };
        _performUpdateButton.Click += async (_, _) => await PerformUpdateAsync();

        _laterButton = new Button
        {
            Text = Localizer.Get("Update_LaterButton", language),
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            Location = new Point(325, 240),
        };

        AcceptButton = _laterButton;
        Controls.AddRange([messageLabel, summaryCaption, summaryBox, linkLabel, _statusLabel, _performUpdateButton, _laterButton]);
    }

    private async Task PerformUpdateAsync()
    {
        _performUpdateButton.Enabled = false;
        _laterButton.Enabled = false;
        _statusLabel.ForeColor = SystemColors.ControlText;
        _statusLabel.Visible = true;

        string destinationPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath)!, "WiiFitToVRC.exe.update");

        // IProgress<T> callbacks land back on this UI thread automatically -- Progress<T> captures
        // the SynchronizationContext at construction time, and this constructor always runs from
        // the button's own Click handler, i.e. already on the UI thread.
        var progress = new Progress<(long BytesDownloaded, long? TotalBytes)>(p =>
        {
            string amount = p.TotalBytes is { } total && total > 0
                ? $"{100.0 * p.BytesDownloaded / total:F0}%"
                : $"{p.BytesDownloaded / 1024.0 / 1024.0:F1} MB";
            _statusLabel.Text = Localizer.GetFormatted("Update_Downloading", _language, amount);
        });

        bool succeeded = await UpdateChecker.DownloadExeAsync(_sha, destinationPath, progress);

        if (!succeeded)
        {
            _statusLabel.ForeColor = Color.DarkRed;
            _statusLabel.Text = Localizer.Get("Update_DownloadFailed", _language);
            _performUpdateButton.Enabled = true;
            _laterButton.Enabled = true;
            return;
        }

        DownloadedExePath = destinationPath;
        Close();
    }
}
