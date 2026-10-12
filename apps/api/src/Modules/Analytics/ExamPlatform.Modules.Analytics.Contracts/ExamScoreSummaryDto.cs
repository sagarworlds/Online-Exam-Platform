namespace ExamPlatform.Modules.Analytics.Contracts;

/// <summary>
/// An exam's candidate scores (FR-37): each candidate counted once by their best result, as a percentage of the marks on their paper, with the
/// mean, the median and the distribution.
/// </summary>
/// <param name="Count">How many candidates' results the figures were worked from.</param>
/// <param name="MeanPercentOfMarks">The mean of the candidates' percentages, to two decimals; null when no candidate had marks to earn.</param>
/// <param name="MedianPercentOfMarks">The median of the candidates' percentages, to two decimals; null when no candidate had marks to earn.</param>
/// <param name="Distribution">The bands from the lowest; their counts add up to <paramref name="Count"/>. Empty while the results are held.</param>
public sealed record ExamScoreSummaryDto(
    int Count,
    decimal? MeanPercentOfMarks,
    decimal? MedianPercentOfMarks,
    IReadOnlyList<ScoreBandDto> Distribution);

/// <summary>One band of an exam's score distribution (FR-37), in percent of the marks on each paper.</summary>
/// <param name="FromPercent">The lowest percentage the band holds, inclusive; null for the bottom band, which also takes every score below ten percent.</param>
/// <param name="ToPercent">The percentage the band stops below, exclusive; null for the top band, which also takes full marks and anything above.</param>
/// <param name="Count">How many candidates' best results fall in the band.</param>
public sealed record ScoreBandDto(decimal? FromPercent, decimal? ToPercent, int Count);
