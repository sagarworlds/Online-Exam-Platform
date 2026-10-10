using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.Modules.Proctoring.Application.Dtos;
using ExamPlatform.Modules.Proctoring.Application.Ports;
using ExamPlatform.Modules.Proctoring.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Proctoring.Application.Queries;

/// <summary>Reads one page of an exam's review queue, highest score first, with each candidate's address for the reviewer.</summary>
public sealed class ListRiskFlagsHandler(IExamCatalog catalog, IExamRoster roster, IRiskAssessmentRepository assessments)
{
    /// <summary>Reads the page.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="filter">Which decisions to show.</param>
    /// <param name="page">The page to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    public async Task<RiskFlagQueueDto> HandleAsync(Guid examId, RiskFlagFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var exam = await catalog.FindAsync(examId, cancellationToken) ?? throw new ExamNotFoundError(examId);
        var flags = await assessments.ListFlagsAsync(examId, filter, page, cancellationToken);

        // Addresses come from the roster once per page, not once per row. A candidate who is no longer on the roster is shown without one.
        var emails = (await roster.GetEnrolledCandidatesAsync(examId, cancellationToken))
            .ToDictionary(c => c.UserId, c => c.Email);
        var items = flags.Items
            .Select(flag => RiskFlagMapper.ToFlagDto(flag, emails.TryGetValue(flag.CandidateId, out var email) ? email : null))
            .ToList();

        return new RiskFlagQueueDto(exam.Id, exam.Name, filter.ToString(), page.Page, page.PageSize, flags.Total, items);
    }
}
