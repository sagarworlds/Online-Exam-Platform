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
        new(name, exam, new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc), 37.75m, 80m, Attempt);

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

        // Seven objects plus the free head: each entry is exactly twenty bytes, and points at "n 0 obj" for its number.
        var entries = Encoding.Latin1.GetString(pdf, startxref, text.Length - startxref).Split('\n').Skip(3).Take(7).ToList();
        for (var i = 1; i <= 7; i++)
        {
            var offset = int.Parse(entries[i - 1].Split(' ')[0], CultureInfo.InvariantCulture);
            Assert.StartsWith($"{i} 0 obj", Encoding.Latin1.GetString(pdf, offset, 12), StringComparison.Ordinal);
        }
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

    [Fact]
    public void ALongName_IsWrappedOverSeveralLines_NotRunOffThePage()
    {
        var name = string.Join(' ', Enumerable.Repeat("Bartholomew", 6));

        var text = Text(CertificatePdf.Render(Details(name)));

        Assert.True(text.Split("/F2 24 Tf").Length - 1 > 1, "the name is printed on more than one line");
    }

    [Fact]
    public void TheCertificateId_IsTheAttemptsIdentity()
    {
        var text = Text(CertificatePdf.Render(Details()));

        Assert.Contains("Certificate ID: 0A1B2C3D-0000-4000-8000-00000000000F", text, StringComparison.Ordinal);
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
