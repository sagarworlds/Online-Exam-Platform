using System.IO.Compression;
using System.Text;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.SharedKernel.UnitTests;

public class XlsxTests
{
    private static byte[] Zip(params (string Path, string Content)[] parts)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in parts)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }

    [Fact]
    public void WrittenRows_ReadBackUnchanged_IncludingCommasNewlinesMarkupAndNonLatinText()
    {
        IReadOnlyList<string>[] rows =
        [
            ["Text", "Option1", "Topics"],
            ["<p>Solve $x^2 < 4$, \"quoted\"\nsecond line</p>", "हिन्दी & <b>", " spaced "],
        ];

        var read = Xlsx.Read(Xlsx.Write(rows));

        Assert.Equal(2, read.Count);
        Assert.Equal(rows[0], read[0].Fields);
        Assert.Equal(rows[1], read[1].Fields);
        Assert.Equal([1, 2], read.Select(r => r.Line));
    }

    [Fact]
    public void EmptyCells_AreKeptInPlace_AndRowsArePaddedToTheWidestRow()
    {
        var read = Xlsx.Read(Xlsx.Write([["a", "", "c"], ["only"]]));

        Assert.Equal(["a", "", "c"], read[0].Fields);
        Assert.Equal(["only", "", ""], read[1].Fields);
    }

    [Fact]
    public void RowsWithNothingInThem_AreSkipped_ButKeepTheirLineNumbers()
    {
        var read = Xlsx.Read(Xlsx.Write([["head"], [""], ["data"]]));

        Assert.Equal(["head", "data"], read.Select(r => r.Fields[0]));
        Assert.Equal([1, 3], read.Select(r => r.Line));
    }

    [Fact]
    public void AWorkbookFromExcel_WithSharedStringsBooleansAndGaps_IsRead()
    {
        var file = Zip(
            ("xl/workbook.xml", """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="S" sheetId="1" r:id="rId9"/></sheets></workbook>"""),
            ("xl/_rels/workbook.xml.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId9" Type="x" Target="worksheets/other.xml"/></Relationships>"""),
            ("xl/sharedStrings.xml", """<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><si><t>Hello</t></si><si><r><t>Rich </t></r><r><t>text</t></r><rPh><t>ignored</t></rPh></si></sst>"""),
            ("xl/worksheets/other.xml", """<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="2"><c r="A2" t="s"><v>0</v></c><c r="C2" t="s"><v>1</v></c><c r="D2" t="b"><v>1</v></c><c r="E2"><v>42</v></c></row></sheetData></worksheet>"""));

        var read = Xlsx.Read(file);

        var row = Assert.Single(read);
        Assert.Equal(2, row.Line);
        Assert.Equal(["Hello", "", "Rich text", "true", "42"], row.Fields);
    }

    [Fact]
    public void ABadSharedStringIndex_IsAnEmptyCell_NotACrash()
    {
        var file = Zip(
            ("xl/workbook.xml", """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="S" sheetId="1" r:id="rId1"/></sheets></workbook>"""),
            ("xl/_rels/workbook.xml.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="x" Target="worksheets/sheet1.xml"/></Relationships>"""),
            ("xl/worksheets/sheet1.xml", """<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="s"><v>99</v></c><c r="B1"><v>ok</v></c></row></sheetData></worksheet>"""));

        var row = Assert.Single(Xlsx.Read(file));

        Assert.Equal(["", "ok"], row.Fields);
    }

    [Theory]
    [InlineData("not a zip at all")]
    [InlineData("")]
    public void BytesThatAreNotAWorkbook_Throw(string text) =>
        Assert.Throws<InvalidDataException>(() => Xlsx.Read(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void AZipWithoutAWorkbook_Throws() =>
        Assert.Throws<InvalidDataException>(() => Xlsx.Read(Zip(("readme.txt", "hi"))));

    [Fact]
    public void AWorkbookWithADoctype_IsRefused_SoExternalEntitiesCannotBeUsed()
    {
        var file = Zip(
            ("xl/workbook.xml", """<!DOCTYPE x [<!ENTITY e SYSTEM "file:///etc/passwd">]><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheets/></workbook>"""));

        Assert.Throws<InvalidDataException>(() => Xlsx.Read(file));
    }

    [Fact]
    public void AFileOverTheSizeLimit_IsRefused() =>
        Assert.Throws<InvalidDataException>(() => Xlsx.Read(new byte[Xlsx.MaxFileBytes + 1]));

    [Fact]
    public void CharactersXmlCannotCarry_AreDropped_SoTheWorkbookStillOpens()
    {
        var read = Xlsx.Read(Xlsx.Write([["a\u0001b"]]));

        Assert.Equal("ab", read[0].Fields[0]);
    }
}
