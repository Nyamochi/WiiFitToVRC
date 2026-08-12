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
}
