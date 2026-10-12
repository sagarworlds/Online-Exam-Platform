using System.Globalization;
using ExamPlatform.SharedKernel.Application.Pdf;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>What a certificate says (FR-34): who completed which exam, when, with what score, and the reference that identifies it.</summary>
/// <param name="CandidateName">The candidate's name, as on their account.</param>
/// <param name="ExamName">The exam's name.</param>
/// <param name="CompletedOnUtc">When the attempt was submitted.</param>
/// <param name="Score">The marks scored, as the released result says.</param>
/// <param name="MaxScore">The marks available.</param>
/// <param name="AttemptId">The attempt the certificate is for; its id is printed on the certificate as the reference.</param>
public sealed record CertificateDetails(string CandidateName, string ExamName, DateTime CompletedOnUtc, decimal Score, decimal MaxScore, Guid AttemptId);

/// <summary>
/// Writes a certificate as a one-page PDF (FR-34). The page is drawn with the shared PDF writer, which uses the Helvetica font every PDF reader
/// has without embedding, so no font file is shipped and no third-party PDF library is needed. The cost is the character set: Helvetica here
/// covers Latin letters, digits and punctuation, which <see cref="CanShow"/> checks before anything is written.
/// </summary>
/// <remarks>
/// The writer and its text rules live in the SharedKernel PDF engine, which the reports also use (FR-38). The engine can also draw Devanagari
/// with its embedded font, but a certificate still refuses it: a certificate is a document of record, and that the name prints in a new way is
/// a decision for its owner, not a side effect of the reports. So the certificate keeps the Latin set, and a name outside it is refused.
/// </remarks>
public static class CertificatePdf
{
    private const double TextWidth = 450;
    private static readonly PdfColor Frame = new(0.15, 0.25, 0.45);

    /// <summary>Whether the text can be printed with the Helvetica font: printable ASCII and the Latin-1 letters from 0xA0 to 0xFF.</summary>
    /// <param name="text">The text to print.</param>
    /// <returns>True when every character of the text can be printed.</returns>
    public static bool CanShow(string text) => PdfText.CanShowLatin(text);

    /// <summary>Lays out and writes the certificate.</summary>
    /// <param name="details">What the certificate says; its text must pass <see cref="CanShow"/>.</param>
    /// <returns>The PDF file's bytes.</returns>
    /// <exception cref="ArgumentException">The candidate's or the exam's name has a character the built-in font cannot show.</exception>
    public static byte[] Render(CertificateDetails details)
    {
        if (!CanShow(details.CandidateName) || !CanShow(details.ExamName))
            throw new ArgumentException("The certificate text uses a character the built-in font cannot show.", nameof(details));

        var document = new PdfDocument($"Certificate of Completion - {details.CandidateName}");
        var page = document.AddPage();
        BuildPage(page, details);
        return document.Build();
    }

    private static void BuildPage(PdfPage page, CertificateDetails details)
    {
        // A dark blue frame around the page, drawn first so the text sits inside it.
        page.StrokeRectangle(36, 36, 523, 770, 2, Frame);

        var y = 730.0;
        y = Text(page, "Certificate of Completion", PdfFont.Bold, 26, 72, y, 40);
        y = Text(page, "This certifies that", PdfFont.Regular, 14, 72, y - 12, 26);
        y = Text(page, details.CandidateName, PdfFont.Bold, 24, 72, y - 8, 32);
        y = Text(page, "has completed", PdfFont.Regular, 14, 72, y - 12, 26);
        y = Text(page, details.ExamName, PdfFont.Bold, 18, 72, y - 8, 24);
        var when = details.CompletedOnUtc.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
        var score = $"{Format(details.Score)} out of {Format(details.MaxScore)}";
        Text(page, $"on {when}, with a score of {score}.", PdfFont.Regular, 14, 72, y - 12, 26);

        Text(page, $"Certificate ID: {details.AttemptId.ToString("D").ToUpperInvariant()}", PdfFont.Regular, 10, 72, 70, 12);
    }

    /// <summary>Writes a paragraph, wrapped to the text width, and returns the height left below it.</summary>
    private static double Text(PdfPage page, string text, PdfFont font, double size, double x, double y, double lineHeight)
    {
        foreach (var line in PdfText.Wrap(text, font, size, TextWidth))
        {
            page.Text(line, font, size, x, y);
            y -= lineHeight;
        }

        return y;
    }

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
