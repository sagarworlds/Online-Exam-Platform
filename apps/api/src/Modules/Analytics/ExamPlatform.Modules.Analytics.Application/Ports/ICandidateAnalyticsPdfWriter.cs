using ExamPlatform.Modules.Analytics.Contracts;

namespace ExamPlatform.Modules.Analytics.Application.Ports;

/// <summary>Writes a candidate's analytics as the bytes of a PDF document (FR-38). The layout is an infrastructure concern, kept behind this port.</summary>
public interface ICandidateAnalyticsPdfWriter
{
    /// <summary>Writes the figures as a report: a summary, the score trend, then the section figures, over as many pages as needed.</summary>
    /// <param name="analytics">The candidate's figures.</param>
    /// <returns>The PDF file's bytes.</returns>
    byte[] Write(CandidateAnalyticsDto analytics);
}
