using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// Makes up a candidate's paper when they start: each section's fixed questions, then the questions its draw rules pick for this
/// candidate alone. Two candidates (and two attempts of one candidate) get independent draws.
/// </summary>
public sealed class PaperDrawer(IQuestionBank questionBank, IQuestionPicker picker)
{
    /// <summary>Whether the exam draws anything, so the paper must be stored rather than read from the exam.</summary>
    /// <param name="exam">The exam.</param>
    public static bool Draws(ExamSnapshot exam) => exam.Sections.Any(s => s.DrawRules is { Count: > 0 });

    /// <summary>Draws the paper.</summary>
    /// <remarks>
    /// A drawn question is never one that already appears elsewhere on the paper, fixed or drawn: the fixed questions always
    /// appear, and one question shown twice would share a single answer between two places.
    /// </remarks>
    /// <param name="exam">The exam, with its draw rules.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every question of the paper with its section, in section order and, within a section, fixed questions first.</returns>
    /// <exception cref="PaperCannotBeDrawnError">A rule finds fewer questions than it draws, so the attempt cannot start.</exception>
    public async Task<IReadOnlyList<(Guid SectionId, Guid QuestionId)>> DrawAsync(ExamSnapshot exam, CancellationToken cancellationToken)
    {
        var taken = exam.Sections.SelectMany(s => s.QuestionIds).ToHashSet();
        var paper = new List<(Guid, Guid)>();

        foreach (var section in exam.Sections.OrderBy(s => s.Order))
        {
            paper.AddRange(section.QuestionIds.Select(id => (section.Id, id)));

            foreach (var rule in section.DrawRules ?? [])
            {
                var matches = await questionBank.FindAsync(
                    new QuestionCriteria(rule.BookId, rule.ChapterId, rule.Difficulty, rule.Topic, rule.ChapterIds), cancellationToken);
                var candidates = matches.Select(m => m.Id).Where(id => !taken.Contains(id)).ToList();

                if (candidates.Count < rule.Count)
                    throw new PaperCannotBeDrawnError(
                        $"Section \"{section.Name}\" draws {rule.Count} questions but only {candidates.Count} are available, " +
                        "so this exam cannot be started until more matching questions are added to the bank.");

                foreach (var id in picker.Pick(candidates, rule.Count))
                {
                    taken.Add(id);
                    paper.Add((section.Id, id));
                }
            }
        }

        return paper;
    }
}
