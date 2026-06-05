using FluentAssertions;
using Haworks.Shipping.Api.Controllers;
using Haworks.Shipping.Api.Domain;
using Xunit;

namespace Haworks.Shipping.Unit;

public class MapEasyPostStatusTests
{
    [Theory]
    [InlineData("delivered", ShipmentStatus.Delivered)]
    [InlineData("in_transit", ShipmentStatus.InTransit)]
    [InlineData("out_for_delivery", ShipmentStatus.OutForDelivery)]
    [InlineData("failure", ShipmentStatus.Exception)]
    [InlineData("error", ShipmentStatus.Exception)]
    [InlineData("cancelled", ShipmentStatus.Cancelled)]
    [InlineData("unknown_status", ShipmentStatus.InTransit)]
    [InlineData(null, ShipmentStatus.InTransit)]
    [InlineData("", ShipmentStatus.InTransit)]
    [InlineData("  ", ShipmentStatus.InTransit)]
    public void MapEasyPostStatus_VariousInputs_ReturnsCorrectStatus(string? input, ShipmentStatus expected)
    {
        // Act
        var result = ShipmentsController.MapEasyPostStatus(input);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("DELIVERED")]
    [InlineData("Delivered")]
    [InlineData("In_Transit")]
    [InlineData("OUT_FOR_DELIVERY")]
    public void MapEasyPostStatus_CaseSensitive_ReturnsInTransitForNonExactMatch(string input)
    {
        // Act - EasyPost API sends lowercase, so case mismatches should default to InTransit
        var result = ShipmentsController.MapEasyPostStatus(input);

        // Assert
        result.Should().Be(ShipmentStatus.InTransit);
    }
}