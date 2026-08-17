using FluentAssertions;
using FluentValidation.TestHelper;
using Haworks.Media.Api.Application;
using Haworks.Media.Api.Domain;
using Haworks.Media.Api.Options;
using Microsoft.Extensions.Options;
using Xunit;

namespace Haworks.Media.Unit;

public class ValidatorTests
{
    private readonly InitiateUploadValidator _initiateValidator = new(Options.Create(new UploadOptions()));
    private readonly ProcessVirusScanValidator _scanValidator = new();

    [Fact]
    public void InitiateUploadValidator_EmptyFileName_ShouldHaveError()
    {
        var command = new InitiateUploadCommand("", new string('a', 64), 1024, "image/png");
        var result = _initiateValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.FileName);
    }

    [Fact]
    public void InitiateUploadValidator_InvalidHashLength_ShouldHaveError()
    {
        var command = new InitiateUploadCommand("test.png", "short", 1024, "image/png");
        var result = _initiateValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Hash);
    }

    [Fact]
    public void ProcessVirusScanValidator_EmptyId_ShouldHaveError()
    {
        var command = new ProcessVirusScanCommand(Guid.Empty);
        var result = _scanValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.MediaId);
    }
}

public class GetMediaUrlValidatorTests
{
    private readonly GetMediaUrlValidator _validator = new();

    [Fact]
    public void Variant_PathTraversal_ShouldHaveError()
    {
        var query = new GetMediaUrlQuery(Guid.NewGuid(), "../etc/passwd");
        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Variant)
            .WithErrorMessage("Path traversal is not allowed.");
    }

    [Fact]
    public void Variant_InvalidCharacters_ShouldHaveError()
    {
        var query = new GetMediaUrlQuery(Guid.NewGuid(), "bad<>char");
        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Variant)
            .WithErrorMessage("Variant contains invalid characters.");
    }

    [Fact]
    public void Variant_ValidValue_ShouldNotHaveError()
    {
        var query = new GetMediaUrlQuery(Guid.NewGuid(), "thumb-300.webp");
        var result = _validator.TestValidate(query);
        result.ShouldNotHaveValidationErrorFor(x => x.Variant);
    }

    [Fact]
    public void Variant_Null_ShouldNotHaveError()
    {
        var query = new GetMediaUrlQuery(Guid.NewGuid(), null);
        var result = _validator.TestValidate(query);
        result.ShouldNotHaveValidationErrorFor(x => x.Variant);
    }

    [Fact]
    public void MediaId_Empty_ShouldHaveError()
    {
        var query = new GetMediaUrlQuery(Guid.Empty);
        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.MediaId);
    }
}

public class MediaFileStateTransitionTests
{
    [Fact]
    public void MarkAsQuarantined_From_Pending_Succeeds()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "owner");
        file.MarkAsQuarantined();
        file.Status.Should().Be(MediaStatus.Quarantined);
    }

    [Fact]
    public void MarkAsQuarantined_From_Active_Throws()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "owner");
        file.MarkAsQuarantined();
        file.MarkAsActive();

        var act = () => file.MarkAsQuarantined();
        act.Should().Throw<InvalidOperationException>().WithMessage("*Cannot quarantine from Active*");
    }

    [Fact]
    public void MarkAsActive_From_Pending_Throws()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "owner");

        var act = () => file.MarkAsActive();
        act.Should().Throw<InvalidOperationException>().WithMessage("*Cannot activate from Pending*");
    }

    [Fact]
    public void MarkAsActive_From_Quarantined_Succeeds()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "owner");
        file.MarkAsQuarantined();
        file.MarkAsActive();
        file.Status.Should().Be(MediaStatus.Active);
    }

    [Fact]
    public void MarkAsRejected_From_Pending_Throws()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "owner");

        var act = () => file.MarkAsRejected();
        act.Should().Throw<InvalidOperationException>().WithMessage("*Cannot reject from Pending*");
    }

    [Fact]
    public void MarkAsRejected_From_Quarantined_Succeeds()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "owner");
        file.MarkAsQuarantined();
        file.MarkAsRejected();
        file.Status.Should().Be(MediaStatus.Rejected);
    }

    [Fact]
    public void MarkAsActive_From_Rejected_Throws()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "owner");
        file.MarkAsQuarantined();
        file.MarkAsRejected();

        var act = () => file.MarkAsActive();
        act.Should().Throw<InvalidOperationException>().WithMessage("*Cannot activate from Rejected*");
    }

    [Fact]
    public void InitiateMultipart_From_Pending_Succeeds()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "owner");
        file.InitiateMultipart("upload-1", 5);
        file.UploadKind.Should().Be(UploadKind.Multipart);
        file.S3UploadId.Should().Be("upload-1");
        file.PartCount.Should().Be(5);
    }

    [Fact]
    public void InitiateMultipart_From_Quarantined_Throws()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "owner");
        file.MarkAsQuarantined();

        var act = () => file.InitiateMultipart("upload-1", 5);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Cannot initiate multipart*");
    }

    [Fact]
    public void InitiateMultipart_NonPositivePartCount_Throws()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "owner");
        var act = () => file.InitiateMultipart("upload-1", 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetEntityLink_ValidValues_SetsEntityIdAndType()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "owner");
        var entityId = Guid.NewGuid();
        file.SetEntityLink(entityId, "Product");
        file.EntityId.Should().Be(entityId);
        file.EntityType.Should().Be("Product");
    }

    [Fact]
    public void SetEntityLink_EmptyEntityType_Throws()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "owner");
        var act = () => file.SetEntityLink(Guid.NewGuid(), "  ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetSlug_ValidValue_SetsSlug()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "owner");
        file.SetSlug("my-slug");
        file.Slug.Should().Be("my-slug");
    }

    [Fact]
    public void SetSlug_EmptyValue_Throws()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "owner");
        var act = () => file.SetSlug("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetValidated_ValidValues_UpdatesEtagHashAndActivatesFile()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "owner");
        file.SetValidated("etag-1", new string('c', 64));
        file.ETag.Should().Be("etag-1");
        file.Hash.Should().Be(new string('c', 64));
        file.Status.Should().Be(MediaStatus.Active);
        file.ValidatedAt.Should().NotBeNull();
    }

    [Fact]
    public void SetValidated_EmptyEtag_Throws()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "owner");
        var act = () => file.SetValidated("", new string('c', 64));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarkAsFailed_ValidReason_SetsFailureReasonAndStatus()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "owner");
        file.MarkAsFailed("processing error");
        file.FailureReason.Should().Be("processing error");
        file.Status.Should().Be(MediaStatus.Failed);
    }

    [Fact]
    public void MarkAsFailed_EmptyReason_Throws()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "owner");
        var act = () => file.MarkAsFailed("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarkAsDeleted_SetsIsDeletedDeletedAtAndStatus()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "owner");
        file.MarkAsDeleted(TimeProvider.System);
        file.IsDeleted.Should().BeTrue();
        file.Status.Should().Be(MediaStatus.Deleted);
        file.DeletedAt.Should().NotBeNull();
        file.DeletedAt!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void MarkDeleted_Alias_BehavesLikeMarkAsDeleted()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "owner");
        file.MarkDeleted(TimeProvider.System);
        file.IsDeleted.Should().BeTrue();
        file.Status.Should().Be(MediaStatus.Deleted);
        file.DeletedAt.Should().NotBeNull();
    }
}
