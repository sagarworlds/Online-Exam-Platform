using System.Reflection;
using ExamPlatform.Modules.Admin.Endpoints;
using ExamPlatform.Modules.Consent.Endpoints;
using ExamPlatform.Modules.Identity.Endpoints;
using NetArchTest.Rules;

namespace ExamPlatform.ArchitectureTests;

/// <summary>
/// Enforces ADR 0001's rules for the outermost layers: a module's
/// <c>Endpoints</c> project never depends on another module's <c>Endpoints</c>
/// (each module maps its own routes independently), and the Host never
/// references a module's Domain, Application, or Infrastructure directly —
/// only its <c>Endpoints</c> project, so it cannot bypass the boundary.
/// </summary>
public class EndpointsAndHostLayerTests
{
    public static IEnumerable<object[]> EndpointsAssemblies =>
        [
            [typeof(IdentityModuleInstaller).Assembly, "Identity", "Identity.Endpoints"],
            [typeof(ConsentModuleInstaller).Assembly, "Consent", "Consent.Endpoints"],
            [typeof(AdminModuleInstaller).Assembly, "Admin", "Admin.Endpoints"],
        ];

    [Theory]
    [MemberData(nameof(EndpointsAssemblies))]
    public void Endpoints_ShouldNotDependOnAnotherModulesEndpoints(Assembly assembly, string ownModule, string moduleName)
    {
        var otherEndpoints = new[] { "Identity", "Consent", "Admin" }
            .Where(m => m != ownModule)
            .Select(m => $"ExamPlatform.Modules.{m}.Endpoints")
            .ToArray();

        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(otherEndpoints)
            .GetResult();

        Assert.True(result.IsSuccessful, $"{moduleName}: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Host_ShouldNotDependOnAnyModulesDomainApplicationOrInfrastructureDirectly()
    {
        var disallowed = new[] { "Identity", "Consent", "Admin" }
            .SelectMany(m => new[]
            {
                $"ExamPlatform.Modules.{m}.Domain",
                $"ExamPlatform.Modules.{m}.Application",
                $"ExamPlatform.Modules.{m}.Infrastructure",
            })
            .ToArray();

        // The Host assembly is found by name (rather than a type reference) because
        // ArchitectureTests never references it directly for anything else — a direct
        // reference exists only via the test project's own dependency graph.
        var hostAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "ExamPlatform.Api")
            ?? Assembly.Load("ExamPlatform.Api");

        var result = Types.InAssembly(hostAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(disallowed)
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
