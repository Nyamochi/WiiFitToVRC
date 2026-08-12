using System.Diagnostics;

namespace WiiFitToVRC.App;

/// <summary>Swaps the running exe for a freshly downloaded replacement and relaunches -- see
/// MonitorForm.CheckForUpdateAsync / UpdateAvailableForm. Windows allows renaming or deleting a
/// running process's own image file (the loader maps it with FILE_SHARE_DELETE), but doing that
/// from inside the very process using the file is asking for trouble -- a tiny detached batch
/// script instead waits for this process to actually exit, then does the move-and-relaunch, so the
/// swap only ever touches a fully-closed file.</summary>
public static class AutoUpdater
{
    /// <summary>Hands off to the detached script and exits this process. downloadedExePath must
    /// already be a verified-complete download (see UpdateChecker.DownloadExeAsync) -- this does
    /// no further validation of it.</summary>
    public static void ApplyAndRestart(string downloadedExePath)
    {
        string targetExePath = Application.ExecutablePath;
        int pid = Environment.ProcessId;
        string scriptPath = Path.Combine(Path.GetTempPath(), $"WiiFitToVRC_update_{pid}.bat");

        // /FI "PID eq N" | find "N" -- waits for this exact process to vanish from the process
        // list before touching the file, rather than a fixed delay that could either race a slow
        // shutdown or needlessly pad a fast one.
        string script =
            $"""
            @echo off
            :wait
            tasklist /FI "PID eq {pid}" | find "{pid}" >nul
            if not errorlevel 1 (
                timeout /t 1 /nobreak >nul
                goto wait
            )
            move /y "{downloadedExePath}" "{targetExePath}"
            start "" "{targetExePath}"
            del "%~f0"
            """;
        File.WriteAllText(scriptPath, script);

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{scriptPath}\"",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = false,
        });

        Application.Exit();
    }
}
