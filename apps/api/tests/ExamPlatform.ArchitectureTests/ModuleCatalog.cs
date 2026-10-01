using System.Reflection;
using System.Runtime.Loader;
using NetArchTest.Rules;

namespace ExamPlatform.ArchitectureTests;

/// <summary>
/// One business module together with the layer assemblies discovered for it
/// (<c>ExamPlatform.Modules.{Name}.{Layer}</c>).
/// </summary>
/// <param name="Name">The module name, e.g. <c>Batch</c>.</param>
/// <param name="Layers">The module's layer assemblies keyed by layer name, e.g. <c>Domain</c>.</param>
internal sealed record ModuleInfo(string Name, IReadOnlyDictionary<string, Assembly> Layers)
{
    /// <summary>Whether the module has a project for <paramref name="layer"/> (not every module has Contracts).</summary>
    public bool HasLayer(string layer) => Layers.ContainsKey(layer);

    /// <summary>The module's assembly for <paramref name="layer"/>.</summary>
    /// <exception cref="InvalidOperationException">The module has no such layer.</exception>
    public Assembly Layer(string layer) =>
        Layers.TryGetValue(layer, out var assembly)
            ? assembly
            : throw new InvalidOperationException($"Module '{Name}' has no {layer} assembly.");

    /// <summary>The root namespace of the module, e.g. <c>ExamPlatform.Modules.Batch</c>.</summary>
    public string Namespace => ModuleCatalog.NamespaceOf(Name);

    /// <summary>The namespace of one layer of the module, e.g. <c>ExamPlatform.Modules.Batch.Domain</c>.</summary>
    public string NamespaceOf(string layer) => ModuleCatalog.NamespaceOf(Name, layer);
}

/// <summary>
/// Discovers every module by assembly name instead of by a hand-maintained list, so a new module
/// is covered by the architecture rules the moment the Host composes it. The Host project
/// references every module's Endpoints project, which references everything below it, so every
/// <c>ExamPlatform.Modules.*.dll</c> is copied next to the test assembly.
/// </summary>
internal static class ModuleCatalog
{
    /// <summary>The layer names a module may consist of (ADR 0001).</summary>
    public static readonly IReadOnlyList<string> KnownLayers =
        ["Domain", "Contracts", "Application", "Infrastructure", "Endpoints"];

    /// <summary>The assembly name of the composition root.</summary>
    public const string HostAssemblyName = "ExamPlatform.Api";

    private const string ModuleAssemblyPrefix = "ExamPlatform.Modules.";

    private static readonly Lazy<IReadOnlyDictionary<string, ModuleInfo>> Discovered = new(Discover);

    /// <summary>Every discovered module, ordered by name.</summary>
    public static IReadOnlyList<ModuleInfo> Modules => Discovered.Value.Values.OrderBy(m => m.Name, StringComparer.Ordinal).ToList();

    /// <summary>Looks a module up by name.</summary>
    /// <exception cref="KeyNotFoundException">No module with that name was discovered.</exception>
    public static ModuleInfo Get(string name) => Discovered.Value[name];

    /// <summary>Every module except <paramref name="name"/>.</summary>
    public static IEnumerable<ModuleInfo> Others(string name) =>
        Modules.Where(m => !string.Equals(m.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// The namespace of a module, or of one of its layers. NetArchTest matches dependencies by whole
    /// namespace segments, so these carry no trailing dot, and a module named <c>Exam</c> could never
    /// be mistaken for <c>ExamAuthoring</c>.
    /// </summary>
    public static string NamespaceOf(string module, string? layer = null) =>
        layer is null
            ? $"{ModuleAssemblyPrefix}{module}"
            : $"{ModuleAssemblyPrefix}{module}.{layer}";

    /// <summary>xUnit theory data: the name of every module.</summary>
    public static IEnumerable<object[]> ModuleNames() =>
        Modules.Select(m => new object[] { m.Name });

    /// <summary>xUnit theory data: the name of every module that has a project for <paramref name="layer"/>.</summary>
    public static IEnumerable<object[]> ModuleNamesWithLayer(string layer) =>
        Modules.Where(m => m.HasLayer(layer)).Select(m => new object[] { m.Name });

    /// <summary>The Host (composition root) assembly.</summary>
    public static Assembly HostAssembly => LoadFromOutput(HostAssemblyName);

    /// <summary>Loads an assembly that was copied next to the test assembly.</summary>
    public static Assembly LoadFromOutput(string assemblyName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");
        return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }

    private static IReadOnlyDictionary<string, ModuleInfo> Discover()
    {
        var layersByModule = new Dictionary<string, Dictionary<string, Assembly>>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, ModuleAssemblyPrefix + "*.dll"))
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (fileName.EndsWith(".UnitTests", StringComparison.Ordinal))
                continue;

            // ExamPlatform.Modules.Batch.Domain -> module "Batch", layer "Domain".
            var rest = fileName[ModuleAssemblyPrefix.Length..];
            var split = rest.LastIndexOf('.');
            if (split <= 0)
                throw new InvalidOperationException($"'{fileName}' does not follow ExamPlatform.Modules.<Module>.<Layer>.");

            var module = rest[..split];
            var layer = rest[(split + 1)..];

            // An unknown layer must fail loudly: the rules below only know the ADR 0001 layers,
            // so a new kind of project has to be taken into account deliberately.
            if (!KnownLayers.Contains(layer))
                throw new InvalidOperationException(
                    $"'{fileName}' is not one of the ADR 0001 layers ({string.Join(", ", KnownLayers)}); extend the architecture rules.");

            if (!layersByModule.TryGetValue(module, out var layers))
                layersByModule[module] = layers = new Dictionary<string, Assembly>(StringComparer.Ordinal);

            layers[layer] = LoadFromOutput(fileName);
        }

        return layersByModule.ToDictionary(
            pair => pair.Key,
            pair => new ModuleInfo(pair.Key, pair.Value),
            StringComparer.Ordinal);
    }
}

/// <summary>Shared evaluation of "this assembly must not depend on these namespaces" rules.</summary>
internal static class DependencyRules
{
    /// <summary>
    /// Names the types of <paramref name="assembly"/> that depend on any of the namespaces.
    /// A namespace matches itself and its sub-namespaces, segment by segment (see <see cref="ModuleCatalog.NamespaceOf"/>).
    /// </summary>
    public static IReadOnlyList<string> TypesDependingOn(Assembly assembly, IEnumerable<string> namespaces)
    {
        var forbidden = namespaces.Distinct(StringComparer.Ordinal).ToArray();
        if (forbidden.Length == 0)
            return [];

        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        if (result.IsSuccessful)
            return [];

        return result.FailingTypeNames?.ToArray() is { Length: > 0 } failing ? failing : ["<unnamed type>"];
    }

    /// <summary>Asserts that no type of <paramref name="assembly"/> depends on any of the namespaces.</summary>
    /// <param name="assembly">The assembly under test.</param>
    /// <param name="description">What the assembly is, shown in the failure message.</param>
    /// <param name="namespaces">The namespaces the assembly must not depend on.</param>
    public static void AssertNoDependency(Assembly assembly, string description, IEnumerable<string> namespaces)
    {
        var forbidden = namespaces.ToArray();
        var offenders = TypesDependingOn(assembly, forbidden);
        Assert.True(
            offenders.Count == 0,
            $"{description} must not depend on [{string.Join(", ", forbidden)}], but these types do: {string.Join(", ", offenders)}");
    }
}
