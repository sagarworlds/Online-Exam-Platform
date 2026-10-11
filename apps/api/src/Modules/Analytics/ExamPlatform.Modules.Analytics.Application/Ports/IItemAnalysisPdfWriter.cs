using ExamPlatform.Modules.Analytics.Contracts;

namespace ExamPlatform.Modules.Analytics.Application.Ports;

/// <summary>Writes an item analysis as the bytes of a PDF document (FR-38). The layout is an infrastructure concern, kept behind this port.</summary>
public interface IItemAnalysisPdfWriter
{
    /// <summary>Writes the figures as a report: a summary, then one table row per question in the exam's order, over as many pages as needed.</summary>
    /// <param name="analysis">The analysis to write.</param>
    /// <returns>The PDF file's bytes.</returns>
    byte[] Write(ExamItemAnalysisDto analysis);
}
