using System.Reflection;
using ExamPlatform.Modules.Admin.Domain;
using ExamPlatform.Modules.Consent.Domain;
using ExamPlatform.Modules.Identity.Domain;
using NetArchTest.Rules;

namespace ExamPlatform.ArchitectureTests;

/// <summary>
/// Enforces ADR 0001's rule that a module's Domain layer has no framework
/// dependencies and no dependency on any other module — the innermost, most
/// stable layer must stay that way regardless of what gets built around it.
/// </summary>
public class DomainLayerTests
{
    public static IEnumerable<object[]> DomainAssemblies =>
        [
            [typeof(User).Assembly, "Identity.Domain"],
            [typeof(ConsentRecord).Assembly, "Consent.Domain"],
            [typeof(AuditLog).Assembly, "Admin.Domain"],
        ];

    [Theory]
    [MemberData(nameof(DomainAssemblies))]
    public void Domain_ShouldNotDependOnEfCoreOrAspNetCore(Assembly assembly, string moduleName)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
            .GetResult();

        Assert.True(result.IsSuccessful, $"{moduleName}: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Theory]
    [MemberData(nameof(DomainAssemblies))]
    public void Domain_ShouldNotDependOnOtherModules(Assembly assembly, string moduleName)
    {
        var otherModuleNamespaces = new[] { "ExamPlatform.Modules.Identity", "ExamPlatform.Modules.Consent", "ExamPlatform.Modules.Admin" }
            .Where(ns => !moduleName.StartsWith(ns.Split('.')[^1], StringComparison.Ordinal))
            .ToArray();

        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(otherModuleNamespaces)
            .GetResult();

        Assert.True(result.IsSuccessful, $"{moduleName}: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
