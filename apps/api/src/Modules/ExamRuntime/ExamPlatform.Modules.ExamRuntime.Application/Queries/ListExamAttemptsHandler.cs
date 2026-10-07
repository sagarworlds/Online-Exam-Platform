using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application.Queries;

/// <summary>Lists an exam's enrolled candidates with their attempts, for the staff who may grant extra attempts.</summary>
public sealed class ListExamAttemptsHandler(
    IExamCatalog catalog,
    IExamRoster roster,
    IAttemptRepository attempts,
    IExtraAttemptGrantRepository grants,
    IAccommodationRepository accommodations,
    Clock clock)
{
    /// <summary>Returns every enrolled candidate of the exam, by e-mail address.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    public async Task<ExamAttemptsDto> HandleAsync(Guid examId, CancellationToken cancellationToken)
    {
        var exam = await catalog.FindAsync(examId, cancellationToken) ?? throw new ExamNotFoundError();

        var enrolled = await roster.GetEnrolledCandidatesAsync(examId, cancellationToken);
        var attemptsByCandidate = (await attempts.ListForExamAsync(examId, cancellationToken))
            .GroupBy(a => a.CandidateId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var grantCounts = await grants.CountsForExamAsync(examId, cancellationToken);
        var accommodated = await accommodations.ListForExamAsync(examId, cancellationToken);
        var nowUtc = clock.UtcNow;

        var rows = enrolled
            .Select(candidate => ExamCandidateRows.For(
                exam,
                candidate,
                attemptsByCandidate.GetValueOrDefault(candidate.UserId) ?? [],
                grantCounts.GetValueOrDefault(candidate.UserId),
                nowUtc,
                accommodated.GetValueOrDefault(candidate.UserId)))
            .ToList();

        return new ExamAttemptsDto(exam.Id, exam.Name, ExamCandidateRows.IsWindowClosed(exam, nowUtc), exam.MaxAttempts, rows);
    }
}
