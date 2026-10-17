using System.Globalization;
using ExamPlatform.SharedKernel.Application.Pdf;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>What a certificate says (FR-34): who took which exam, which kind of certificate it is, when, and the reference that identifies it.</summary>
/// <param name="CandidateName">The candidate's name, as on their account.</param>
/// <param name="ExamName">The exam's name.</param>
/// <param name="CompletedOnUtc">When the attempt was submitted.</param>
/// <param name="Kind">Whether it certifies a pass or participation.</param>
/// <param name="Score">The marks scored, for an achievement certificate; null for a participation certificate, which names no score.</param>
/// <param name="MaxScore">The marks available, for an achievement certificate; null together with <paramref name="Score"/>.</param>
/// <param name="AttemptId">The attempt the certificate is for; its id is printed on the certificate as the reference.</param>
public sealed record CertificateDetails(
    string CandidateName, string ExamName, DateTime CompletedOnUtc, CertificateKind Kind, decimal? Score, decimal? MaxScore, Guid AttemptId);

/// <summary>
/// Writes a certificate as a one-page landscape PDF (FR-34). The page is drawn with the shared PDF writer, which uses the Helvetica and Times
/// faces every PDF reader has without embedding, so no font file is shipped and no third-party PDF library is needed. The cost is the character
/// set: those faces cover Latin letters, digits and punctuation, which <see cref="CanShow"/> checks before anything is written.
/// </summary>
/// <remarks>
/// <para>
/// The look is the classic one: an ivory page inside a navy frame with a gold inner line and gold corners, a spaced serif title, the
/// candidate's name large in the middle, and a footer of the date, a seal and the certificate's reference. An achievement is gold, with the
/// word PASSED on its seal; a participation certificate is silver, names no score, and its seal says PARTICIPANT.
/// </para>
/// <para>
/// The writer and its text rules live in the SharedKernel PDF engine, which the reports also use (FR-38). The engine can also draw Devanagari
/// with its embedded font, but a certificate still refuses it: a certificate is a document of record, and that the name prints in a new way is
/// a decision for its owner, not a side effect of the reports. So the certificate keeps the Latin set, and a name outside it is refused.
/// </para>
/// <para>
/// All positions below are in points on an A4 landscape page, 842 wide and 595 tall. They are written as the distance from the top of the page,
/// as a designer reads a page, and turned into the writer's bottom-up coordinates by <see cref="FromTop"/>.
/// </para>
/// </remarks>
public static class CertificatePdf
{
    private const string Issuer = "ONLINE EXAM PLATFORM";

    /// <summary>The widest a name or an exam's name may run before it is made smaller or wrapped, in points.</summary>
    private const double TextWidth = 640;

    private static readonly PdfColor Ivory = PdfColor.FromRgb(0xFBF6E9);
    private static readonly PdfColor Navy = PdfColor.FromRgb(0x1B2A49);
    private static readonly PdfColor Grey = PdfColor.FromRgb(0x4A5568);
    private static readonly PdfColor Ink = PdfColor.FromRgb(0x2D3748);

    /// <summary>The colours and the seal's lettering that tell an achievement from a participation certificate.</summary>
    /// <param name="Decoration">The colour of the inner frame, the corners, the divider and the rule under the name.</param>
    /// <param name="AccentText">The colour of the line naming the kind, dark enough to read on the ivory page.</param>
    /// <param name="Seal">The seal's colour.</param>
    /// <param name="SealLabel">The word on the seal.</param>
    /// <param name="SealLabelSize">The size of the word on the seal, in points, so the longer word still fits inside the ring.</param>
    /// <param name="SealLabelSpacing">The letter spacing of the word on the seal, in points.</param>
    private sealed record Palette(PdfColor Decoration, PdfColor AccentText, PdfColor Seal, string SealLabel, double SealLabelSize, double SealLabelSpacing);

    private static readonly Palette Gold = new(PdfColor.FromRgb(0xB38B2E), PdfColor.FromRgb(0x8A6612), PdfColor.FromRgb(0xC9A24A), "PASSED", 11, 1.5);
    private static readonly Palette Silver = new(PdfColor.FromRgb(0x8C97A8), PdfColor.FromRgb(0x4F5B6D), PdfColor.FromRgb(0xC5CBD6), "PARTICIPANT", 7.5, 0.5);

    /// <summary>Whether the text can be printed with the built-in fonts: printable ASCII and the Latin-1 letters from 0xA0 to 0xFF.</summary>
    /// <param name="text">The text to print.</param>
    /// <returns>True when every character of the text can be printed.</returns>
    public static bool CanShow(string text) => PdfText.CanShowLatin(text);

    /// <summary>Lays out and writes the certificate.</summary>
    /// <param name="details">What the certificate says; its text must pass <see cref="CanShow"/>.</param>
    /// <returns>The PDF file's bytes.</returns>
    /// <exception cref="ArgumentException">The candidate's or the exam's name has a character the built-in fonts cannot show.</exception>
    public static byte[] Render(CertificateDetails details)
    {
        if (!CanShow(details.CandidateName) || !CanShow(details.ExamName))
            throw new ArgumentException("The certificate text uses a character the built-in font cannot show.", nameof(details));

        var document = new PdfDocument($"{TitleOf(details.Kind)} - {details.CandidateName}");
        var page = document.AddPage(PdfPageOrientation.Landscape);
        BuildPage(page, details);
        return document.Build();
    }

    private static void BuildPage(PdfPage page, CertificateDetails details)
    {
        var palette = details.Kind == CertificateKind.Achievement ? Gold : Silver;
        DrawFrame(page, palette);
        DrawHeading(page, details.Kind, palette);
        DrawBody(page, details, palette);
        DrawFooter(page, details, palette);
    }

    /// <summary>
    /// The ivory page, the navy frame, the gold line inside it and the four gold corners. The ivory stops at the frame, 24 points from each
    /// edge, because a home printer cannot print to the edge of the paper and a tint that ran off the page would show a ragged white border.
    /// </summary>
    private static void DrawFrame(PdfPage page, Palette palette)
    {
        var width = page.Width;
        var height = page.Height;
        page.FillRectangle(24, 24, width - 48, height - 48, Ivory);
        page.StrokeRectangle(25, 25, width - 50, height - 50, 2, Navy);
        page.StrokeRectangle(33.5, 33.5, width - 67, height - 67, 1, palette.Decoration);

        const double corner = 12;
        foreach (var x in new[] { 27, width - 27 - corner })
        {
            foreach (var y in new[] { 27, height - 27 - corner })
                page.FillRectangle(x, y, corner, corner, palette.Decoration);
        }
    }

    /// <summary>The issuer, the title, the line naming the kind of certificate, and the divider.</summary>
    private static void DrawHeading(PdfPage page, CertificateKind kind, Palette palette)
    {
        var centre = page.Width / 2.0;
        page.TextCentered(Issuer, PdfFont.Bold, 10, centre, FromTop(page, 63), Navy, 3.5);
        page.TextCentered("CERTIFICATE", PdfFont.TimesBold, 52, centre, FromTop(page, 124), Navy, 9);
        page.TextCentered(KindLine(kind), PdfFont.Bold, 12.5, centre, FromTop(page, 158), palette.AccentText, 6);

        // A rule, a small square and a rule: the divider between the title and the words about the candidate.
        var dividerY = FromTop(page, 180.5);
        page.Line(centre - 114, dividerY, centre - 14, dividerY, 1, palette.Decoration);
        page.FillRectangle(centre - 3.5, dividerY - 3.5, 7, 7, palette.Decoration);
        page.Line(centre + 14, dividerY, centre + 114, dividerY, 1, palette.Decoration);
    }

    /// <summary>
    /// The words of the certificate: who, what and, for an achievement, the score. The candidate's name and the exam's name are set as large
    /// as they fit: a long one is made smaller, and one that is still too long is wrapped over two lines, so nothing runs off the page.
    /// </summary>
    private static void DrawBody(PdfPage page, CertificateDetails details, Palette palette)
    {
        var centre = page.Width / 2.0;
        page.TextCentered("This is to certify that", PdfFont.TimesItalic, 17, centre, FromTop(page, 219), Grey);

        var name = Fit(details.CandidateName, PdfFont.TimesBold, startSize: 44, floorSize: 30, minSize: 20, maxLines: 2);
        var nameLast = DrawLines(page, name, PdfFont.TimesBold, centre, firstBaseline: 271, Navy);

        // A rule under the name, then the connecting words and the exam.
        var ruleY = nameLast + 17;
        page.Line(centre - 200, FromTop(page, ruleY), centre + 200, FromTop(page, ruleY), 1, palette.Decoration);

        var verbY = ruleY + 30.5;
        page.TextCentered(VerbOf(details.Kind), PdfFont.TimesItalic, 17, centre, FromTop(page, verbY), Grey);

        var exam = Fit(details.ExamName, PdfFont.TimesBold, startSize: 26, floorSize: 20, minSize: 14, maxLines: 2);
        var examLast = DrawLines(page, exam, PdfFont.TimesBold, centre, firstBaseline: verbY + 34.5, Navy);

        // Only an achievement names a score: a participation certificate has none to name, so it is not given one.
        if (details.Score is { } score && details.MaxScore is { } maxScore)
            page.TextCentered($"with a score of {Format(score)} out of {Format(maxScore)}", PdfFont.TimesRoman, 16, centre, FromTop(page, examLast + 32), Ink);
    }

    /// <summary>
    /// The footer: the date on the left, the reference on the right, each above a rule with its caption below, and the seal between them.
    /// </summary>
    private static void DrawFooter(PdfPage page, CertificateDetails details, Palette palette)
    {
        var centre = page.Width / 2.0;
        const double ruleY = 83;
        const double captionY = 69;
        const double captionSize = 9;
        const double captionSpacing = 2;
        const double blockWidth = 230;
        const double blockOffset = 234;

        var dateX = centre - blockOffset;
        var idX = centre + blockOffset;

        var date = details.CompletedOnUtc.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
        page.TextCentered(date, PdfFont.TimesRoman, 14, dateX, ruleY + 7, Navy);
        page.Line(dateX - blockWidth / 2, ruleY, dateX + blockWidth / 2, ruleY, 1, Navy);
        page.TextCentered("DATE", PdfFont.Bold, captionSize, dateX, captionY, Grey, captionSpacing);

        page.TextCentered(details.AttemptId.ToString("D").ToUpperInvariant(), PdfFont.Regular, 9.5, idX, ruleY + 7, Ink, 0.6);
        page.Line(idX - blockWidth / 2, ruleY, idX + blockWidth / 2, ruleY, 1, Navy);
        page.TextCentered("CERTIFICATE ID", PdfFont.Bold, captionSize, idX, captionY, Grey, captionSpacing);

        DrawSeal(page, centre, 98, details, palette);
    }

    /// <summary>A coloured disc inside two thin rings, with the word for the kind of certificate over a short rule and the year.</summary>
    private static void DrawSeal(PdfPage page, double centreX, double centreY, CertificateDetails details, Palette palette)
    {
        page.FillCircle(centreX, centreY, 46, palette.Seal);
        page.StrokeCircle(centreX, centreY, 40, 1.5, Ivory);
        page.StrokeCircle(centreX, centreY, 34, 1, Navy);

        page.TextCentered(palette.SealLabel, PdfFont.Bold, palette.SealLabelSize, centreX, centreY + 7, Navy, palette.SealLabelSpacing);
        page.Line(centreX - 13, centreY + 1.5, centreX + 13, centreY + 1.5, 1, Navy);
        page.TextCentered(details.CompletedOnUtc.Year.ToString(CultureInfo.InvariantCulture), PdfFont.TimesBold, 15, centreX, centreY - 13.5, Navy);
    }

    /// <summary>Writes lines of text one under another, each centred, and returns where the last one sits, as a distance from the top.</summary>
    private static double DrawLines(PdfPage page, FittedText text, PdfFont font, double centre, double firstBaseline, PdfColor color)
    {
        var baseline = firstBaseline;
        for (var i = 0; i < text.Lines.Count; i++)
        {
            if (i > 0)
                baseline += text.Size * 1.1;

            page.TextCentered(text.Lines[i], font, text.Size, centre, FromTop(page, baseline), color);
        }

        return baseline;
    }

    /// <summary>
    /// Chooses the size and the lines for a piece of text that must fit <see cref="TextWidth"/>. It is set at <paramref name="startSize"/> on one
    /// line if it fits, made smaller down to <paramref name="floorSize"/> to fit on one line if it can, and otherwise wrapped, at the floor size
    /// or smaller (never below <paramref name="minSize"/>), onto no more than <paramref name="maxLines"/> lines. Text so long that it still does
    /// not fit is wrapped at the smallest size, which is the most that can be done on one page.
    /// </summary>
    private static FittedText Fit(string text, PdfFont font, double startSize, double floorSize, double minSize, int maxLines)
    {
        for (var size = startSize; size >= floorSize; size -= 1)
        {
            var lines = PdfText.Wrap(text, font, size, TextWidth);
            if (lines.Count <= 1)
                return new FittedText(lines, size);
        }

        for (var size = floorSize; size >= minSize; size -= 1)
        {
            var lines = PdfText.Wrap(text, font, size, TextWidth);
            if (lines.Count <= maxLines)
                return new FittedText(lines, size);
        }

        return new FittedText(PdfText.Wrap(text, font, minSize, TextWidth), minSize);
    }

    /// <summary>Text with the size it is set at and the lines it is broken into.</summary>
    private sealed record FittedText(IReadOnlyList<string> Lines, double Size);

    /// <summary>Turns a distance from the top of the page into the writer's y, which counts up from the bottom.</summary>
    private static double FromTop(PdfPage page, double distance) => page.Height - distance;

    private static string TitleOf(CertificateKind kind) =>
        kind == CertificateKind.Achievement ? "Certificate of Achievement" : "Certificate of Participation";

    private static string KindLine(CertificateKind kind) => kind == CertificateKind.Achievement ? "OF ACHIEVEMENT" : "OF PARTICIPATION";

    private static string VerbOf(CertificateKind kind) => kind == CertificateKind.Achievement ? "has successfully passed" : "has taken part in";

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
