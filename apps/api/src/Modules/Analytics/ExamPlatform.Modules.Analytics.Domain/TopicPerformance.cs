namespace ExamPlatform.Modules.Analytics.Domain;

/// <summary>How a candidate's answers fell under one topic label, added up across every question that carries it (FR-36).</summary>
/// <param name="Name">The topic label.</param>
/// <param name="QuestionCount">How many questions the candidate sat that carry the label.</param>
/// <param name="Answers">How those questions were answered.</param>
public sealed record TopicTally(string Name, int QuestionCount, AnswerTally Answers);

/// <summary>
/// Groups a candidate's answers by the topic labels their questions carry (FR-36, accuracy by topic). Pure, so the grouping is tested without a
/// database, and the same rule can be reused by any report that groups by topic.
/// </summary>
/// <remarks>
/// A question with several labels counts under each of them, because it tests each one: a question on "algebra" and "graphs" says something about
/// both. A question with no label says nothing about any topic, so it is left out rather than filed under an empty name. Labels match ignoring case
/// and surrounding spaces, as sections do. The topics come back weakest accuracy first, so the ones to work on are at the top; a topic the candidate
/// never answered has no accuracy and sorts last, since it says nothing about where they are weak.
/// </remarks>
public static class TopicPerformance
{
    /// <summary>Adds up each question's answer under each of its topic labels.</summary>
    /// <param name="questions">Each question the candidate sat, with the labels it carries (possibly none) and how it was answered.</param>
    /// <returns>One tally per topic label, weakest accuracy first, ties broken by name.</returns>
    public static IReadOnlyList<TopicTally> Of(IEnumerable<(IReadOnlyList<string> Topics, AnswerTally Answer)> questions) =>
        questions
            .SelectMany(question => question.Topics.Select(topic => (Topic: topic.Trim(), question.Answer)))
            .Where(labelled => labelled.Topic.Length > 0)
            .GroupBy(labelled => labelled.Topic, StringComparer.OrdinalIgnoreCase)
            .Select(group => new TopicTally(
                group.Key,
                group.Count(),
                group.Aggregate(AnswerTally.Empty, (total, labelled) => total.Add(labelled.Answer))))
            .OrderBy(topic => topic.Answers.Accuracy ?? decimal.MaxValue)
            .ThenBy(topic => topic.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
