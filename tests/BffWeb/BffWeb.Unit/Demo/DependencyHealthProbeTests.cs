using System.Net;
using FluentAssertions;
using Haworks.BffWeb.Api;
using Haworks.BffWeb.Api.Demo;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Haworks.BffWeb.Unit.Demo;

/// <summary>
/// Covers <see cref="DependencyHealthProbe"/>'s aggregation rule ("any
/// offline -&gt; degraded", verified at source line 62) and its per-target
/// error handling. The 5s hardcoded <c>ProbeTimeout</c> race itself
/// (Task.WhenAny at line ~93) is not exercised here — it isn't injectable,
/// and a real test would need to sleep 5s. See "Tests Skipped" in the
/// session report.
/// </summary>
public sealed class DependencyHealthProbeTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        // _respond runs synchronously inside the caller's try/catch (BffServiceTokenProvider-
        // style providers await this call directly), so a synchronous throw here is caught
        // exactly like a faulted async response — no need to wrap it into a faulted Task.
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(_respond(request));
    }

    private sealed class RoutingHttpClientFactory : IHttpClientFactory
    {
        private readonly Func<string, HttpResponseMessage> _respond;
        public RoutingHttpClientFactory(Func<string, HttpResponseMessage> respond) => _respond = respond;

        public HttpClient CreateClient(string name) =>
            new(new StubHandler(_ => _respond(name))) { BaseAddress = new Uri($"http://{name}.test") };
    }

    private static Mock<IBus> ConnectedBus()
    {
        var bus = new Mock<IBus>();
        bus.Setup(b => b.Address).Returns(new Uri("rabbitmq://localhost/"));
        return bus;
    }

    private static IServiceProvider ServicesWithBus(IBus? bus)
    {
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IBus))).Returns(bus);
        return services.Object;
    }

    [Fact]
    public async Task ProbeAsync_reports_healthy_when_every_target_including_mq_is_online()
    {
        var factory = new RoutingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var probe = new DependencyHealthProbe(factory, ServicesWithBus(ConnectedBus().Object), NullLogger<DependencyHealthProbe>.Instance);

        var snapshot = await probe.ProbeAsync();

        snapshot.SystemStatus.Should().Be("healthy");
        snapshot.Services.Should().OnlyContain(s => s.Status == "online");
        snapshot.Services.Should().Contain(s => s.Id == "api");
        snapshot.Services.Should().Contain(s => s.Id == "mq");
        snapshot.Services.Should().Contain(s => s.Id == "catalog");
    }

    [Fact]
    public async Task ProbeAsync_reports_degraded_when_a_single_downstream_target_returns_non_success()
    {
        var factory = new RoutingHttpClientFactory(name =>
            name == BackendClients.Catalog
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK));
        var probe = new DependencyHealthProbe(factory, ServicesWithBus(ConnectedBus().Object), NullLogger<DependencyHealthProbe>.Instance);

        var snapshot = await probe.ProbeAsync();

        snapshot.SystemStatus.Should().Be("degraded");
        var catalog = snapshot.Services.Single(s => s.Id == "catalog");
        catalog.Status.Should().Be("degraded");
        catalog.Message.Should().Contain("503");
        // Every other target stays healthy — one bad target degrades the
        // aggregate without flipping the rest.
        snapshot.Services.Where(s => s.Id != "catalog").Should().OnlyContain(s => s.Status == "online");
    }

    [Fact]
    public async Task ProbeAsync_reports_mq_offline_with_reason_when_IBus_is_not_registered()
    {
        var factory = new RoutingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var probe = new DependencyHealthProbe(factory, ServicesWithBus(null), NullLogger<DependencyHealthProbe>.Instance);

        var snapshot = await probe.ProbeAsync();

        snapshot.SystemStatus.Should().Be("degraded");
        var mq = snapshot.Services.Single(s => s.Id == "mq");
        mq.Status.Should().Be("offline");
        mq.Message.Should().Be("IBus not registered");
    }

    [Fact]
    public async Task ProbeAsync_reports_mq_offline_when_bus_has_no_remote_address()
    {
        var factory = new RoutingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var busWithoutAddress = new Mock<IBus>();
        busWithoutAddress.Setup(b => b.Address).Returns((Uri)null!);
        var probe = new DependencyHealthProbe(factory, ServicesWithBus(busWithoutAddress.Object), NullLogger<DependencyHealthProbe>.Instance);

        var snapshot = await probe.ProbeAsync();

        var mq = snapshot.Services.Single(s => s.Id == "mq");
        mq.Status.Should().Be("offline");
        mq.Message.Should().Be("bus has no remote address");
    }

    [Fact]
    public async Task ProbeAsync_reports_a_downstream_target_offline_when_the_http_call_throws()
    {
        var factory = new RoutingHttpClientFactory(name =>
            name == BackendClients.Orders
                ? throw new HttpRequestException("connection refused")
                : new HttpResponseMessage(HttpStatusCode.OK));
        var probe = new DependencyHealthProbe(factory, ServicesWithBus(ConnectedBus().Object), NullLogger<DependencyHealthProbe>.Instance);

        var snapshot = await probe.ProbeAsync();

        var orders = snapshot.Services.Single(s => s.Id == "orders");
        orders.Status.Should().Be("offline");
        orders.Message.Should().Be(nameof(HttpRequestException));
    }
}
