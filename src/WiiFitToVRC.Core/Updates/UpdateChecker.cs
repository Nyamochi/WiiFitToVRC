using System.Net.Http;
using System.Text.Json;

namespace WiiFitToVRC.Core.Updates;

public sealed record LatestExeCommit(string Sha, DateTimeOffset CommittedAt, string Message);

/// <summary>Checks GitHub for whether WiiFitToVRC.exe at the repo root has been updated more
/// recently than the exe currently running -- the published exe is committed directly to the
/// repo (no GitHub Releases in use), so "is there an update" is answered by the latest commit
/// that touched that specific path, not a version tag. Best-effort only: any failure (offline,
/// rate-limited, GitHub API shape change) returns null rather than throwing, since this must never
/// block or crash startup.</summary>
public static class UpdateChecker
{
    private const string CommitsUrl =
        "https://api.github.com/repos/Nyamochi/WiiFitToVRC/commits?path=WiiFitToVRC.exe&per_page=1";

    // GitHub's API rejects requests with no User-Agent header entirely.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    // Separate client for the exe download itself -- the ~8s timeout above is sized for a small
    // JSON API call, not a ~70MB binary; this one relies on the caller's CancellationToken instead
    // of a blanket timeout.
    private static readonly HttpClient DownloadHttp = new() { Timeout = Timeout.InfiniteTimeSpan };

    // A truncated/interrupted download would leave a file far smaller than a real build -- this is
    // just a sanity floor to reject an obviously-broken download before it ever gets swapped in
    // for the running exe (see AutoUpdater), not a precise size check.
    private const long MinPlausibleExeBytes = 20 * 1024 * 1024;

    public static async Task<LatestExeCommit?> GetLatestExeCommitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, CommitsUrl);
            request.Headers.UserAgent.ParseAdd("WiiFitToVRC-UpdateChecker");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            {
                return null;
            }

            var latest = doc.RootElement[0];
            string sha = latest.GetProperty("sha").GetString() ?? "";
            var commitElement = latest.GetProperty("commit");
            string dateText = commitElement.GetProperty("committer").GetProperty("date").GetString() ?? "";
            string message = commitElement.GetProperty("message").GetString() ?? "";
            if (sha.Length == 0 || !DateTimeOffset.TryParse(dateText, out var committedAt))
            {
                return null;
            }

            return new LatestExeCommit(sha, committedAt, message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
            or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Downloads WiiFitToVRC.exe as it existed at the given commit (raw.githubusercontent.com,
    /// not the API) to destinationPath. Pinned to that exact sha rather than "whatever's on main
    /// right now" so the download matches the commit the user was actually shown/confirmed --
    /// main could theoretically move again mid-download otherwise. Reports (bytesDownloaded,
    /// totalBytes-or-null) as it goes; totalBytes is null if the server didn't send a
    /// Content-Length. Best-effort like the rest of this class: any failure returns false rather
    /// than throwing, and never leaves a partially-written file at destinationPath behind.</summary>
    public static async Task<bool> DownloadExeAsync(string sha, string destinationPath,
        IProgress<(long BytesDownloaded, long? TotalBytes)>? progress, CancellationToken cancellationToken = default)
    {
        try
        {
            string url = $"https://raw.githubusercontent.com/Nyamochi/WiiFitToVRC/{sha}/WiiFitToVRC.exe";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("WiiFitToVRC-UpdateChecker");

            using var response = await DownloadHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            long? totalBytes = response.Content.Headers.ContentLength;

            using (var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                int read;
                while ((read = await httpStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    downloaded += read;
                    progress?.Report((downloaded, totalBytes));
                }
            }

            if (new FileInfo(destinationPath).Length < MinPlausibleExeBytes)
            {
                File.Delete(destinationPath);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            try
            {
                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }
            }
            catch (IOException) { }
            return false;
        }
    }
}
