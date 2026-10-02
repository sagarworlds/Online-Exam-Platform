using Xunit.Sdk;

namespace ExamPlatform.ArchitectureTests;

/// <summary>
/// Positive controls for the layer rules. A "must not depend on X" rule passes when there is nothing
/// to find, so each control proves the machinery can actually see a dependency that exists: the
/// namespace prefixes line up with the real assemblies and the rule fails when it is violated.
/// </summary>
public class RuleControlTests
{
    /// <summary>xUnit theory data: every discovered module name.</summary>
    public static IEnumerable<object[]> Modules() => ModuleCatalog.ModuleNames();

    [Fact]
    public void Rule_FailsWhenAnAssemblyDependsOnAForbiddenNamespace()
    {
        // Identity.Application really calls Admin.Contracts.IAuditLogger. Forbidding exactly that
        // must therefore fail; if it passed, every "must not depend on" rule would be vacuous.
        var identity = ModuleCatalog.Get("Identity");
        var admin = ModuleCatalog.Get("Admin");

        Assert.Throws<TrueException>(() => DependencyRules.AssertNoDependency(
            identity.Layer("Application"),
            "Identity.Application",
            [admin.NamespaceOf("Contracts")]));
    }

    [Fact]
    public void Rule_FailsWhenTheHostDependsOnAnEndpointsProject()
    {
        // The Host composes the Identity module through its installer, so forbidding the Endpoints
        // namespace must fail; this also proves the Host assembly is the one being analysed.
        var identity = ModuleCatalog.Get("Identity");

        Assert.Throws<TrueException>(() => DependencyRules.AssertNoDependency(
            ModuleCatalog.HostAssembly,
            "The Host",
            [identity.NamespaceOf("Endpoints")]));
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void DomainDependsOnSharedKernelDomain(string moduleName)
    {
        var domain = ModuleCatalog.Get(moduleName).Layer("Domain");

        Assert.NotEmpty(DependencyRules.TypesDependingOn(domain, ["ExamPlatform.SharedKernel.Domain"]));
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void ApplicationDependsOnItsOwnDomain(string moduleName)
    {
        var module = ModuleCatalog.Get(moduleName);

        Assert.NotEmpty(DependencyRules.TypesDependingOn(module.Layer("Application"), [module.NamespaceOf("Domain")]));
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void InfrastructureDependsOnItsOwnApplication(string moduleName)
    {
        var module = ModuleCatalog.Get(moduleName);

        Assert.NotEmpty(DependencyRules.TypesDependingOn(module.Layer("Infrastructure"), [module.NamespaceOf("Application")]));
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void EndpointsDependOnTheirOwnApplication(string moduleName)
    {
        var module = ModuleCatalog.Get(moduleName);

        Assert.NotEmpty(DependencyRules.TypesDependingOn(module.Layer("Endpoints"), [module.NamespaceOf("Application")]));
    }

    [Fact]
    public void Namespaces_MatchWholeSegmentsOnly()
    {
        // "ExamPlatform.Modules.Exam" is the start of "ExamPlatform.Modules.ExamAuthoring" as a string.
        // The rules rely on NetArchTest matching whole namespace segments, so a future module named
        // "Exam" is not confused with ExamAuthoring, while a module's root namespace still covers all
        // of its layers. This pins both behaviours.
        var host = ModuleCatalog.HostAssembly;

        Assert.Empty(DependencyRules.TypesDependingOn(host, ["ExamPlatform.Modules.Exam"]));
        Assert.NotEmpty(DependencyRules.TypesDependingOn(host, [ModuleCatalog.NamespaceOf("ExamAuthoring")]));
        Assert.NotEmpty(DependencyRules.TypesDependingOn(host, [ModuleCatalog.NamespaceOf("ExamAuthoring", "Endpoints")]));
    }
}
