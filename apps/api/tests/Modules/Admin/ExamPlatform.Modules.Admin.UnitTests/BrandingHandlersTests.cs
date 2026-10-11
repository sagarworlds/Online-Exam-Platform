using ExamPlatform.Modules.Admin.Application;
using ExamPlatform.Modules.Admin.Application.Commands;
using ExamPlatform.Modules.Admin.Application.Dtos;
using ExamPlatform.Modules.Admin.Application.Ports;
using ExamPlatform.Modules.Admin.Application.Queries;
using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Admin.Domain;
using ExamPlatform.Modules.Admin.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.Admin.UnitTests;

public class BrandingHandlersTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid ActorId = Guid.NewGuid();

    private readonly IBrandingRepository repository = Substitute.For<IBrandingRepository>();
    private readonly IAdminUnitOfWork unitOfWork = Substitute.For<IAdminUnitOfWork>();
    private readonly IAuditLogger auditLogger = Substitute.For<IAuditLogger>();
    private readonly IRequestContext requestContext = Substitute.For<IRequestContext>();
    private readonly Clock clock = new FakeClock(Now);

    public BrandingHandlersTests()
    {
        requestContext.UserId.Returns(ActorId);
        requestContext.Role.Returns("ExamAdmin");
        repository.Start(Arg.Any<DateTime>()).Returns(callInfo => InstituteBranding.Create(callInfo.Arg<DateTime>()));
    }

    private BrandingAudit Audit() => new(auditLogger, requestContext);

    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    [Fact]
    public async Task GetBranding_WhenNoneWasSaved_ReturnsTheDefaultLook()
    {
        repository.FindSettingsAsync(Arg.Any<CancellationToken>()).Returns((BrandingDto?)null);

        var dto = await new GetBrandingHandler(repository).HandleAsync(CancellationToken.None);

        Assert.Equal(BrandingDto.Unset, dto);
        Assert.False(dto.HasLogo);
    }

    [Fact]
    public async Task GetBrandingLogo_WhenNoneIsSet_Throws()
    {
        repository.FindLogoAsync(Arg.Any<CancellationToken>()).Returns((BrandingLogoDto?)null);

        await Assert.ThrowsAsync<LogoNotFoundError>(() =>
            new GetBrandingLogoHandler(repository).HandleAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UpdateBranding_StartsTheRecordOnFirstUse_SavesAndAudits()
    {
        repository.FindForChangeAsync(Arg.Any<CancellationToken>()).Returns((InstituteBranding?)null);
        var handler = new UpdateBrandingHandler(repository, unitOfWork, Audit(), clock);

        var dto = await handler.HandleAsync(new UpdateBrandingCommand("Riverside Academy", "#1a56db"), CancellationToken.None);

        Assert.Equal("Riverside Academy", dto.InstituteName);
        Assert.Equal("#1A56DB", dto.PrimaryColour);
        repository.Received(1).Start(Now);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await auditLogger.Received(1).RecordAsync(
            Arg.Is<AuditEntry>(e =>
                e.Action == "Branding.Updated"
                && e.ActorUserId == ActorId
                && e.ActorRole == "ExamAdmin"
                && e.Metadata["instituteName"] == "Riverside Academy"
                && e.Metadata["primaryColour"] == "#1A56DB"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateBranding_WithAnInvalidColour_SavesAndAuditsNothing()
    {
        repository.FindForChangeAsync(Arg.Any<CancellationToken>()).Returns(InstituteBranding.Create(Now));
        var handler = new UpdateBrandingHandler(repository, unitOfWork, Audit(), clock);

        await Assert.ThrowsAsync<InvalidBrandingError>(() =>
            handler.HandleAsync(new UpdateBrandingCommand("Riverside", "red"), CancellationToken.None));

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await auditLogger.DidNotReceive().RecordAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetLogo_StoresTheImage_SavesAndAudits()
    {
        repository.FindForChangeAsync(Arg.Any<CancellationToken>()).Returns(InstituteBranding.Create(Now));
        var handler = new SetBrandingLogoHandler(repository, unitOfWork, Audit(), clock);

        var dto = await handler.HandleAsync(new SetBrandingLogoCommand(Png()), CancellationToken.None);

        Assert.True(dto.HasLogo);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await auditLogger.Received(1).RecordAsync(
            Arg.Is<AuditEntry>(e => e.Action == "Branding.LogoChanged" && e.Metadata["contentType"] == "image/png"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetLogo_WithAnUnsupportedFile_SavesAndAuditsNothing()
    {
        repository.FindForChangeAsync(Arg.Any<CancellationToken>()).Returns(InstituteBranding.Create(Now));
        var handler = new SetBrandingLogoHandler(repository, unitOfWork, Audit(), clock);

        await Assert.ThrowsAsync<UnsupportedLogoTypeError>(() =>
            handler.HandleAsync(new SetBrandingLogoCommand("GIF89a"u8.ToArray()), CancellationToken.None));

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveLogo_WhenNoBrandingExists_ReturnsTheDefaultLookAndSavesNothing()
    {
        repository.FindForChangeAsync(Arg.Any<CancellationToken>()).Returns((InstituteBranding?)null);
        var handler = new RemoveBrandingLogoHandler(repository, unitOfWork, Audit(), clock);

        var dto = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(BrandingDto.Unset, dto);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveLogo_WithALogo_RemovesItSavesAndAudits()
    {
        var branding = InstituteBranding.Create(Now);
        branding.SetLogo(Png(), Now);
        repository.FindForChangeAsync(Arg.Any<CancellationToken>()).Returns(branding);
        var handler = new RemoveBrandingLogoHandler(repository, unitOfWork, Audit(), clock);

        var dto = await handler.HandleAsync(CancellationToken.None);

        Assert.False(dto.HasLogo);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await auditLogger.Received(1).RecordAsync(
            Arg.Is<AuditEntry>(e => e.Action == "Branding.LogoRemoved"),
            Arg.Any<CancellationToken>());
    }
}
