using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application.Queries;

/// <summary>
/// Shows staff an exam exactly as a candidate would be shown it, before or after it is published (FR-15). Nothing is stored: the
/// attempt is built in memory, handed to the same view builder a real attempt goes through, and dropped, so a preview cannot create an
/// attempt, use up anybody's allowance, appear in a list, or change a score.
/// </summary>
public sealed class PreviewExamHandler(IExamCatalog catalog, AttemptViewBuilder views, PaperDrawer paperDrawer, Clock clock)
{
    /// <summary>How long a preview runs when the exam has no time limit; it only sets the countdown the page shows.</summary>
    private static readonly TimeSpan UntimedPreview = TimeSpan.FromHours(24);

    /// <summary>Builds the candidate's view of the exam: its questions in the order and with the shuffling a candidate would get, and no answer key.</summary>
    /// <param name="examId">The exam, in any state.</param>
    /// <param name="previewerUserId">The signed-in staff member; the preview is theirs alone and is not kept.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="PaperCannotBeDrawnError">A draw rule of the exam finds fewer questions than it needs, which would also stop a real attempt starting.</exception>
    /// <exception cref="ExamContentUnavailableError">The exam's questions cannot be read.</exception>
    public async Task<AttemptDto> HandleAsync(Guid examId, Guid previewerUserId, CancellationToken cancellationToken)
    {
        var exam = await catalog.FindAsync(examId, cancellationToken) ?? throw new ExamNotFoundError();

        var nowUtc = clock.UtcNow;
        var length = exam.DurationSeconds is { } seconds and > 0 ? TimeSpan.FromSeconds(seconds) : UntimedPreview;
        var attempt = Attempt.Start(exam.Id, previewerUserId, 1, nowUtc, nowUtc + length);

        // A drawn paper is drawn for the preview too, so the author sees what a candidate would, and learns now, not on exam day,
        // that a rule cannot be filled.
        if (PaperDrawer.Draws(exam))
            attempt.SetPaper(await paperDrawer.DrawAsync(exam, cancellationToken));

        return await views.BuildAsync(attempt, exam.For(attempt), cancellationToken);
    }
}
