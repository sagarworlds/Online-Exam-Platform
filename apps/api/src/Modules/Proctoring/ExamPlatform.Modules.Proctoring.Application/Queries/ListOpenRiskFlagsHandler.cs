using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.Proctoring.Application.Dtos;
using ExamPlatform.Modules.Proctoring.Application.Ports;

namespace ExamPlatform.Modules.Proctoring.Application.Queries;

/// <summary>
/// Reads what waits in the review queues of every exam at once: how many flags are open, and the few with the highest score, each with the
/// name of its exam. The admin home shows it as a tile, so staff see that something needs review without opening each exam in turn.
/// </summary>
public sealed class ListOpenRiskFlagsHandler(IExamCatalog catalog, IRiskAssessmentRepository assessments)
{
    /// <summary>The most flags a caller may ask to see at once.</summary>
    public const int MaxLimit = 10;

    /// <summary>The number shown when the caller does not say.</summary>
    public const int DefaultLimit = 5;

    /// <summary>Reads the total and the leading flags.</summary>
    /// <param name="limit">How many flags to list; held between 1 and <see cref="MaxLimit"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<OpenRiskFlagsDto> HandleAsync(int? limit, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var open = await assessments.ListOpenFlagsAsync(take, cancellationToken);

        // One read per exam that has a flag in the list, at most MaxLimit of them. An exam that is gone is shown without a name.
        var names = new Dictionary<Guid, string?>();
        foreach (var examId in open.Items.Select(flag => flag.ExamId).Distinct())
        {
            names[examId] = (await catalog.FindAsync(examId, cancellationToken))?.Name;
        }

        var items = open.Items
            .Select(flag => new OpenRiskFlagDto(flag.Id, flag.ExamId, names[flag.ExamId], flag.AttemptNumber, flag.Score, flag.MaxScore, flag.ComputedAtUtc))
            .ToList();
        return new OpenRiskFlagsDto(open.Total, items);
    }
}
