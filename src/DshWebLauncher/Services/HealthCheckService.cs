using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace DshWebLauncher.Services;

public interface IDshHealthChecker
{
    Task<bool> IsHealthyAsync(Uri uri, CancellationToken cancellationToken);
}

public sealed class DshHealthChecker(HttpClient httpClient) : IDshHealthChecker
{
    private const int MaxProbeCharacters = 64 * 1024;

    public async Task<bool> IsHealthyAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Range = new RangeHeaderValue(0, MaxProbeCharacters - 1);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices) return false;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);
            var buffer = new char[MaxProbeCharacters];
            var count = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
            var bodyStart = new string(buffer, 0, count);
            return bodyStart.Contains("__DSH_BOOT__", StringComparison.Ordinal) ||
                   bodyStart.Contains("DeepSeek Harness", StringComparison.OrdinalIgnoreCase);
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
