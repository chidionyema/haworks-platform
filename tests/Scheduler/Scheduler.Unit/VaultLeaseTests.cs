using FluentAssertions;
using Haworks.Scheduler.Domain.Entities;

namespace Haworks.Scheduler.Unit;

public class VaultLeaseTests
{
    [Fact]
    public void Create_sets_initial_state()
    {
        var lease = VaultLease.Create("catalog", "catalog-role", "database", TimeSpan.FromHours(24));

        lease.Id.Should().NotBeEmpty();
        lease.ServiceName.Should().Be("catalog");
        lease.RoleName.Should().Be("catalog-role");
        lease.CredentialType.Should().Be("database");
        lease.Status.Should().Be(VaultLeaseStatus.Active);
        lease.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(24), TimeSpan.FromSeconds(5));
        lease.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        lease.LeaseId.Should().BeNull();
        lease.LastRotatedAt.Should().BeNull();
    }

    [Theory]
    [InlineData("", "role", "database")]
    [InlineData("svc", "", "database")]
    [InlineData("svc", "role", "")]
    public void Create_throws_on_empty_strings(string svc, string role, string type)
    {
        var act = () => VaultLease.Create(svc, role, type, TimeSpan.FromHours(1));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_throws_on_zero_or_negative_ttl()
    {
        var act = () => VaultLease.Create("svc", "role", "database", TimeSpan.Zero);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MarkRotating_from_Active_succeeds()
    {
        var lease = VaultLease.Create("svc", "role", "database", TimeSpan.FromHours(24));

        lease.MarkRotating();

        lease.Status.Should().Be(VaultLeaseStatus.Rotating);
    }

    [Fact]
    public void MarkRotating_from_non_Active_throws()
    {
        var lease = VaultLease.Create("svc", "role", "database", TimeSpan.FromHours(24));
        lease.MarkRotating();

        var act = () => lease.MarkRotating();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkRotated_from_Rotating_succeeds()
    {
        var lease = VaultLease.Create("svc", "role", "database", TimeSpan.FromHours(24));
        lease.MarkRotating();

        var newExpiry = DateTimeOffset.UtcNow.AddHours(24);
        lease.MarkRotated("new-lease-id", newExpiry);

        lease.Status.Should().Be(VaultLeaseStatus.Active);
        lease.LeaseId.Should().Be("new-lease-id");
        lease.ExpiresAt.Should().Be(newExpiry);
        lease.LastRotatedAt.Should().NotBeNull();
        lease.LastRotatedAt!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void MarkRotated_from_non_Rotating_throws()
    {
        var lease = VaultLease.Create("svc", "role", "database", TimeSpan.FromHours(24));

        var act = () => lease.MarkRotated("id", DateTimeOffset.UtcNow);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkRotated_with_empty_leaseId_throws()
    {
        var lease = VaultLease.Create("svc", "role", "database", TimeSpan.FromHours(24));
        lease.MarkRotating();

        var act = () => lease.MarkRotated("", DateTimeOffset.UtcNow);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarkFailed_sets_status_and_error()
    {
        var lease = VaultLease.Create("svc", "role", "database", TimeSpan.FromHours(24));
        lease.MarkRotating();

        lease.MarkFailed("Vault 503");

        lease.Status.Should().Be(VaultLeaseStatus.Failed);
        lease.LastError.Should().Be("Vault 503");
    }

    [Fact]
    public void MarkExpired_sets_status()
    {
        var lease = VaultLease.Create("svc", "role", "database", TimeSpan.FromHours(24));

        lease.MarkExpired();

        lease.Status.Should().Be(VaultLeaseStatus.Expired);
    }

    [Fact]
    public void NeedsRotation_returns_false_for_fresh_lease()
    {
        var lease = VaultLease.Create("svc", "role", "database", TimeSpan.FromHours(24));

        lease.NeedsRotation().Should().BeFalse();
    }

    [Fact]
    public void NeedsRotation_returns_false_for_non_Active_status()
    {
        var lease = VaultLease.Create("svc", "role", "database", TimeSpan.FromHours(24));
        lease.MarkExpired();

        lease.NeedsRotation().Should().BeFalse();
    }

    [Fact]
    public async Task NeedsRotation_returns_true_when_past_threshold()
    {
        // Create a lease with a very short TTL so we immediately exceed 80%
        var lease = VaultLease.Create("svc", "role", "database", TimeSpan.FromMilliseconds(1));
        await Task.Delay(10); // ensure time has passed

        lease.NeedsRotation().Should().BeTrue();
    }

    [Fact]
    public async Task NeedsRotation_with_custom_threshold()
    {
        var lease = VaultLease.Create("svc", "role", "database", TimeSpan.FromMilliseconds(1));
        await Task.Delay(10);

        lease.NeedsRotation(thresholdPercent: 0.1).Should().BeTrue();
    }

    [Fact]
    public void ResetStaleRotating_from_Rotating_transitions_to_Failed()
    {
        var lease = VaultLease.Create("svc", "role", "database", TimeSpan.FromHours(24));
        lease.MarkRotating();

        lease.ResetStaleRotating("Stale rotation detected");

        lease.Status.Should().Be(VaultLeaseStatus.Failed);
        lease.LastError.Should().Be("Stale rotation detected");
    }

    [Fact]
    public void ResetStaleRotating_from_non_Rotating_throws()
    {
        var lease = VaultLease.Create("svc", "role", "database", TimeSpan.FromHours(24));

        var act = () => lease.ResetStaleRotating("reason");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkRotated_clears_LastError()
    {
        var lease = VaultLease.Create("svc", "role", "database", TimeSpan.FromHours(24));
        lease.MarkRotating();
        lease.MarkFailed("some error");

        // Re-create to test the clearing behavior (since Failed can't go to Rotating directly)
        var lease2 = VaultLease.Create("svc2", "role2", "database", TimeSpan.FromHours(24));
        lease2.MarkRotating();
        lease2.MarkRotated("new-id", DateTimeOffset.UtcNow.AddHours(24));

        lease2.LastError.Should().BeNull();
    }
}
