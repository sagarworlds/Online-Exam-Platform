using ExamPlatform.Modules.ExamAuthoring.Application.Dtos;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.ExamAuthoring.Application;

/// <summary>
/// Builds the DTOs that report an exam, naming the book and chapters its scope refers to. An exam holds only their ids
/// (the question bank owns what they mean), so every handler that reports an exam goes through here and the names
/// are looked up once, in one place, however many exams are being reported.
/// </summary>
public sealed class ExamDtoFactory(IBookCatalog catalog)
{
    /// <summary>Reports one exam without its sections.</summary>
    /// <param name="exam">The exam to report.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ExamDto> ToDtoAsync(Exam exam, CancellationToken cancellationToken) =>
        (await ToDtosAsync([exam], cancellationToken))[0];

    /// <summary>Reports several exams without their sections, looking up the scope names in one call.</summary>
    /// <param name="exams">The exams to report.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<ExamDto>> ToDtosAsync(IReadOnlyList<Exam> exams, CancellationToken cancellationToken)
    {
        var books = await LoadBooksAsync(exams, cancellationToken);
        return exams.Select(e => e.ToDto() with { Scope = ScopeOf(e, books) }).ToList();
    }

    /// <summary>Reports one exam with its sections and questions.</summary>
    /// <param name="exam">The exam to report.</param>
    /// <param name="questionTexts">Question text by bank id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ExamDto> ToDetailDtoAsync(Exam exam, IReadOnlyDictionary<Guid, string> questionTexts, CancellationToken cancellationToken)
    {
        var books = await LoadBooksAsync([exam], cancellationToken);
        return exam.ToDetailDto(questionTexts) with { Scope = ScopeOf(exam, books) };
    }

    private async Task<IReadOnlyDictionary<Guid, BookSnapshot>> LoadBooksAsync(IReadOnlyList<Exam> exams, CancellationToken cancellationToken)
    {
        var bookIds = exams.Select(e => e.Scope.BookId).OfType<Guid>().Distinct().ToList();
        return (await catalog.GetBooksAsync(bookIds, cancellationToken)).ToDictionary(b => b.Id);
    }

    private static ExamScopeDto ScopeOf(Exam exam, IReadOnlyDictionary<Guid, BookSnapshot> books)
    {
        var scope = exam.Scope;
        var book = scope.BookId is { } bookId ? books.GetValueOrDefault(bookId) : null;

        return new ExamScopeDto(
            scope.Type,
            scope.BookId,
            book?.Name,
            scope.ChapterIds
                .Select(id => new ExamScopeChapterDto(id, book?.Chapters.FirstOrDefault(c => c.Id == id)?.Title))
                .ToList(),
            book?.ClassName);
    }
}
