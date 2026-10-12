namespace ExamPlatform.Modules.Analytics.Contracts;

/// <summary>The file formats a report can be exported in (FR-38).</summary>
public enum ReportFormat
{
    /// <summary>Comma-separated values, for a spreadsheet to sort and sum.</summary>
    Csv,

    /// <summary>A PDF document, for reading and printing.</summary>
    Pdf,
}
