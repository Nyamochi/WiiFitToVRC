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
        Text = "WiiFitToVRC モニター";
        // FixedToolWindow carries WS_EX_TOOLWINDOW, which Windows explicitly excludes from the
        // taskbar -- the window was there (visible, focusable) but never showed a taskbar button.
        FormBorderStyle = FormBorderStyle.FixedSingle;
        ShowInTaskbar = true;
        MaximizeBox = false;
    }
}
