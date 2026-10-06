using System.Text;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.QuestionBank.Application.Queries;

/// <summary>
/// Exports questions matching a filter as CSV, Excel or JSON (FR-6); the CSV has exactly the shape <see cref="Commands.ImportQuestionsHandler"/>
/// reads, so a bank's own export is always a file it can re-import unchanged.
/// </summary>
public sealed class ExportQuestionsHandler(IQuestionRepository repository)
{
    /// <summary>The most questions one export returns; a bank larger than this needs a narrower filter to export it in parts.</summary>
    public const int MaxRows = 5000;

    /// <summary>Builds the CSV text for the questions that match the filter, newest first.</summary>
    /// <param name="filter">Which questions to include; none set means all, up to <see cref="MaxRows"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<string> HandleAsync(QuestionFilter filter, CancellationToken cancellationToken)
    {
        var file = await ExportAsync(filter, QuestionFileFormat.Csv, cancellationToken);
        return Encoding.UTF8.GetString(file.Bytes);
    }

    /// <summary>Builds a file in the chosen format for the questions that match the filter, newest first.</summary>
    /// <param name="filter">Which questions to include; none set means all, up to <see cref="MaxRows"/>.</param>
    /// <param name="format">The format to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ExportedQuestionFile> ExportAsync(QuestionFilter filter, QuestionFileFormat format, CancellationToken cancellationToken)
    {
        var questions = await repository.ListNewestAsync(filter, 0, MaxRows, cancellationToken);
        return QuestionFiles.Write(format, questions);
    }
}
