using FluentAssertions;
using Haworks.Contracts.Rotation;
using Haworks.Scheduler.Application.Common.Interfaces;
using Haworks.Scheduler.Application.Jobs;
using Haworks.Scheduler.Domain.Entities;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using VaultSharp;
using VaultSharp.V1;
using VaultSharp.V1.SecretsEngines;
using VaultSharp.V1.SecretsEngines.Database;

namespace Haworks.Scheduler.Unit;

public class LeaseWatcherJobTests
{
    private readonly Mock<ILeaseRepository> _repoMock = new();
    private readonly Mock<IVaultClient> _vaultMock = new();
    private readonly Mock<IPublishEndpoint> _publishMock = new();
    private readonly Mock<ILogger<LeaseWatcherJob>> _loggerMock = new();
    private readonly LeaseWatcherJob _job;

    public LeaseWatcherJobTests()
    {
        _job = new LeaseWatcherJob(
            _repoMock.Object,
            _vaultMock.Object,
            _publishMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ExecuteAsync_no_leases_needing_rotation_does_nothing()
    {
        _repoMock.Setup(r => r.GetStaleRotatingLeasesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VaultLease>());
        _repoMock.Setup(r => r.GetActiveLeasesNeedingRotationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VaultLease>());

        await _job.ExecuteAsync(CancellationToken.None);

        _publishMock.Verify(p => p.Publish(It.IsAny<CredentialRotatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        _publishMock.Verify(p => p.Publish(It.IsAny<RotationFailedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_resets_stale_rotating_leases()
    {
        var staleLease = VaultLease.Create("catalog", "catalog-role", "database", TimeSpan.FromHours(24));
        staleLease.MarkRotating();

        _repoMock.Setup(r => r.GetStaleRotatingLeasesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VaultLease> { staleLease });
        _repoMock.Setup(r => r.GetActiveLeasesNeedingRotationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VaultLease>());

        await _job.ExecuteAsync(CancellationToken.None);

        staleLease.Status.Should().Be(VaultLeaseStatus.Failed);
        _repoMock.Verify(r => r.AddAuditEntryAsync(It.IsAny<RotationAuditEntry>(), It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_rotates_database_credential_successfully()
    {
        var lease = VaultLease.Create("catalog", "catalog-role", "database", TimeSpan.FromMilliseconds(1));
        Thread.Sleep(2); // ensure NeedsRotation is true

        _repoMock.Setup(r => r.GetStaleRotatingLeasesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VaultLease>());
        _repoMock.Setup(r => r.GetActiveLeasesNeedingRotationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VaultLease> { lease });

        // Mock vault database credentials response
        var vaultV1Mock = new Mock<IVaultClientV1>();
        var secretsEngineMock = new Mock<ISecretsEngine>();
        var databaseMock = new Mock<IDatabaseSecretsEngine>();
        _vaultMock.Setup(v => v.V1).Returns(vaultV1Mock.Object);
        vaultV1Mock.Setup(v => v.Secrets).Returns(secretsEngineMock.Object);
        secretsEngineMock.Setup(s => s.Database).Returns(databaseMock.Object);

        var credResponse = new VaultSharp.V1.Commons.Secret<UsernamePasswordCredentials>
        {
            LeaseId = "database/creds/catalog-role/abc123",
            LeaseDurationSeconds = 86400,
            Data = new UsernamePasswordCredentials { Username = "v-root-catalog", Password = "generated" }
        };
        databaseMock.Setup(d => d.GetCredentialsAsync("catalog-role", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(credResponse);

        await _job.ExecuteAsync(CancellationToken.None);

        lease.Status.Should().Be(VaultLeaseStatus.Active);
        lease.LeaseId.Should().Be("database/creds/catalog-role/abc123");
        lease.LastRotatedAt.Should().NotBeNull();
        _publishMock.Verify(p => p.Publish(It.Is<CredentialRotatedEvent>(e =>
            e.ServiceName == "catalog" && e.RoleName == "catalog-role"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_handles_vault_failure_gracefully()
    {
        var lease = VaultLease.Create("catalog", "catalog-role", "database", TimeSpan.FromMilliseconds(1));
        Thread.Sleep(2);

        _repoMock.Setup(r => r.GetStaleRotatingLeasesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VaultLease>());
        _repoMock.Setup(r => r.GetActiveLeasesNeedingRotationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VaultLease> { lease });

        // Mock vault to throw
        var vaultV1Mock = new Mock<IVaultClientV1>();
        var secretsEngineMock = new Mock<ISecretsEngine>();
        var databaseMock = new Mock<IDatabaseSecretsEngine>();
        _vaultMock.Setup(v => v.V1).Returns(vaultV1Mock.Object);
        vaultV1Mock.Setup(v => v.Secrets).Returns(secretsEngineMock.Object);
        secretsEngineMock.Setup(s => s.Database).Returns(databaseMock.Object);
        databaseMock.Setup(d => d.GetCredentialsAsync("catalog-role", It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new VaultSharp.Core.VaultApiException("Vault sealed"));

        await _job.ExecuteAsync(CancellationToken.None);

        lease.Status.Should().Be(VaultLeaseStatus.Failed);
        lease.LastError.Should().Contain("Vault sealed");
        _publishMock.Verify(p => p.Publish(It.Is<RotationFailedEvent>(e =>
            e.ServiceName == "catalog" && e.RoleName == "catalog-role"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_kv_type_marks_rotated_without_vault_call()
    {
        var lease = VaultLease.Create("identity", "identity-role", "kv", TimeSpan.FromMilliseconds(1));
        Thread.Sleep(2);

        _repoMock.Setup(r => r.GetStaleRotatingLeasesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VaultLease>());
        _repoMock.Setup(r => r.GetActiveLeasesNeedingRotationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VaultLease> { lease });

        await _job.ExecuteAsync(CancellationToken.None);

        lease.Status.Should().Be(VaultLeaseStatus.Active);
        lease.LeaseId.Should().StartWith("kv-");
        lease.LastRotatedAt.Should().NotBeNull();
        _repoMock.Verify(r => r.AddAuditEntryAsync(
            It.Is<RotationAuditEntry>(e => e.Success), It.IsAny<CancellationToken>()), Times.Once);
    }
}
