using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.ExamAuthoring.Application;

/// <summary>
/// Checks that every draw rule of an exam can be filled. Candidates are only drawn their papers when they start, so a rule the bank
/// cannot satisfy would otherwise surface as a refused start on the day; this finds it while the author can still fix it.
/// </summary>
public sealed class DrawPoolChecker(IQuestionBank questionBank)
{
    /// <summary>Verifies each rule has at least as many eligible questions as it draws.</summary>
    /// <remarks>
    /// Eligible means: matches the rule inside the exam's scope and is not already one of the exam's fixed questions (those always
    /// appear, so drawing one again would add nothing). Each rule is judged alone; rules of one section that overlap can still
    /// compete for the same questions, which the draw itself reports when it happens.
    /// </remarks>
    /// <param name="exam">The exam to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="DrawPoolTooSmallError">A rule needs more questions than the bank has for it.</exception>
    public async Task EnsureFillableAsync(Exam exam, CancellationToken cancellationToken)
    {
        var fixedIds = exam.Sections.SelectMany(s => s.Questions).Select(q => q.QuestionVersionId).ToHashSet();

        foreach (var section in exam.Sections.OrderBy(s => s.Order))
        {
            foreach (var rule in section.DrawRules.OrderBy(r => r.Order))
            {
                var scoped = DrawRuleScoping.Apply(rule, exam.Scope)
                    ?? throw new DrawPoolTooSmallError(
                        $"A draw rule in section \"{section.Name}\" is outside the exam's scope, so it can never match a question.");

                var matches = await questionBank.FindAsync(
                    new QuestionCriteria(scoped.BookId, scoped.ChapterId, scoped.Difficulty, scoped.Topic, scoped.ChapterIds), cancellationToken);
                var available = matches.Count(q => !fixedIds.Contains(q.Id));

                if (available < rule.Count)
                    throw new DrawPoolTooSmallError(
                        $"Section \"{section.Name}\" draws {rule.Count} questions but only {available} match its rule and are not already in the exam.");
            }
        }
    }
}
