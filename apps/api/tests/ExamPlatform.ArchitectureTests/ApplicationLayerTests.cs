using ExamPlatform.Modules.Admin.Application;
using ExamPlatform.Modules.Consent.Application;
using ExamPlatform.Modules.Identity.Application;
using NetArchTest.Rules;

namespace ExamPlatform.ArchitectureTests;

/// <summary>
/// Enforces ADR 0001's rule that a module's Application layer may depend on
/// another module only through its <c>Contracts</c> project — never its
/// Domain, Application, or Infrastructure directly.
/// </summary>
public class ApplicationLayerTests
{
    [Fact]
    public void IdentityApplication_ShouldNotDependOnConsentOrAdminInternals()
    {
        var result = Types.InAssembly(typeof(OtpChallengeIssuer).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "ExamPlatform.Modules.Consent.Domain",
                "ExamPlatform.Modules.Consent.Application",
                "ExamPlatform.Modules.Consent.Infrastructure",
                "ExamPlatform.Modules.Admin.Domain",
                "ExamPlatform.Modules.Admin.Application",
                "ExamPlatform.Modules.Admin.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void ConsentApplication_ShouldNotDependOnIdentityOrAdminInternals()
    {
        var result = Types.InAssembly(typeof(ConsentService).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "ExamPlatform.Modules.Identity.Domain",
                "ExamPlatform.Modules.Identity.Application",
                "ExamPlatform.Modules.Identity.Infrastructure",
                "ExamPlatform.Modules.Admin.Domain",
                "ExamPlatform.Modules.Admin.Application",
                "ExamPlatform.Modules.Admin.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void AdminApplication_ShouldNotDependOnIdentityOrConsentAtAll()
    {
        // Admin depends on nothing from Identity or Consent — not even their Contracts —
        // since nothing in this slice needs to call from Admin into either of them.
        var result = Types.InAssembly(typeof(AuditLogger).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "ExamPlatform.Modules.Identity",
                "ExamPlatform.Modules.Consent")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void IdentityApplication_MayDependOnAdminContracts()
    {
        // Positive control: proves the negative-assertion tests above aren't vacuously
        // true because the reference doesn't exist — Identity.Application does call
        // Admin.Contracts.IAuditLogger (see AssignRoleHandler), and must be allowed to.
        var result = Types.InAssembly(typeof(OtpChallengeIssuer).Assembly)
            .That().HaveDependencyOn("ExamPlatform.Modules.Admin.Contracts")
            .GetTypes();

        Assert.NotEmpty(result);
    }
}
