using System.Reflection;
using System.Runtime.CompilerServices;
using NetArchTest.Rules;

namespace ExamPlatform.ArchitectureTests;

/// <summary>
/// Guards the discovery itself: the layer rules iterate over <see cref="ModuleCatalog"/>, so if the
/// catalog silently came back empty or incomplete every rule would pass without checking anything.
/// </summary>
public class ModuleCatalogTests
{
    private static readonly string[] KnownModules =
        ["Identity", "Consent", "Admin", "ExamAuthoring", "Batch", "Invite", "Guardian", "QuestionBank", "ExamRuntime"];

    [Fact]
    public void DiscoversAtLeastTheKnownModules()
    {
        var discovered = ModuleCatalog.Modules.Select(m => m.Name).ToArray();

        foreach (var module in KnownModules)
            Assert.Contains(module, discovered);
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void EveryModule_HasDomainApplicationInfrastructureAndEndpointsLayers(string moduleName)
    {
        var module = ModuleCatalog.Get(moduleName);

        foreach (var layer in new[] { "Domain", "Application", "Infrastructure", "Endpoints" })
            Assert.True(module.HasLayer(layer), $"Module '{moduleName}' has no {layer} assembly.");
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void EveryLayer_ContainsTypesInItsOwnNamespace(string moduleName)
    {
        // The rules identify a layer by its namespace. A layer whose types sat in some other
        // namespace would be invisible to every rule that names it.
        var module = ModuleCatalog.Get(moduleName);

        foreach (var (layer, assembly) in module.Layers)
        {
            var own = Types.InAssembly(assembly)
                .That().ResideInNamespaceStartingWith(module.NamespaceOf(layer))
                .GetTypes()
                .ToArray();

            Assert.True(own.Length > 0, $"{moduleName}.{layer} has no type in {module.NamespaceOf(layer)}");

            var stray = assembly.GetTypes()
                .Where(t => t.Namespace is not null
                    && !t.Namespace.StartsWith(module.NamespaceOf(layer), StringComparison.Ordinal)
                    && !t.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
                    && !IsCompilerEmbedded(t))
                .Select(t => t.FullName)
                .ToArray();

            Assert.True(stray.Length == 0, $"{moduleName}.{layer} declares types outside {module.NamespaceOf(layer)}: {string.Join(", ", stray)}");
        }
    }

    [Fact]
    public void Host_ComposesEveryDiscoveredModule()
    {
        // A module that is found on disk but not referenced by the Host would be tested here yet
        // never run in production.
        var referenced = ModuleCatalog.HostAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var module in ModuleCatalog.Modules)
            Assert.Contains($"ExamPlatform.Modules.{module.Name}.Endpoints", referenced);
    }

    /// <summary>xUnit theory data: every discovered module name.</summary>
    public static IEnumerable<object[]> ModuleNames() => ModuleCatalog.ModuleNames();

    private static bool IsCompilerEmbedded(Type type) =>
        type.Namespace is { } ns
        && (ns.StartsWith("System.Runtime.CompilerServices", StringComparison.Ordinal)
            || ns.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal));
}
