using System.Reflection;

namespace ExamPlatform.ArchitectureTests;

/// <summary>
/// Dependencies that ADR 0001 rejected must not creep back in. MediatR's licensing change makes it
/// a cost for a project of this lifetime; modules dispatch to plain <c>HandleAsync</c> handlers and
/// events go through the hand-rolled <c>IDomainEventDispatcher</c>.
/// </summary>
public class BannedDependencyTests
{
    private const string MediatR = "MediatR";

    /// <summary>xUnit theory data: every discovered module name.</summary>
    public static IEnumerable<object[]> Modules() => ModuleCatalog.ModuleNames();

    [Theory]
    [MemberData(nameof(Modules))]
    public void NoAssembly_DependsOnMediatR(string moduleName)
    {
        var module = ModuleCatalog.Get(moduleName);

        foreach (var (layer, assembly) in module.Layers)
            AssertDoesNotUse(assembly, $"{moduleName}.{layer}", MediatR);
    }

    [Fact]
    public void Host_DoesNotDependOnMediatR() =>
        AssertDoesNotUse(ModuleCatalog.HostAssembly, "The Host", MediatR);

    [Fact]
    public void DependencyGraph_DoesNotContainMediatR()
    {
        // The deps.json of the test project lists every package that any referenced project pulls
        // in, so a package reference that no code uses (and so leaves no trace in the IL) is still
        // caught here.
        var depsJson = ReadTestDependencyGraph();

        Assert.DoesNotContain("\"MediatR", depsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Control_DependencyGraphListsPackagesThatAreInUse()
    {
        // Positive control: proves the deps.json check above reads a real, populated graph.
        var depsJson = ReadTestDependencyGraph();

        Assert.Contains("Microsoft.EntityFrameworkCore", depsJson, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Control_InfrastructureUsesEfCore_SoTheBanCheckIsNotVacuous(string moduleName)
    {
        // Positive control: the same detection finds a dependency that really exists.
        var infrastructure = ModuleCatalog.Get(moduleName).Layer("Infrastructure");

        Assert.NotEmpty(DependencyRules.TypesDependingOn(infrastructure, ["Microsoft.EntityFrameworkCore"]));
    }

    private static void AssertDoesNotUse(Assembly assembly, string description, string package)
    {
        var referenced = assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name.StartsWith(package, StringComparison.Ordinal))
            .ToArray();

        Assert.True(referenced.Length == 0, $"{description} references {string.Join(", ", referenced)}.");

        DependencyRules.AssertNoDependency(assembly, description, [package]);
    }

    private static string ReadTestDependencyGraph()
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"{typeof(BannedDependencyTests).Assembly.GetName().Name}.deps.json");
        Assert.True(File.Exists(path), $"Expected the dependency graph at {path}.");
        return File.ReadAllText(path);
    }
}
