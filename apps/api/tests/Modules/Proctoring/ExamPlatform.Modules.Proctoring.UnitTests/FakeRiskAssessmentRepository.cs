using ExamPlatform.Modules.Proctoring.Application;
using ExamPlatform.Modules.Proctoring.Application.Ports;
using ExamPlatform.Modules.Proctoring.Domain;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Proctoring.UnitTests;

/// <summary>An in-memory <see cref="IRiskAssessmentRepository"/> that filters, orders and pages the way the database query does.</summary>
internal sealed class FakeRiskAssessmentRepository : IRiskAssessmentRepository
{
    public List<RiskAssessment> Stored { get; } = [];

    public void Add(RiskAssessment assessment) => Stored.Add(assessment);

    public Task<IReadOnlyList<RiskAssessment>> ListForExamAsync(Guid examId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RiskAssessment>>(Stored.Where(a => a.ExamId == examId).ToList());

    public Task<RiskAssessment?> GetByIdAsync(Guid assessmentId, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.FirstOrDefault(a => a.Id == assessmentId));

    public Task<RiskFlagPage> ListFlagsAsync(Guid examId, RiskFlagFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var matching = Stored.Where(a => a.ExamId == examId && a.Flagged && Matches(a, filter)).ToList();
        var ordered = matching
            .OrderByDescending(a => a.Score)
            .ThenBy(a => a.AttemptNumber)
            .ThenBy(a => a.AttemptId)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToList();

        return Task.FromResult(new RiskFlagPage(ordered, matching.Count));
    }

    public Task<bool> HasOpenFlagAsync(Guid attemptId, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.Any(a => a.AttemptId == attemptId && a.Flagged && a.Status == RiskFlagStatus.Open));

    private static bool Matches(RiskAssessment assessment, RiskFlagFilter filter) => filter switch
    {
        RiskFlagFilter.Open => assessment.Status == RiskFlagStatus.Open,
        RiskFlagFilter.Reviewed => assessment.Status == RiskFlagStatus.Reviewed,
        RiskFlagFilter.Dismissed => assessment.Status == RiskFlagStatus.Dismissed,
        _ => true,
    };
}
