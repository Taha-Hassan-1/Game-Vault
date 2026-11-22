using System.Net;
using GameVault.Application.Metadata;
using GameVault.Web.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace GameVault.Tests.Unit;

public class HealthCheckTests
{
    private sealed class Factory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public Factory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;
        public List<Uri> Requests { get; } = new();
        public Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            return _respond(request, ct);
        }
    }

    private static Task<HealthCheckResult> Run(Handler handler, bool enabled = true, TimeSpan? timeout = null)
        => new RawgHealthCheck(new Factory(handler), Options.Create(new RawgOptions { Enabled = enabled, ApiKey = "secret", BaseAddress = "https://api.rawg.test/api/" }), timeout)
            .CheckHealthAsync(new HealthCheckContext());

    private static Handler Status(HttpStatusCode code) => new((_, _) => Task.FromResult(new HttpResponseMessage(code)));

    [Fact]
    public async Task A_switched_off_integration_is_healthy_and_makes_no_call()
    {
        var h = Status(HttpStatusCode.OK);
        var r = await Run(h, enabled: false);
        Assert.Equal(HealthStatus.Healthy, r.Status);
        Assert.Empty(h.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Unauthorized)] // no key is sent, so a 401 still proves the service is up
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Any_answer_below_500_means_reachable(HttpStatusCode code)
        => Assert.Equal(HealthStatus.Healthy, (await Run(Status(code))).Status);

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_server_error_is_degraded_never_unhealthy(HttpStatusCode code)
        => Assert.Equal(HealthStatus.Degraded, (await Run(Status(code))).Status);

    [Fact]
    public async Task A_network_failure_is_degraded()
    {
        var r = await Run(new Handler((_, _) => throw new HttpRequestException("connection refused")));
        Assert.Equal(HealthStatus.Degraded, r.Status);
        Assert.DoesNotContain("connection refused", r.Description); // exception text is not exposed
    }

    [Fact]
    public async Task A_hung_service_times_out_and_is_degraded()
    {
        var r = await Run(new Handler(async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return new HttpResponseMessage(); }),
            timeout: TimeSpan.FromMilliseconds(100));
        Assert.Equal(HealthStatus.Degraded, r.Status);
    }

    [Fact]
    public async Task The_probe_never_sends_the_api_key()
    {
        var h = Status(HttpStatusCode.OK);
        await Run(h);
        Assert.DoesNotContain("secret", h.Requests.Single().ToString());
        Assert.DoesNotContain("key=", h.Requests.Single().Query);
    }
}
