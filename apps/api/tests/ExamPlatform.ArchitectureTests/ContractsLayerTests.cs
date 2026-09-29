using System.Reflection;
using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Consent.Contracts;
using NetArchTest.Rules;

namespace ExamPlatform.ArchitectureTests;

/// <summary>
/// Enforces ADR 0001's rule that a module's <c>Contracts</c> project stays
/// dependency-free (only <c>SharedKernel.Domain</c>), so any other module can
/// reference it cheaply — no EF Core, no ASP.NET Core, no other module, and not
/// even its own module's Domain/Application/Infrastructure.
/// </summary>
public class ContractsLayerTests
{
    public static IEnumerable<object[]> ContractsAssemblies =>
        [
            [typeof(IAuditLogger).Assembly, "Admin", "Admin.Contracts"],
            [typeof(IConsentService).Assembly, "Consent", "Consent.Contracts"],
        ];

    [Theory]
    [MemberData(nameof(ContractsAssemblies))]
    public void Contracts_ShouldNotDependOnFrameworksOrOtherProjects(Assembly assembly, string ownModule, string moduleName)
    {
        var otherModules = new[] { "Identity", "Consent", "Admin" }.Where(m => m != ownModule);

        var disallowed = new List<string> { "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore" };
        disallowed.AddRange(otherModules.Select(m => $"ExamPlatform.Modules.{m}"));
        disallowed.Add($"ExamPlatform.Modules.{ownModule}.Domain");
        disallowed.Add($"ExamPlatform.Modules.{ownModule}.Application");
        disallowed.Add($"ExamPlatform.Modules.{ownModule}.Infrastructure");

        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(disallowed.Distinct().ToArray())
            .GetResult();

        Assert.True(result.IsSuccessful, $"{moduleName}: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
