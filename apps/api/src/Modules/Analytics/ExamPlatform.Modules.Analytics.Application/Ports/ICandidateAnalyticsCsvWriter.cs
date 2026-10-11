using ExamPlatform.Modules.Analytics.Contracts;

namespace ExamPlatform.Modules.Analytics.Application.Ports;

/// <summary>Writes a candidate's analytics as the bytes of a CSV file (FR-38). The format is an infrastructure concern, kept behind this port.</summary>
public interface ICandidateAnalyticsCsvWriter
{
    /// <summary>Writes the score trend and the section figures as two blocks of rows, each with its own header.</summary>
    /// <param name="analytics">The candidate's figures.</param>
    /// <returns>The file's bytes, UTF-8 with a byte-order mark so a spreadsheet reads any exam name correctly.</returns>
    byte[] Write(CandidateAnalyticsDto analytics);
}
