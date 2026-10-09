using ExamPlatform.Modules.QuestionBank.Application.Dtos;
using ExamPlatform.Modules.QuestionBank.Application.Ports;

namespace ExamPlatform.Modules.QuestionBank.Application.Queries;

/// <summary>Lists classes with the number of books under each, for the authoring screens.</summary>
public sealed class ListClassesHandler(IClassRepository classes)
{
    /// <summary>Returns the classes ordered by name.</summary>
    /// <param name="includeArchived">Whether archived classes are included.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<ClassDto>> HandleAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        var list = await classes.ListAsync(includeArchived, cancellationToken);
        var counts = await classes.CountBooksAsync(cancellationToken);
        return list.Select(c => c.ToDto(counts.GetValueOrDefault(c.Id))).ToList();
    }
}
