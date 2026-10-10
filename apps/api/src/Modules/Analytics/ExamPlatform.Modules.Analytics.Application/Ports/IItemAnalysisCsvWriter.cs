using ExamPlatform.Modules.Analytics.Contracts;

namespace ExamPlatform.Modules.Analytics.Application.Ports;

/// <summary>Writes an item analysis as the bytes of a CSV file. The format is an infrastructure concern, kept behind this port so the export rules do not depend on it.</summary>
public interface IItemAnalysisCsvWriter
{
    /// <summary>Writes one row per question, in the exam's order.</summary>
    /// <param name="analysis">The analysis to write.</param>
    /// <returns>The file's bytes, UTF-8 with a byte-order mark so a spreadsheet reads Hindi and Marathi text correctly.</returns>
    byte[] Write(ExamItemAnalysisDto analysis);
}
