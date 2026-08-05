using FluentAssertions;
using Haworks.BffWeb.Api.SignalR;
using Xunit;

namespace Haworks.BffWeb.Unit.SignalR;

/// <summary>
/// <see cref="CheckoutHub.GroupNameFor"/> is the IDOR boundary for checkout
/// push events: a client only receives saga updates if it subscribes with
/// both the correct userId AND sagaId baked into the SignalR group name.
/// Previously untested (audit CONFIRMED gap).
/// </summary>
public sealed class CheckoutHubTests
{
    [Fact]
    public void GroupNameFor_is_deterministic_for_the_same_userId_and_sagaId()
    {
        var sagaId = Guid.NewGuid();

        CheckoutHub.GroupNameFor("user-1", sagaId).Should().Be(CheckoutHub.GroupNameFor("user-1", sagaId));
    }

    [Fact]
    public void GroupNameFor_produces_different_groups_for_different_users_on_the_same_saga()
    {
        var sagaId = Guid.NewGuid();

        var groupA = CheckoutHub.GroupNameFor("user-a", sagaId);
        var groupB = CheckoutHub.GroupNameFor("user-b", sagaId);

        // Without this, user B could subscribe to user A's sagaId group and
        // read another shopper's checkout/payment status (IDOR).
        groupA.Should().NotBe(groupB);
    }

    [Fact]
    public void GroupNameFor_produces_different_groups_for_different_sagas_for_the_same_user()
    {
        var group1 = CheckoutHub.GroupNameFor("user-1", Guid.NewGuid());
        var group2 = CheckoutHub.GroupNameFor("user-1", Guid.NewGuid());

        group1.Should().NotBe(group2);
    }

    [Fact]
    public void GroupNameFor_embeds_both_userId_and_sagaId_in_the_group_name()
    {
        var sagaId = Guid.NewGuid();

        var group = CheckoutHub.GroupNameFor("user-42", sagaId);

        group.Should().Contain("user-42").And.Contain(sagaId.ToString("N"));
    }
}
