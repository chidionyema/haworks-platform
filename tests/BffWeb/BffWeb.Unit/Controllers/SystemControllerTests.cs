using FluentAssertions;
using Haworks.BffWeb.Api.Controllers;
using Haworks.BffWeb.Api.Demo;
using Haworks.BffWeb.Api.SignalR;
using Haworks.BffWeb.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Haworks.BffWeb.Unit.Controllers;

/// <summary>
/// Covers <see cref="SystemController"/>'s snapshot endpoints (the shape
/// portfolio-site's StatusStrip/hero-metrics/fingerprint panels depend on)
/// plus a cancellation-safety check for each SSE stream endpoint. Previously
/// zero grep hits under tests/ for this class (audit: "6 endpoints, 0 tests").
/// The streaming endpoints loop with a 5s Task.Delay between ticks; testing a
/// live tick would mean sleeping 5s in CI, so these assert the pre-cancelled
/// exit path (headers still get set, no exception, no data written) rather
/// than the steady-state tick. See "Tests Skipped" in the session report.
/// </summary>
public sealed class SystemControllerTests
{
    private static Mock<IDemoActivityCounters> MockCounters(DemoActivitySnapshot snapshot)
    {
        var mock = new Mock<IDemoActivityCounters>();
        mock.Setup(c => c.Snapshot()).Returns(snapshot);
        return mock;
    }

    private static Mock<IDependencyHealthProbe> MockProbe(DependencySnapshot snapshot)
    {
        var mock = new Mock<IDependencyHealthProbe>();
        mock.Setup(p => p.ProbeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
        return mock;
    }

    private static DependencySnapshot SampleProbeSnapshot() => new(
        Services: new[]
        {
            new DependencyStatus("catalog", "catalog-svc", "online", 12, null),
            new DependencyStatus("orders", "orders-svc", "degraded", 40, "HTTP 503"),
        },
        SystemStatus: "degraded",
        CapturedAt: new DateTime(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc));

    private static DemoActivitySnapshot SampleActivitySnapshot() => new(
        IngressEvents24h: 4200,
        ActiveSessions: 7,
        P99LatencyMs: 123.4,
        CapturedAt: new DateTime(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc));

    private static void AttachHttpContext(ControllerBase controller, CancellationToken ct = default)
    {
        var httpContext = new DefaultHttpContext
        {
            RequestAborted = ct,
        };
        httpContext.Response.Body = new MemoryStream();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    [Fact]
    public async Task GetHealthSnapshot_returns_200_combining_probe_and_activity_data()
    {
        var controller = new SystemController(
            MockCounters(SampleActivitySnapshot()).Object,
            MockProbe(SampleProbeSnapshot()).Object);

        var result = await controller.GetHealthSnapshot();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value!;
        payload.GetType().GetProperty("systemStatus")!.GetValue(payload).Should().Be("degraded");
        payload.GetType().GetProperty("p99LatencyMs")!.GetValue(payload).Should().Be(123.4);
        var services = (System.Collections.IEnumerable)payload.GetType().GetProperty("services")!.GetValue(payload)!;
        services.Cast<object>().Should().HaveCount(2);
    }

    [Fact]
    public void GetMetricsSnapshot_returns_200_with_activity_counter_fields()
    {
        var controller = new SystemController(
            MockCounters(SampleActivitySnapshot()).Object,
            MockProbe(SampleProbeSnapshot()).Object);

        var result = controller.GetMetricsSnapshot();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value!;
        payload.GetType().GetProperty("ingressEvents24h")!.GetValue(payload).Should().Be(4200L);
        payload.GetType().GetProperty("activeSessions")!.GetValue(payload).Should().Be(7);
        payload.GetType().GetProperty("clusterAvailability")!.GetValue(payload).Should().BeNull();
    }

    [Fact]
    public void GetSystemIdentity_returns_the_broadcaster_hello_fields()
    {
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.All).Returns(Mock.Of<IClientProxy>());
        var hub = new Mock<IHubContext<LiveConsoleHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);
        var broadcaster = new LiveConsoleBroadcaster(hub.Object, NullLogger<LiveConsoleBroadcaster>.Instance);
        var controller = new SystemController(
            MockCounters(SampleActivitySnapshot()).Object,
            MockProbe(SampleProbeSnapshot()).Object);

        var result = controller.GetSystemIdentity(broadcaster);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value!;
        payload.GetType().GetProperty("service")!.GetValue(payload).Should().Be("bff-web");
        payload.GetType().GetProperty("instanceId")!.GetValue(payload).Should().Be(broadcaster.Hello.InstanceId);
        payload.GetType().GetProperty("gitSha")!.GetValue(payload).Should().Be(broadcaster.Hello.GitSha);
    }

    [Fact]
    public async Task GetHealthStream_sets_SSE_headers_and_exits_without_writing_when_already_cancelled()
    {
        var probe = MockProbe(SampleProbeSnapshot());
        var controller = new SystemController(MockCounters(SampleActivitySnapshot()).Object, probe.Object);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        AttachHttpContext(controller, cts.Token);

        await controller.GetHealthStream(cts.Token);

        controller.Response.Headers["Content-Type"].ToString().Should().Be("text/event-stream");
        controller.Response.Body.Length.Should().Be(0);
        probe.Verify(p => p.ProbeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMetricsStream_sets_SSE_headers_and_exits_without_writing_when_already_cancelled()
    {
        var counters = MockCounters(SampleActivitySnapshot());
        var controller = new SystemController(counters.Object, MockProbe(SampleProbeSnapshot()).Object);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        AttachHttpContext(controller, cts.Token);

        await controller.GetMetricsStream(cts.Token);

        controller.Response.Headers["Content-Type"].ToString().Should().Be("text/event-stream");
        controller.Response.Body.Length.Should().Be(0);
        counters.Verify(c => c.Snapshot(), Times.Never);
    }

    [Fact]
    public async Task GetTopologyStream_sets_SSE_headers_and_exits_without_writing_when_already_cancelled()
    {
        var controller = new SystemController(MockCounters(SampleActivitySnapshot()).Object, MockProbe(SampleProbeSnapshot()).Object);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        AttachHttpContext(controller, cts.Token);

        await controller.GetTopologyStream(cts.Token);

        controller.Response.Headers["Content-Type"].ToString().Should().Be("text/event-stream");
        controller.Response.Body.Length.Should().Be(0);
    }
}
