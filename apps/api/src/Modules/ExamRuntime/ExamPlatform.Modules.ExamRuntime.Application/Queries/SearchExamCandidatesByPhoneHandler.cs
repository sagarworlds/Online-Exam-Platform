using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.Identity.Contracts;
using ExamPlatform.Modules.Invite.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.Application.Queries;

/// <summary>
/// Finds the candidates of an exam by part of their phone number, for the staff who work with the exam's candidates. It answers with the
/// candidates' ids and nothing about the numbers, so the page that lists the candidates can be searched by number without ever holding one.
/// </summary>
public sealed class SearchExamCandidatesByPhoneHandler(IExamCatalog catalog, IExamRoster roster, IAccountPhoneSearch phones)
{
    /// <summary>Returns the ids of the exam's enrolled candidates whose phone number matches what was typed.</summary>
    /// <remarks>
    /// Only candidates enrolled in this exam are looked through, so the search cannot be used to find out who holds a number among accounts
    /// that have nothing to do with it. Neither the search nor any number is logged.
    /// </remarks>
    /// <param name="examId">The exam.</param>
    /// <param name="phone">What was typed, as it was typed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    public async Task<ExamCandidateMatchesDto> HandleAsync(Guid examId, string? phone, CancellationToken cancellationToken)
    {
        _ = await catalog.FindAsync(examId, cancellationToken) ?? throw new ExamNotFoundError();

        if (string.IsNullOrWhiteSpace(phone))
        {
            return new ExamCandidateMatchesDto([]);
        }

        var enrolled = await roster.GetEnrolledCandidatesAsync(examId, cancellationToken);
        if (enrolled.Count == 0)
        {
            return new ExamCandidateMatchesDto([]);
        }

        var matches = await phones.FindAccountsByPhoneAsync(enrolled.Select(candidate => candidate.UserId).ToList(), phone, cancellationToken);

        // In the order of the roster, which is by address, so the answer is the same each time.
        return new ExamCandidateMatchesDto(enrolled.Where(candidate => matches.Contains(candidate.UserId)).Select(candidate => candidate.UserId).ToList());
    }
}
