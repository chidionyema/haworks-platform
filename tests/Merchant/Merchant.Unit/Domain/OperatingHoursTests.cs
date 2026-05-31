using FluentAssertions;
using Haworks.Merchant.Domain.Aggregates;
using Xunit;

namespace Haworks.Merchant.Unit.Domain;

public sealed class OperatingHoursTests
{
    [Fact]
    public void Create_sets_IsOpen_to_true_by_default()
    {
        var hours = OperatingHours.Create(Guid.NewGuid(), 1, TimeSpan.FromHours(9), TimeSpan.FromHours(17));

        hours.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void Close_sets_IsOpen_to_false()
    {
        var hours = OperatingHours.Create(Guid.NewGuid(), 1, TimeSpan.FromHours(9), TimeSpan.FromHours(17));

        hours.Close();

        hours.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Open_sets_IsOpen_to_true()
    {
        var hours = OperatingHours.Create(Guid.NewGuid(), 1, TimeSpan.FromHours(9), TimeSpan.FromHours(17), false);

        hours.Open();

        hours.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void Create_with_midnight_times_creates_24_hour_operation()
    {
        var hours = OperatingHours.Create(Guid.NewGuid(), 1, TimeSpan.Zero, TimeSpan.FromHours(24));

        hours.OpenTime.Should().Be(TimeSpan.Zero);
        hours.CloseTime.Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void Create_with_both_zero_times_represents_closed_all_day()
    {
        var hours = OperatingHours.Create(Guid.NewGuid(), 1, TimeSpan.Zero, TimeSpan.Zero, false);

        hours.OpenTime.Should().Be(TimeSpan.Zero);
        hours.CloseTime.Should().Be(TimeSpan.Zero);
        hours.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Create_with_valid_day_of_week_range_succeeds()
    {
        for (int day = 0; day <= 6; day++)
        {
            var hours = OperatingHours.Create(Guid.NewGuid(), day, TimeSpan.FromHours(9), TimeSpan.FromHours(17));

            hours.DayOfWeek.Should().Be(day);
        }
    }

    [Fact]
    public void Create_with_late_evening_close_time_succeeds()
    {
        var hours = OperatingHours.Create(Guid.NewGuid(), 5, TimeSpan.FromHours(18), TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)));

        hours.OpenTime.Should().Be(TimeSpan.FromHours(18));
        hours.CloseTime.Should().Be(TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)));
    }

    [Fact]
    public void Create_with_overnight_hours_works_with_next_day_close()
    {
        // Represents opening at 10 PM and closing at 2 AM next day
        var hours = OperatingHours.Create(Guid.NewGuid(), 5, TimeSpan.FromHours(22), TimeSpan.FromHours(26)); // 26 = 2 AM next day

        hours.OpenTime.Should().Be(TimeSpan.FromHours(22));
        hours.CloseTime.Should().Be(TimeSpan.FromHours(26));
    }

    [Fact]
    public void Create_with_same_open_and_close_time_is_valid()
    {
        var sameTime = TimeSpan.FromHours(12);
        var hours = OperatingHours.Create(Guid.NewGuid(), 3, sameTime, sameTime);

        hours.OpenTime.Should().Be(sameTime);
        hours.CloseTime.Should().Be(sameTime);
    }

    [Fact]
    public void Create_preserves_all_required_properties()
    {
        var merchantId = Guid.NewGuid();
        var dayOfWeek = 4; // Thursday
        var openTime = TimeSpan.FromHours(8).Add(TimeSpan.FromMinutes(30));
        var closeTime = TimeSpan.FromHours(17).Add(TimeSpan.FromMinutes(15));

        var hours = OperatingHours.Create(merchantId, dayOfWeek, openTime, closeTime, false);

        hours.MerchantId.Should().Be(merchantId);
        hours.DayOfWeek.Should().Be(dayOfWeek);
        hours.OpenTime.Should().Be(openTime);
        hours.CloseTime.Should().Be(closeTime);
        hours.IsOpen.Should().BeFalse();
        hours.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_generates_unique_ids_for_different_instances()
    {
        var merchantId = Guid.NewGuid();

        var hours1 = OperatingHours.Create(merchantId, 1, TimeSpan.FromHours(9), TimeSpan.FromHours(17));
        var hours2 = OperatingHours.Create(merchantId, 2, TimeSpan.FromHours(9), TimeSpan.FromHours(17));

        hours1.Id.Should().NotBe(hours2.Id);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(100)]
    public void Create_with_invalid_day_of_week_should_be_handled_by_validation_layer(int invalidDay)
    {
        // Note: The domain entity doesn't enforce day validation - this is handled at the application layer
        // This test documents the expected behavior that the domain allows any int value
        var hours = OperatingHours.Create(Guid.NewGuid(), invalidDay, TimeSpan.FromHours(9), TimeSpan.FromHours(17));

        hours.DayOfWeek.Should().Be(invalidDay);
    }

    [Fact]
    public void Toggle_open_close_multiple_times_works_correctly()
    {
        var hours = OperatingHours.Create(Guid.NewGuid(), 1, TimeSpan.FromHours(9), TimeSpan.FromHours(17), true);

        hours.IsOpen.Should().BeTrue();

        hours.Close();
        hours.IsOpen.Should().BeFalse();

        hours.Open();
        hours.IsOpen.Should().BeTrue();

        hours.Close();
        hours.IsOpen.Should().BeFalse();
    }
}
