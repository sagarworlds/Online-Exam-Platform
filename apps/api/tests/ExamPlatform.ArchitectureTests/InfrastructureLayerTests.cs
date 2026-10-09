namespace ExamPlatform.ArchitectureTests;

/// <summary>
/// Enforces ADR 0001's rule that a module's Infrastructure layer never depends
/// on another module at all — not even via that module's <c>Contracts</c>
/// project, since calling another module is an Application-layer concern.
/// Runs once per discovered module.
/// </summary>
public class InfrastructureLayerTests
{
    /// <summary>xUnit theory data: every discovered module name.</summary>
    public static IEnumerable<object[]> Modules() => ModuleCatalog.ModuleNames();

    [Theory]
    [MemberData(nameof(Modules))]
    public void Infrastructure_ShouldNotDependOnOtherModules(string moduleName)
    {
        var module = ModuleCatalog.Get(moduleName);

        DependencyRules.AssertNoDependency(
            module.Layer("Infrastructure"),
            $"{moduleName}.Infrastructure",
            ModuleCatalog.Others(moduleName).Select(m => m.Namespace));
    }
}
