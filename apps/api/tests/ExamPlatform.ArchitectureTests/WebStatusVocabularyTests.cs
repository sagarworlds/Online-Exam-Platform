using System.Text.RegularExpressions;

namespace ExamPlatform.ArchitectureTests;

/// <summary>
/// The names of a lifecycle's states are a contract between the API and the web app: the API sends an enum's member name, and the page
/// looks that name up in its own table of words and colours. Nothing ties the two lists together, so they can drift without any compiler
/// or test noticing: the page once knew a batch that had not been opened as <c>Draft</c> while the API sent <c>Pending</c>, so every new
/// batch showed an empty badge. These tests read each web union from its source file and compare it, name for name, with the enum the
/// API serialises (<c>JsonStringEnumConverter</c>, see <c>Program.cs</c>).
/// <para>
/// To add a lifecycle, add one row to <see cref="Pairs"/>. To rename or add a state, change the enum and the web union in the same change;
/// the failure message names both. <c>docs/state-machines.md</c> lists every lifecycle.
/// </para>
/// </summary>
public class WebStatusVocabularyTests
{
    // Where the web app keeps its models, from the repository root.
    private const string WebApp = "apps/web/src/app";

    // Module, layer and name of the API enum; the web file (under WebApp) and the name of the union in it. The question status is the one
    // lifecycle the page spells differently (in_review for InReview), because its words travel in a query string; it is compared with
    // the spelling stripped.
    private static readonly (string Module, string Layer, string Enum, string WebFile, string WebType, bool IgnoreSpelling)[] Rows =
    [
        ("Batch", "Domain", "BatchStatus", "batch-management/batch.models.ts", "BatchStatus", false),
        ("Batch", "Domain", "MemberRegistrationStatus", "batch-management/batch.models.ts", "MemberRegistrationStatus", false),
        ("ExamAuthoring", "Domain", "ExamStatus", "exam-authoring/exam.models.ts", "ExamStatus", false),
        ("ExamAuthoring", "Domain", "ResultReleaseMode", "exam-authoring/exam.models.ts", "ResultReleaseMode", false),
        ("ExamRuntime", "Domain", "AttemptStatus", "candidate/candidate.models.ts", "AttemptStatus", false),
        ("ExamRuntime", "Application", "Dtos.MyExamState", "candidate/candidate.models.ts", "MyExamState", false),
        ("ExamRuntime", "Domain", "AttemptRequestStatus", "candidate/candidate.models.ts", "AttemptRequestStatus", false),
        ("ExamRuntime", "Domain", "DisputeStatus", "candidate/candidate.models.ts", "DisputeStatus", false),
        ("ExamRuntime", "Domain", "IssueReportStatus", "attempt-management/attempt-admin.models.ts", "IssueReportStatus", false),
        ("ExamRuntime", "Application", "Dtos.AiCheckState", "attempt-management/attempt-admin.models.ts", "AiCheckState", false),
        ("Guardian", "Domain", "GuardianLinkStatus", "guardian-portal/guardian.models.ts", "GuardianLinkStatus", false),
        ("Invite", "Domain", "InviteStatus", "invite-management/invite.models.ts", "InviteStatus", false),
        ("Proctoring", "Domain", "RiskFlagStatus", "proctoring/proctoring.models.ts", "RiskFlagStatus", false),
        ("QuestionBank", "Domain", "QuestionStatus", "question-bank/question.models.ts", "QuestionStatus", true),
    ];

    /// <summary>xUnit theory data: the name of every lifecycle that is compared.</summary>
    public static IEnumerable<object[]> Pairs() => Rows.Select(r => new object[] { $"{r.Module}.{r.Enum}" });

    [Theory]
    [MemberData(nameof(Pairs))]
    public void TheWebApp_ShouldNameEveryStateTheApiSends_AndNoOther(string name)
    {
        var row = Rows.Single(r => $"{r.Module}.{r.Enum}" == name);
        var apiType = ModuleCatalog.Get(row.Module).Layer(row.Layer)
            .GetType($"{ModuleCatalog.NamespaceOf(row.Module, row.Layer)}.{row.Enum}", throwOnError: true)!;

        var api = Enum.GetNames(apiType).Select(n => Spell(n, row.IgnoreSpelling)).Order(StringComparer.Ordinal).ToArray();
        var web = ReadUnion(Path.Combine(RepositoryRoot(), WebApp, row.WebFile), row.WebType).Select(n => Spell(n, row.IgnoreSpelling)).Order(StringComparer.Ordinal).ToArray();

        var missingFromWeb = api.Except(web).ToArray();
        var unknownToApi = web.Except(api).ToArray();
        Assert.True(
            missingFromWeb.Length == 0 && unknownToApi.Length == 0,
            $"{row.WebFile} type {row.WebType} and the API enum {apiType.FullName} name different states. "
            + $"The API sends {string.Join(", ", missingFromWeb)} and the page does not know {(missingFromWeb.Length == 0 ? "of any" : "them")}; "
            + $"the page knows {string.Join(", ", unknownToApi)} and the API never sends {(unknownToApi.Length == 0 ? "any" : "them")}. "
            + "Change the enum and the union in the same change (see docs/state-machines.md).");
    }

    [Fact]
    public void EveryRow_ShouldBeUnique()
    {
        Assert.Equal(Rows.Length, Rows.Select(r => $"{r.Module}.{r.Enum}").Distinct().Count());
    }

    // The members of `export type Name = 'A' | 'B' ...;`, which may run over several lines.
    private static IReadOnlyList<string> ReadUnion(string file, string typeName)
    {
        Assert.True(File.Exists(file), $"The web model file {file} is not there; move it in WebStatusVocabularyTests as well.");

        var source = File.ReadAllText(file);
        var declaration = Regex.Match(source, @$"export type {Regex.Escape(typeName)}\s*=\s*(?<members>(?:\|?\s*'[^']*'\s*)+);", RegexOptions.CultureInvariant);
        Assert.True(declaration.Success, $"{file} has no `export type {typeName} = 'A' | 'B';`; keep the union literal so the contract can be read.");

        return Regex.Matches(declaration.Groups["members"].Value, "'([^']*)'", RegexOptions.CultureInvariant).Select(m => m.Groups[1].Value).ToArray();
    }

    private static string Spell(string name, bool ignoreSpelling) =>
        ignoreSpelling ? name.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant() : name;

    // The test runs from bin/..., so the repository root is the nearest folder above it that holds the web app.
    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, WebApp)))
                return dir.FullName;
        }

        throw new InvalidOperationException($"No folder above {AppContext.BaseDirectory} holds {WebApp}; the vocabulary contract needs the whole repository checked out.");
    }
}
