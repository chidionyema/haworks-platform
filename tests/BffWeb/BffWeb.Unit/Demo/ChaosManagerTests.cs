using FluentAssertions;
using Haworks.BffWeb.Api.Demo;
using Haworks.BffWeb.Api.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Haworks.BffWeb.Unit.Demo;

/// <summary>
/// Real behavioral coverage for <see cref="ChaosManager"/>, replacing a
/// prior fake test that asserted against an unrelated anonymous object and
/// never touched the class under test. Only service targets (catalog,
/// orders, payments, checkout, identity) are exercised — those use
/// <c>FaultInjectionStrategy</c>, a pure in-memory HashSet toggle. Container
/// targets (postgres/redis/rabbitmq/vault) shell out to the real `docker`
/// binary and are deliberately not exercised here.
/// </summary>
public sealed class ChaosManagerTests
{
    private static ChaosManager CreateManager()
    {
        var clientProxy = new Mock<IClientProxy>();
        clientProxy
            .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.All).Returns(clientProxy.Object);

        var hubContext = new Mock<IHubContext<LiveConsoleHub>>();
        hubContext.Setup(h => h.Clients).Returns(clients.Object);

        return new ChaosManager(hubContext.Object, NullLogger<ChaosManager>.Instance);
    }

    [Fact]
    public void Snapshot_reports_all_known_targets_as_running_when_nothing_paused()
    {
        var manager = CreateManager();

        var snapshot = manager.Snapshot();

        snapshot.Keys.Should().BeEquivalentTo(manager.KnownTargets);
        snapshot.Values.Should().OnlyContain(s => s.Status == "running" && s.ResumeAtUtc == null && s.RemainingSeconds == null);
    }

    [Fact]
    public void IsServiceInjected_returns_false_for_a_target_that_has_never_been_paused()
    {
        var manager = CreateManager();

        manager.IsServiceInjected("catalog-svc").Should().BeFalse();
    }

    [Fact]
    public async Task PauseAsync_unknown_target_returns_NotFound_and_does_not_create_a_snapshot_entry()
    {
        var manager = CreateManager();

        var result = await manager.PauseAsync("not-a-real-target", null);

        result.Should().Be(PauseResult.NotFound);
        manager.Snapshot().Should().NotContainKey("not-a-real-target");
    }

    [Fact]
    public async Task PauseAsync_known_service_target_marks_it_paused_and_sets_IsServiceInjected_true()
    {
        await using var manager = CreateManager();

        var result = await manager.PauseAsync("catalog", TimeSpan.FromSeconds(10));

        result.Should().Be(PauseResult.Ok);
        manager.IsServiceInjected("catalog-svc").Should().BeTrue();
        var snapshot = manager.Snapshot();
        snapshot["catalog"].Status.Should().Be("paused");
        snapshot["catalog"].RemainingSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PauseAsync_clamps_a_too_long_duration_to_the_2_minute_maximum()
    {
        await using var manager = CreateManager();

        await manager.PauseAsync("orders", TimeSpan.FromMinutes(10));

        var remaining = manager.Snapshot()["orders"].RemainingSeconds;
        remaining.Should().NotBeNull();
        remaining!.Value.Should().BeLessOrEqualTo(120);
    }

    [Fact]
    public async Task PauseAsync_clamps_a_too_short_duration_to_the_5_second_minimum()
    {
        await using var manager = CreateManager();

        await manager.PauseAsync("payments", TimeSpan.FromMilliseconds(1));

        var remaining = manager.Snapshot()["payments"].RemainingSeconds;
        remaining.Should().NotBeNull();
        remaining!.Value.Should().BeInRange(1, 5);
    }

    [Fact]
    public async Task PauseAsync_on_an_already_paused_target_is_idempotent_and_refreshes_the_deadline()
    {
        await using var manager = CreateManager();
        await manager.PauseAsync("checkout", TimeSpan.FromSeconds(5));
        var firstDeadline = manager.Snapshot()["checkout"].ResumeAtUtc;

        var result = await manager.PauseAsync("checkout", TimeSpan.FromSeconds(60));

        result.Should().Be(PauseResult.Ok);
        manager.IsServiceInjected("checkout-svc").Should().BeTrue();
        var secondDeadline = manager.Snapshot()["checkout"].ResumeAtUtc;
        secondDeadline.Should().NotBe(firstDeadline);
    }

    [Fact]
    public async Task ResumeAsync_unpauses_a_paused_target_and_clears_IsServiceInjected()
    {
        var manager = CreateManager();
        await manager.PauseAsync("identity", TimeSpan.FromSeconds(30));
        manager.IsServiceInjected("identity-svc").Should().BeTrue();

        var resumed = await manager.ResumeAsync("identity");

        resumed.Should().BeTrue();
        manager.IsServiceInjected("identity-svc").Should().BeFalse();
        manager.Snapshot()["identity"].Status.Should().Be("running");
    }

    [Fact]
    public async Task ResumeAsync_unknown_target_returns_false()
    {
        var manager = CreateManager();

        var resumed = await manager.ResumeAsync("not-a-real-target");

        resumed.Should().BeFalse();
    }

    [Fact]
    public async Task ResumeAsync_on_a_target_that_is_not_currently_paused_returns_false()
    {
        var manager = CreateManager();

        var resumed = await manager.ResumeAsync("catalog");

        resumed.Should().BeFalse();
    }

    [Fact]
    public async Task DisposeAsync_resumes_every_still_paused_target()
    {
        var manager = CreateManager();
        await manager.PauseAsync("catalog", TimeSpan.FromSeconds(30));
        manager.IsServiceInjected("catalog-svc").Should().BeTrue();

        await manager.DisposeAsync();

        manager.IsServiceInjected("catalog-svc").Should().BeFalse();
    }
}
