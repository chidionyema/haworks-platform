using FluentAssertions;
using Haworks.Scheduler.Domain.Entities;

namespace Haworks.Scheduler.Unit;

public class RotationAuditEntryTests
{
    [Fact]
    public void Record_creates_successful_entry()
    {
        var leaseId = Guid.NewGuid();

        var entry = RotationAuditEntry.Record(leaseId, "rotate", success: true, newLeaseId: "new-123");

        entry.Id.Should().NotBeEmpty();
        entry.LeaseId.Should().Be(leaseId);
        entry.Action.Should().Be("rotate");
        entry.Success.Should().BeTrue();
        entry.NewLeaseId.Should().Be("new-123");
        entry.ErrorMessage.Should().BeNull();
        entry.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Record_creates_failed_entry()
    {
        var leaseId = Guid.NewGuid();

        var entry = RotationAuditEntry.Record(leaseId, "fail", success: false, error: "Vault sealed");

        entry.Success.Should().BeFalse();
        entry.ErrorMessage.Should().Be("Vault sealed");
        entry.NewLeaseId.Should().BeNull();
    }

    [Fact]
    public void Record_throws_on_empty_action()
    {
        var act = () => RotationAuditEntry.Record(Guid.NewGuid(), "", success: true);
        act.Should().Throw<ArgumentException>();
    }
}
