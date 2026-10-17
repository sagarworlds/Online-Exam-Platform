using System.Globalization;
using System.Text;
using ExamPlatform.Modules.ExamRuntime.Application;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>
/// The certificate's PDF (FR-34): a file every reader opens, with its cross-reference table pointing at the real objects, printing the text as
/// written and refusing the characters its font cannot show.
/// </summary>
public class CertificatePdfTests
{
    private static readonly Guid Attempt = new("0a1b2c3d-0000-4000-8000-00000000000f");

    private static CertificateDetails Details(string name = "Asha Kumar", string exam = "Mathematics Mock Test") =>
        new(name, exam, new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc), CertificateKind.Achievement, 37.75m, 80m, Attempt);

    private static string Text(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    [Fact]
    public void TheFile_StartsWithAPdfHeader_AndEndsWithTheEndOfFileMarker()
    {
        var text = Text(CertificatePdf.Render(Details()));

        Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCrossReferenceTable_PointsAtEachObject_ByItsRealByteOffset()
    {
        var pdf = CertificatePdf.Render(Details());
        var text = Text(pdf);

        var startxref = int.Parse(text.Split("startxref\n")[1].Split('\n')[0], CultureInfo.InvariantCulture);
        Assert.Equal("xref", Encoding.Latin1.GetString(pdf, startxref, 4));

        // The seven objects the file has always had, then the three Times faces the page draws with; each entry is exactly twenty bytes and
        // points at "n 0 obj" for its number.
        var objectCount = 10;
        Assert.Equal($"0 {objectCount + 1}", text[startxref..].Split('\n')[1]);
        var entries = text[startxref..].Split('\n').Skip(3).Take(objectCount).ToList();
        for (var i = 1; i <= objectCount; i++)
        {
            Assert.Equal(20, entries[i - 1].Length + 1);
            var offset = int.Parse(entries[i - 1].Split(' ')[0], CultureInfo.InvariantCulture);
            Assert.StartsWith($"{i} 0 obj", Encoding.Latin1.GetString(pdf, offset, 12), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheCertificate_IsStillOnOnePage()
    {
        // The shared PDF writer lays out any number of pages; the certificate is a single page, as its object numbers have always been.
        var text = Text(CertificatePdf.Render(Details()));

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, @"/Type /Page\b"));
        Assert.Contains("/Info 7 0 R", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCertificate_IsALandscapeA4Page_TitledWithTheKindAndTheCandidate()
    {
        var text = Text(CertificatePdf.Render(Details()));

        Assert.Contains("/MediaBox [0 0 842 595]", text, StringComparison.Ordinal);
        Assert.Contains("/Title (Certificate of Achievement - Asha Kumar)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AParticipationCertificate_IsTitledAsSuch_AndNamesNoScore()
    {
        var pdf = Text(CertificatePdf.Render(Details() with { Kind = CertificateKind.Participation, Score = null, MaxScore = null }));

        Assert.Contains("(OF PARTICIPATION) Tj", pdf);
        Assert.Contains("(has taken part in) Tj", pdf);
        Assert.Contains("(PARTICIPANT) Tj", pdf);
        Assert.DoesNotContain("with a score of", pdf);
        Assert.DoesNotContain("PASSED", pdf);
        Assert.Contains("/Title (Certificate of Participation - Asha Kumar)", pdf);
    }

    [Fact]
    public void AnAchievementCertificate_IsTitledAsSuch_AndNamesTheScore()
    {
        var pdf = Text(CertificatePdf.Render(Details()));

        Assert.Contains("(OF ACHIEVEMENT) Tj", pdf);
        Assert.Contains("(has successfully passed) Tj", pdf);
        Assert.Contains("(PASSED) Tj", pdf);
        Assert.Contains("(with a score of 37.75 out of 80) Tj", pdf);
        Assert.DoesNotContain("PARTICIPANT", pdf);
    }

    [Fact]
    public void TheDateAndTheYear_AreWhenTheAttemptWasSubmitted()
    {
        var text = Text(CertificatePdf.Render(Details()));

        Assert.Contains("(12 October 2026) Tj", text, StringComparison.Ordinal);
        Assert.Contains("(2026) Tj", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLetterSpacedLines_SayWhatTheySpell_SoTheyCanBeCopiedAndSearched()
    {
        var text = Text(CertificatePdf.Render(Details()));

        // CERTIFICATE, as the hexadecimal UTF-16 an ActualText string is written in.
        Assert.Contains("/ActualText <FEFF" + string.Concat("CERTIFICATE".Select(c => ((int)c).ToString("X4", CultureInfo.InvariantCulture))) + ">", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCandidatesName_IsPrintedAsWritten()
    {
        var text = Text(CertificatePdf.Render(Details("Asha Kumar")));

        Assert.Contains("(Asha Kumar) Tj", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BracketsAndBackslashes_InAName_AreEscaped_SoTheyCannotBreakTheFile()
    {
        var text = Text(CertificatePdf.Render(Details(name: @"Asha (Kumar) \ Rao")));

        Assert.Contains(@"(Asha \(Kumar\) \\ Rao) Tj", text, StringComparison.Ordinal);
    }

    private static IReadOnlyList<double> NameSizes(string pdf) =>
        System.Text.RegularExpressions.Regex.Matches(pdf, @"/F5 ([0-9.]+) Tf [0-9.\-]+ [0-9.\-]+ Td \(Bartholomew")
            .Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();

    [Fact]
    public void AShortName_IsSetAtTheFullSize_OnOneLine()
    {
        var text = Text(CertificatePdf.Render(Details("Bartholomew Rao")));

        Assert.Equal([44.0], NameSizes(text));
    }

    [Fact]
    public void ANameTooWideAtTheFullSize_IsMadeSmaller_AndStaysOnOneLine()
    {
        var text = Text(CertificatePdf.Render(Details("Bartholomew Bartholomew Bartholomew")));

        var sizes = NameSizes(text);

        Assert.Single(sizes);
        Assert.InRange(sizes[0], 30, 43);
    }

    [Fact]
    public void ALongName_IsWrappedOverSeveralLines_NotRunOffThePage()
    {
        var name = string.Join(' ', Enumerable.Repeat("Bartholomew", 6));

        var sizes = NameSizes(Text(CertificatePdf.Render(Details(name))));

        // Two lines, both at one size, which is smaller than the full size.
        Assert.Equal(2, sizes.Count);
        Assert.Single(sizes.Distinct());
        Assert.True(sizes[0] < 44);
    }

    [Fact]
    public void ALongExamName_IsBrokenBetweenWords_OverMoreThanOneLine()
    {
        var exam = "National Level Scholarship Qualifying Examination in Advanced Mathematics and Physical Sciences (Stage 2)";

        var text = Text(CertificatePdf.Render(Details(exam: exam)));

        // The words are all there, but not on one line: the break falls between two of them, and the score is still drawn after the exam.
        Assert.Contains("National Level Scholarship", text, StringComparison.Ordinal);
        Assert.DoesNotContain("National Level Scholarship Qualifying Examination in Advanced Mathematics and Physical Sciences", text, StringComparison.Ordinal);
        Assert.Contains("Mathematics and Physical Sciences", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("(with a score of", StringComparison.Ordinal) > text.IndexOf("Mathematics and Physical Sciences", StringComparison.Ordinal));
    }

    [Fact]
    public void TheCertificateId_IsTheAttemptsIdentity()
    {
        var text = Text(CertificatePdf.Render(Details()));

        Assert.Contains("(0A1B2C3D-0000-4000-8000-00000000000F) Tj", text, StringComparison.Ordinal);
        Assert.Contains("(CERTIFICATE ID) Tj", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Asha Kumar", true)]
    [InlineData("Café Müller", true)]
    [InlineData("Asha (Kumar)", true)]
    [InlineData("अशा कुमार", false)]
    [InlineData("Ā-long vowel", false)]
    [InlineData("Ellipsis…", false)]
    public void OnlyTheCharactersTheBuiltInFontHas_CanBePrinted(string text, bool canShow)
    {
        Assert.Equal(canShow, CertificatePdf.CanShow(text));
    }

    [Fact]
    public void Rendering_AnUnprintableName_IsRefused_RatherThanPrintingSubstitutes()
    {
        Assert.Throws<ArgumentException>(() => CertificatePdf.Render(Details("अशा कुमार")));
    }
}
