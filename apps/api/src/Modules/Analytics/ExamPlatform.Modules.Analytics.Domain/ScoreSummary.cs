namespace ExamPlatform.Modules.Analytics.Domain;

/// <summary>One band of an exam's score distribution (FR-37).</summary>
/// <param name="From">The lowest percentage the band holds, inclusive; null for the bottom band, which also takes every score below ten percent.</param>
/// <param name="To">The percentage the band stops below, exclusive; null for the top band, which also takes full marks and anything above.</param>
/// <param name="Count">How many candidates' best results fall in the band.</param>
public sealed record ScoreBand(decimal? From, decimal? To, int Count);

/// <summary>An exam's candidate scores as percentages of the marks on each paper, with their mean, median and distribution (FR-37).</summary>
/// <param name="Count">How many candidates' results the figures were worked from.</param>
/// <param name="Mean">The mean percentage, to two decimals; null when no candidate had marks to earn.</param>
/// <param name="Median">The median percentage, to two decimals; null when no candidate had marks to earn.</param>
/// <param name="Bands">The bands in order from the lowest; their counts add up to <paramref name="Count"/>.</param>
public sealed record ScoreSummary(int Count, decimal? Mean, decimal? Median, IReadOnlyList<ScoreBand> Bands);
