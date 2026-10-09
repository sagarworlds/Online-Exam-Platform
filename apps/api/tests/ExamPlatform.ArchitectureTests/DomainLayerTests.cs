namespace ExamPlatform.ArchitectureTests;

/// <summary>
/// Enforces ADR 0001's rule that a module's Domain layer has no framework
/// dependencies and no dependency on any other module — the innermost, most
/// stable layer must stay that way regardless of what gets built around it.
/// Runs once per discovered module.
/// </summary>
public class DomainLayerTests
{
    /// <summary>xUnit theory data: every discovered module name.</summary>
    public static IEnumerable<object[]> Modules() => ModuleCatalog.ModuleNames();

    [Theory]
    [MemberData(nameof(Modules))]
    public void Domain_ShouldNotDependOnFrameworks(string moduleName)
    {
        var module = ModuleCatalog.Get(moduleName);

        DependencyRules.AssertNoDependency(
            module.Layer("Domain"),
            $"{moduleName}.Domain",
            ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore"]);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Domain_ShouldNotDependOnOtherModules(string moduleName)
    {
        var module = ModuleCatalog.Get(moduleName);

        // Other modules entirely, and the module's own outer layers: Domain depends on
        // SharedKernel.Domain only.
        var disallowed = ModuleCatalog.Others(moduleName).Select(m => m.Namespace)
            .Concat(new[] { "Contracts", "Application", "Infrastructure", "Endpoints" }.Select(module.NamespaceOf));

        DependencyRules.AssertNoDependency(module.Layer("Domain"), $"{moduleName}.Domain", disallowed);
    }
}
