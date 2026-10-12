using ExamPlatform.Modules.Analytics.Contracts;

namespace ExamPlatform.Modules.Analytics.Application;

/// <summary>What each report format is called on the wire, in a file name, and in the audit trail (FR-38).</summary>
public static class ReportFormats
{
    /// <summary>The media type a download of the format carries.</summary>
    /// <param name="format">The format.</param>
    /// <returns><c>text/csv</c> or <c>application/pdf</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The format is not one this module writes.</exception>
    public static string ContentTypeOf(ReportFormat format) => format switch
    {
        ReportFormat.Csv => "text/csv",
        ReportFormat.Pdf => "application/pdf",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "No media type is defined for this report format."),
    };

    /// <summary>The file extension of the format, without the dot.</summary>
    /// <param name="format">The format.</param>
    /// <returns><c>csv</c> or <c>pdf</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The format is not one this module writes.</exception>
    public static string ExtensionOf(ReportFormat format) => format switch
    {
        ReportFormat.Csv => "csv",
        ReportFormat.Pdf => "pdf",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "No extension is defined for this report format."),
    };
}
