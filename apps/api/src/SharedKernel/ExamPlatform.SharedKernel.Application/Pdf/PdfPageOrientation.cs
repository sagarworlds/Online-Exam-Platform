namespace ExamPlatform.SharedKernel.Application.Pdf;

/// <summary>Which way an A4 page is turned.</summary>
public enum PdfPageOrientation
{
    /// <summary>595 points wide and 842 tall, as every page has been unless it asks otherwise.</summary>
    Portrait,

    /// <summary>842 points wide and 595 tall, for a certificate.</summary>
    Landscape,
}
