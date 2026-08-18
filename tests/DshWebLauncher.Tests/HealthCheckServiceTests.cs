using System.Net;
using DshWebLauncher.Services;

namespace DshWebLauncher.Tests;

public sealed class HealthCheckServiceTests
{
    [Theory]
    [InlineData("<html>ordinary web server</html>", false)]
    [InlineData("<script>window.__DSH_BOOT__ = {};</script>", true)]
    [InlineData("<title>DeepSeek Harness</title>", true)]
    public async Task IsHealthyAsync_RequiresDshMarker(string body, bool expected)
    {
        using var client = new HttpClient(new StubHandler(HttpStatusCode.OK, body));
        var checker = new DshHealthChecker(client);

        Assert.Equal(expected, await checker.IsHealthyAsync(new Uri("http://localhost:3080"), CancellationToken.None));
    }

    [Fact]
    public async Task IsHealthyAsync_RejectsErrorResponse()
    {
        using var client = new HttpClient(new StubHandler(HttpStatusCode.InternalServerError, "__DSH_BOOT__"));
        var checker = new DshHealthChecker(client);

        Assert.False(await checker.IsHealthyAsync(new Uri("http://localhost:3080"), CancellationToken.None));
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
    }
}
