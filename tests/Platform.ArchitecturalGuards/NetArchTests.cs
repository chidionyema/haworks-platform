using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Haworks.Platform.ArchitecturalGuards;

/// <summary>
/// Assembly-level architecture rules using NetArchTest (ArchUnit for .NET).
/// These validate dependency direction and layer isolation at the type level,
/// not just file-pattern scanning. Inspired by:
/// https://medium.com/@bnayae/proactive-architecture-guarding-b71c4a77a0ec
/// </summary>
public sealed class NetArchTests
{
    // ─── Clean Architecture: Domain must not depend on anything ───────

    [Fact]
    public void Payments_Domain_has_no_dependency_on_Application()
    {
        var result = Types.InAssembly(typeof(Haworks.Payments.Domain.Payment).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Haworks.Payments.Application")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Domain layer must not reference Application — dependency inversion violation");
    }

    [Fact]
    public void Payments_Domain_has_no_dependency_on_Infrastructure()
    {
        var result = Types.InAssembly(typeof(Haworks.Payments.Domain.Payment).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Haworks.Payments.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Domain layer must not reference Infrastructure");
    }

    [Fact]
    public void Payments_Application_has_no_dependency_on_Infrastructure()
    {
        var result = Types.InAssembly(typeof(Haworks.Payments.Application.DependencyInjection).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Haworks.Payments.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Application layer must not reference Infrastructure — use interfaces");
    }

    [Fact]
    public void Payouts_Domain_has_no_dependency_on_Application_or_Infrastructure()
    {
        var result = Types.InAssembly(typeof(Haworks.Payouts.Domain.Aggregates.Payout).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Haworks.Payouts.Application",
                "Haworks.Payouts.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Payouts Domain must not reference Application or Infrastructure");
    }

    // ─── Domain entities must be classes (not records) ────────────────

    [Fact]
    public void Domain_entities_are_classes_not_records()
    {
        var domainTypes = Types.InAssembly(typeof(Haworks.Payments.Domain.Payment).Assembly)
            .That()
            .HaveNameEndingWith("State")
            .Or()
            .Inherit(typeof(Haworks.BuildingBlocks.Persistence.AuditableEntity))
            .GetTypes();

        foreach (var type in domainTypes)
        {
            type.IsClass.Should().BeTrue($"{type.Name} must be a class, not a record/struct (EF change tracking)");
            // Records have a compiler-generated <Clone>$ method
            type.GetMethod("<Clone>$").Should().BeNull($"{type.Name} is a record — EF entities must be classes");
        }
    }

    // ─── No service-to-service direct references ─────────────────────

    [Fact]
    public void Payments_does_not_reference_Orders_or_Catalog_directly()
    {
        var result = Types.InAssembly(typeof(Haworks.Payments.Domain.Payment).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Haworks.Orders",
                "Haworks.Catalog",
                "Haworks.Content",
                "Haworks.Identity")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Payments must not directly reference sibling services — communicate via events");
    }

    // ─── Handlers/Consumers must be sealed ────────────────────────────

    [Fact]
    public void Consumers_and_handlers_should_be_sealed()
    {
        var types = Types.InAssembly(typeof(Haworks.Payments.Application.DependencyInjection).Assembly)
            .That()
            .HaveNameEndingWith("Handler")
            .Or()
            .HaveNameEndingWith("Consumer")
            .GetTypes();

        foreach (var type in types)
        {
            if (type.IsAbstract || type.IsInterface) continue;
            type.IsSealed.Should().BeTrue(
                $"{type.Name} should be sealed — prevents unintended inheritance and improves performance");
        }
    }

    // ─── Domain must not use MediatR directly (only contracts) ────────

    [Fact]
    public void Domain_does_not_reference_MediatR()
    {
        var result = Types.InAssembly(typeof(Haworks.Payments.Domain.Payment).Assembly)
            .ShouldNot()
            .HaveDependencyOn("MediatR")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Domain must not reference MediatR — it's an application concern");
    }

    // ─── BuildingBlocks must not reference any service ────────────────

    [Fact]
    public void BuildingBlocks_does_not_reference_any_service()
    {
        var result = Types.InAssembly(typeof(Haworks.BuildingBlocks.Common.BrandOptions).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Haworks.Payments",
                "Haworks.Payouts",
                "Haworks.Orders",
                "Haworks.Catalog",
                "Haworks.Identity",
                "Haworks.Notifications")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "BuildingBlocks is shared infrastructure — must not reference any service");
    }
}
