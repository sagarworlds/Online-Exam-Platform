namespace ExamPlatform.ArchitectureTests;

/// <summary>
/// Enforces ADR 0001's rule that a module's <c>Contracts</c> project stays
/// dependency-free (only <c>SharedKernel.Domain</c>), so any other module can
/// reference it cheaply — no EF Core, no ASP.NET Core, no other module, and not
/// even its own module's Domain/Application/Infrastructure/Endpoints. Runs once
/// per discovered module that has a Contracts project.
/// </summary>
public class ContractsLayerTests
{
    /// <summary>xUnit theory data: the modules that have a Contracts project.</summary>
    public static IEnumerable<object[]> ModulesWithContracts() => ModuleCatalog.ModuleNamesWithLayer("Contracts");

    [Theory]
    [MemberData(nameof(ModulesWithContracts))]
    public void Contracts_ShouldStayDependencyFree(string moduleName)
    {
        var module = ModuleCatalog.Get(moduleName);

        var disallowed = new[] { "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore" }
            .Concat(ModuleCatalog.Others(moduleName).Select(m => m.Namespace))
            .Concat(new[] { "Domain", "Application", "Infrastructure", "Endpoints" }.Select(module.NamespaceOf));

        DependencyRules.AssertNoDependency(module.Layer("Contracts"), $"{moduleName}.Contracts", disallowed);
    }

    [Fact]
    public void AtLeastOneModuleHasContracts()
    {
        // Guards the theory above against running zero cases.
        Assert.NotEmpty(ModuleCatalog.ModuleNamesWithLayer("Contracts"));
    }
}
