namespace ExamPlatform.ArchitectureTests;

/// <summary>
/// Enforces ADR 0001's rule that a module's Application layer may depend on
/// another module only through its <c>Contracts</c> project — never its
/// Domain, Application, Infrastructure or Endpoints directly. Runs once per
/// discovered module.
/// </summary>
public class ApplicationLayerTests
{
    /// <summary>xUnit theory data: every discovered module name.</summary>
    public static IEnumerable<object[]> Modules() => ModuleCatalog.ModuleNames();

    [Theory]
    [MemberData(nameof(Modules))]
    public void Application_ShouldReachOtherModulesOnlyThroughContracts(string moduleName)
    {
        var module = ModuleCatalog.Get(moduleName);

        // Every layer of another module except Contracts, plus the module's own outer layers.
        var disallowed = ModuleCatalog.Others(moduleName)
            .SelectMany(other => ModuleCatalog.KnownLayers
                .Where(layer => layer != "Contracts")
                .Select(other.NamespaceOf))
            .Concat(new[] { "Infrastructure", "Endpoints" }.Select(module.NamespaceOf));

        DependencyRules.AssertNoDependency(module.Layer("Application"), $"{moduleName}.Application", disallowed);
    }

    [Fact]
    public void AdminApplication_ShouldNotDependOnAnyOtherModuleAtAll()
    {
        // Admin is the audit sink every other module calls into. It depends on nothing from any
        // other module — not even their Contracts — since nothing in it needs to call out.
        var admin = ModuleCatalog.Get("Admin");

        DependencyRules.AssertNoDependency(
            admin.Layer("Application"),
            "Admin.Application",
            ModuleCatalog.Others("Admin").Select(m => m.Namespace));
    }

    [Fact]
    public void IdentityApplication_MayDependOnAdminContracts()
    {
        // Positive control: proves the negative-assertion tests above aren't vacuously
        // true because the reference doesn't exist — Identity.Application does call
        // Admin.Contracts.IAuditLogger (see AssignRoleHandler), and must be allowed to.
        var identity = ModuleCatalog.Get("Identity");
        var admin = ModuleCatalog.Get("Admin");

        var dependents = DependencyRules.TypesDependingOn(
            identity.Layer("Application"),
            [admin.NamespaceOf("Contracts")]);

        Assert.NotEmpty(dependents);
    }
}
