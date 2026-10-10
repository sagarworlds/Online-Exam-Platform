using System.Text;

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
/// Writes a certificate as a one-page PDF (FR-34). The writer is small on purpose: it uses the Helvetica font that every PDF reader has
/// without embedding, so no font file is shipped and no third-party PDF library, with its licence, is needed. The cost is the character set:
/// Helvetica here covers Latin letters, digits and punctuation, which <see cref="CanShow"/> checks before anything is written.
/// </summary>
/// <remarks>
/// The PDF is written by hand, with the cross-reference table computed from the real byte offsets, so every reader can open it and a
/// test can check the offsets. Text is escaped, so a name with brackets or a backslash cannot break the file.
/// </remarks>
public static class CertificatePdf
{
    private const int PageWidth = 595;
    private const int PageHeight = 842;

    /// <summary>Whether the text can be printed with the built-in font: printable ASCII and the Latin-1 letters from 0xA0 to 0xFF.</summary>
    /// <param name="text">The text to print.</param>
    public static bool CanShow(string text) =>
        text.All(c => c is >= ' ' and <= '~' || c is >= ' ' and <= 'ÿ');

    /// <summary>Lays out and writes the certificate.</summary>
    /// <param name="details">What the certificate says; its text must pass <see cref="CanShow"/>.</param>
    /// <returns>The PDF file's bytes.</returns>
    /// <exception cref="ArgumentException">The candidate's or the exam's name has a character the built-in font cannot show.</exception>
    public static byte[] Render(CertificateDetails details)
    {
        if (!CanShow(details.CandidateName) || !CanShow(details.ExamName))
            throw new ArgumentException("The certificate text uses a character the built-in font cannot show.", nameof(details));

        var content = BuildContent(details);
        // Every character is below 0x100 (CanShow), so Latin-1 writes each one as the single byte the PDF's offsets were counted in.
        return Encoding.Latin1.GetBytes(Assemble(content, details.CandidateName));
    }

    private static string BuildContent(CertificateDetails details)
    {
        var sb = new StringBuilder();
        // A dark blue frame around the page, drawn first so the text sits inside it.
        sb.Append("0.15 0.25 0.45 RG 2 w 36 36 523 770 re S\n");

        var y = 730.0;
        y = Text(sb, "Certificate of Completion", "F2", 26, 72, y, 40);
        y = Text(sb, "This certifies that", "F1", 14, 72, y - 12, 26);
        y = Text(sb, details.CandidateName, "F2", 24, 72, y - 8, 32);
        y = Text(sb, "has completed", "F1", 14, 72, y - 12, 26);
        y = Text(sb, details.ExamName, "F2", 18, 72, y - 8, 24);
        var when = details.CompletedOnUtc.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
        var score = $"{Format(details.Score)} out of {Format(details.MaxScore)}";
        Text(sb, $"on {when}, with a score of {score}.", "F1", 14, 72, y - 12, 26);

        Text(sb, $"Certificate ID: {details.AttemptId.ToString("D").ToUpperInvariant()}", "F1", 10, 72, 70, 12);
        return sb.ToString();
    }

    /// <summary>Writes a paragraph, wrapped to the page, and returns the height left below it.</summary>
    private static double Text(StringBuilder sb, string text, string font, int size, double x, double y, double lineHeight)
    {
        // A conservative width for Helvetica: most letters are narrower than half an em, so this keeps lines inside the frame.
        var maxChars = (int)Math.Floor(450 / (size * 0.5));
        foreach (var line in Wrap(text, maxChars))
        {
            sb.Append($"BT /{font} {size} Tf {Num(x)} {Num(y)} Td ({Escape(line)}) Tj ET\n");
            y -= lineHeight;
        }

        return y;
    }

    private static IEnumerable<string> Wrap(string text, int maxChars)
    {
        var line = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > maxChars)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0)
                line.Append(' ');
            line.Append(word);
        }

        if (line.Length > 0)
            yield return line.ToString();
    }

    private static string Assemble(string content, string candidateName)
    {
        // Object 1 is the catalogue, 2 the page tree, 3 the page, 4 and 5 the two fonts, 6 the page's content and 7 the document information.
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 6 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
            $"<< /Length {Encoding.Latin1.GetByteCount(content)} >>\nstream\n{content}endstream",
            $"<< /Title ({Escape("Certificate of Completion - " + candidateName)}) /Producer (Online Exam Platform) >>",
        };

        var sb = new StringBuilder();
        // A binary comment on the second line tells transfer tools this file is binary, not text.
        sb.Append("%PDF-1.4\n%âãÏÓ\n");
        var offsets = new List<int>(objects.Length);
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.Latin1.GetByteCount(sb.ToString()));
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = Encoding.Latin1.GetByteCount(sb.ToString());
        sb.Append($"xref\n0 {objects.Length + 1}\n");
        // Entry 0 is the free head; every other entry is exactly 20 bytes, as the format requires.
        sb.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
            sb.Append($"{offset:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R /Info 7 0 R >>\nstartxref\n{xref}\n%%EOF\n");

        return sb.ToString();
    }

    /// <summary>Escapes the characters that have a meaning inside a PDF string, so the text is printed as written.</summary>
    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    private static string Num(double value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static string Format(decimal value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
