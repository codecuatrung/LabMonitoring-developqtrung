using System.Net;
using System.Net.Http.Headers;
using LabMonitoring.Agent.Identity;

namespace LabMonitoring.Agent.Client;

/// <summary>
/// HTTP client for file-transfer bodies (download of pushed files, upload of collected zips). Kept apart from
/// <see cref="AgentApiClient"/> because its <see cref="HttpClient"/> has an infinite timeout: a 500 MB
/// transfer over a busy Wi-Fi legitimately takes longer than the 120s the small heartbeat/config calls allow.
/// Cancellation is driven by the caller's token instead.
/// </summary>
public sealed class TransferApiClient(HttpClient http, CredentialStore credentials)
{
    /// <summary>Starts (or resumes, when <paramref name="rangeFrom"/> &gt; 0) the download of a pushed file.
    /// Caller disposes the response. Status is the caller's to interpret (200/206 ok, 429 busy, 403/404 not ready).</summary>
    public async Task<HttpResponseMessage> DownloadFileAsync(int transferId, long rangeFrom, CancellationToken ct)
    {
        var msg = Authorized(HttpMethod.Get, $"api/v1/agent/transfers/{transferId}/file");
        if (rangeFrom > 0)
            msg.Headers.Range = new RangeHeaderValue(rangeFrom, null);
        return await http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    /// <summary>Uploads the zip built for a collect transfer; returns the HTTP status.</summary>
    public async Task<HttpStatusCode> UploadCollectedAsync(int transferId, Stream zip, int fileCount, bool truncated, CancellationToken ct)
    {
        using var msg = Authorized(HttpMethod.Post,
            $"api/v1/agent/transfers/{transferId}/collected?fileCount={fileCount}&truncated={(truncated ? "true" : "false")}");
        msg.Content = new StreamContent(zip);
        msg.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        msg.Content.Headers.ContentLength = zip.Length;

        using var response = await http.SendAsync(msg, ct);
        return response.StatusCode;
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        var creds = credentials.Load() ?? throw new InvalidOperationException("Agent is not registered.");
        var msg = new HttpRequestMessage(method, path);
        msg.Headers.Authorization = new AuthenticationHeaderValue("AgentToken", creds.AgentToken);
        return msg;
    }
}
