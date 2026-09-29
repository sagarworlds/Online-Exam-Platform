using System.Reflection;
using ExamPlatform.Modules.Admin.Infrastructure;
using ExamPlatform.Modules.Consent.Infrastructure;
using ExamPlatform.Modules.Identity.Infrastructure;
using NetArchTest.Rules;

namespace ExamPlatform.ArchitectureTests;

/// <summary>
/// Enforces ADR 0001's rule that a module's Infrastructure layer never depends
/// on another module at all — not even via that module's <c>Contracts</c>
/// project, since calling another module is an Application-layer concern.
/// </summary>
public class InfrastructureLayerTests
{
    public static IEnumerable<object[]> InfrastructureAssemblies =>
        [
            [typeof(IdentityDbContext).Assembly, "Identity", "Identity.Infrastructure"],
            [typeof(ConsentDbContext).Assembly, "Consent", "Consent.Infrastructure"],
            [typeof(AdminDbContext).Assembly, "Admin", "Admin.Infrastructure"],
        ];

    [Theory]
    [MemberData(nameof(InfrastructureAssemblies))]
    public void Infrastructure_ShouldNotDependOnOtherModules(Assembly assembly, string ownModule, string moduleName)
    {
        var otherModules = new[] { "Identity", "Consent", "Admin" }
            .Where(m => m != ownModule)
            .Select(m => $"ExamPlatform.Modules.{m}")
            .ToArray();

        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(otherModules)
            .GetResult();

        Assert.True(result.IsSuccessful, $"{moduleName}: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
