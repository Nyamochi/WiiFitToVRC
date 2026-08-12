#nullable enable

namespace WiiFitToVRC.App;

partial class MonitorForm
{
    private System.ComponentModel.IContainer? components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _device?.Dispose();
            _logWriter?.Dispose();
            _inputController.ReleaseAll();
            _inputController.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(660, 380);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "WiiFitToVRC";
        // FixedToolWindow carries WS_EX_TOOLWINDOW, which Windows explicitly excludes from the
        // taskbar -- the window was there (visible, focusable) but never showed a taskbar button.
        FormBorderStyle = FormBorderStyle.FixedSingle;
        ShowInTaskbar = true;
        MaximizeBox = false;
        // Reads back the icon the App.csproj's ApplicationIcon (icon.ico) already embedded into
        // the exe, rather than shipping/loading the .ico file separately -- works the same way
        // whether running the regular build output or the self-contained single-file publish.
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
    }
}
