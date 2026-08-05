using FluentAssertions;
using Haworks.BffWeb.Api.Controllers;
using Haworks.BffWeb.Api.Demo;
using Haworks.BffWeb.Api.SignalR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Haworks.BffWeb.Unit.Controllers;

/// <summary>
/// Covers <see cref="ChaosController"/>'s dev-mode-only gate (<c>IsForbidden</c>)
/// and its pause/resume/state actions. The only prior "coverage" of this
/// controller was a fake test that never referenced it.
/// </summary>
public sealed class ChaosControllerTests
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

    private static IWebHostEnvironment MockEnv(string envName)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(envName);
        return env.Object;
    }

    [Fact]
    public void GetState_returns_403_when_not_in_Development()
    {
        var manager = CreateManager();
        var controller = new ChaosController(MockEnv("Production"), manager);

        var result = controller.GetState();

        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(403);
    }

    [Fact]
    public void GetState_returns_403_when_ChaosManager_is_not_registered()
    {
        var controller = new ChaosController(MockEnv("Development"), manager: null);

        var result = controller.GetState();

        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(403);
    }

    [Fact]
    public void GetState_returns_200_with_snapshot_in_Development_with_manager_present()
    {
        var manager = CreateManager();
        var controller = new ChaosController(MockEnv("Development"), manager);

        var result = controller.GetState();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeAssignableTo<IReadOnlyDictionary<string, ChaosState>>()
            .Which.Keys.Should().BeEquivalentTo(manager.KnownTargets);
    }

    [Fact]
    public async Task Pause_unknown_target_returns_404()
    {
        var manager = CreateManager();
        var controller = new ChaosController(MockEnv("Development"), manager);

        var result = await controller.Pause("not-a-real-target", req: null);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Pause_known_target_in_Development_returns_200_and_pauses_it()
    {
        var manager = CreateManager();
        var controller = new ChaosController(MockEnv("Development"), manager);

        var result = await controller.Pause("catalog", req: null);

        result.Should().BeOfType<OkObjectResult>();
        manager.IsServiceInjected("catalog-svc").Should().BeTrue();
        await manager.DisposeAsync();
    }

    [Fact]
    public async Task Pause_is_forbidden_outside_Development_even_for_a_valid_target()
    {
        var manager = CreateManager();
        var controller = new ChaosController(MockEnv("Production"), manager);

        var result = await controller.Pause("catalog", req: null);

        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(403);
        manager.IsServiceInjected("catalog-svc").Should().BeFalse();
    }

    [Fact]
    public async Task Resume_on_a_target_that_was_never_paused_reports_already_running()
    {
        var manager = CreateManager();
        var controller = new ChaosController(MockEnv("Development"), manager);

        var result = await controller.Resume("catalog");

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Resume_after_Pause_clears_injection_and_returns_snapshot()
    {
        var manager = CreateManager();
        var controller = new ChaosController(MockEnv("Development"), manager);
        await controller.Pause("orders", req: null);

        var result = await controller.Resume("orders");

        result.Should().BeOfType<OkObjectResult>();
        manager.IsServiceInjected("orders-svc").Should().BeFalse();
    }

    [Fact]
    public async Task Resume_is_forbidden_outside_Development()
    {
        var manager = CreateManager();
        var controller = new ChaosController(MockEnv("Production"), manager);

        var result = await controller.Resume("catalog");

        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(403);
    }
}
