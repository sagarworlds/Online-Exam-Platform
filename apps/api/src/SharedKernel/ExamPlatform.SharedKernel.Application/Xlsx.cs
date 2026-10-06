using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ExamPlatform.SharedKernel.Application;

/// <summary>
/// A minimal reader and writer for the one-sheet Excel workbooks (<c>.xlsx</c>) a module imports and exports (FR-6): rows of
/// text cells, like <see cref="Csv"/>, with no formulas, styles or types. An .xlsx file is a zip of XML parts, which
/// the framework can already read, so no spreadsheet library is needed. Reading takes the first sheet in the workbook.
/// </summary>
public static class Xlsx
{
    /// <summary>The most characters Excel itself keeps in one cell; a longer value cannot be written to a workbook it will open.</summary>
    public const int MaxCellLength = 32_767;

    /// <summary>The largest file <see cref="Read"/> accepts, so a hostile upload cannot be used to exhaust memory.</summary>
    public const int MaxFileBytes = 10 * 1024 * 1024;

    /// <summary>The most bytes any one part may expand to when read, which stops a small zip that unpacks to gigabytes.</summary>
    private const long MaxPartBytes = 64L * 1024 * 1024;

    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>One row read from a sheet.</summary>
    /// <param name="Line">The row's number in the sheet, counting from 1, so a report can name the row a person sees in Excel.</param>
    /// <param name="Fields">Its cells as text, padded with empty text to the width of the widest row.</param>
    public sealed record SheetRow(int Line, IReadOnlyList<string> Fields);

    /// <summary>Writes rows as a workbook with one sheet. Cells longer than <see cref="MaxCellLength"/> must be dealt with by the caller.</summary>
    /// <param name="rows">The rows, each a list of text cells.</param>
    public static byte[] Write(IEnumerable<IReadOnlyList<string>> rows)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>""");
            Add(zip, "_rels/.rels",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Add(zip, "xl/workbook.xml",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Questions" sheetId="1" r:id="rId1"/></sheets></workbook>""");
            Add(zip, "xl/_rels/workbook.xml.rels",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>""");

            var data = new XElement(Main + "sheetData");
            var line = 0;
            foreach (var row in rows)
            {
                line++;
                var r = new XElement(Main + "row", new XAttribute("r", line));
                for (var col = 0; col < row.Count; col++)
                {
                    if (row[col].Length == 0)
                    {
                        continue;
                    }

                    r.Add(new XElement(Main + "c",
                        new XAttribute("r", ColumnName(col) + line),
                        new XAttribute("t", "inlineStr"),
                        new XElement(Main + "is", new XElement(Main + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), XmlSafe(row[col])))));
                }

                data.Add(r);
            }

            var sheet = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(Main + "worksheet", data));
            using var entry = new StreamWriter(zip.CreateEntry("xl/worksheets/sheet1.xml").Open(), new UTF8Encoding(false));
            sheet.Save(entry);
        }

        return buffer.ToArray();
    }

    /// <summary>Reads the first sheet of a workbook into rows of text, skipping rows with nothing in them.</summary>
    /// <param name="file">The bytes of an .xlsx file.</param>
    /// <exception cref="InvalidDataException">The bytes are not a readable workbook, or are larger than allowed.</exception>
    public static IReadOnlyList<SheetRow> Read(byte[] file)
    {
        if (file.Length > MaxFileBytes)
        {
            throw new InvalidDataException("The workbook is too large.");
        }

        try
        {
            using var zip = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
            var sheetPath = FirstSheetPath(zip);
            var shared = SharedStrings(zip);
            var sheet = Load(zip, sheetPath) ?? throw new InvalidDataException("The workbook has no sheet.");

            var parsed = new List<(int Line, Dictionary<int, string> Cells)>();
            var width = 0;
            var nextLine = 0;
            foreach (var row in sheet.Descendants(Main + "row"))
            {
                nextLine = int.TryParse((string?)row.Attribute("r"), out var n) ? n : nextLine + 1;
                var cells = new Dictionary<int, string>();
                var nextCol = 0;
                foreach (var cell in row.Elements(Main + "c"))
                {
                    var col = ColumnIndex((string?)cell.Attribute("r")) ?? nextCol;
                    nextCol = col + 1;
                    var text = CellText(cell, shared);
                    if (text.Length > 0)
                    {
                        cells[col] = text;
                        width = Math.Max(width, col + 1);
                    }
                }

                if (cells.Count > 0)
                {
                    parsed.Add((nextLine, cells));
                }
            }

            return parsed
                .Select(p => new SheetRow(p.Line, Enumerable.Range(0, width).Select(c => p.Cells.GetValueOrDefault(c, string.Empty)).ToList()))
                .ToList();
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or IOException or InvalidOperationException)
        {
            throw new InvalidDataException("The file is not a readable Excel workbook.", ex);
        }
    }

    private static string FirstSheetPath(ZipArchive zip)
    {
        var workbook = Load(zip, "xl/workbook.xml") ?? throw new InvalidDataException("Not a workbook.");
        var id = (string?)workbook.Descendants(Main + "sheet").FirstOrDefault()?.Attribute(RelNs + "id");
        var rels = Load(zip, "xl/_rels/workbook.xml.rels");
        var target = rels?.Descendants(PackageRel + "Relationship").FirstOrDefault(r => (string?)r.Attribute("Id") == id)?.Attribute("Target")?.Value;
        if (target is null)
        {
            throw new InvalidDataException("The workbook has no sheet.");
        }

        return target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
    }

    private static List<string> SharedStrings(ZipArchive zip)
    {
        var doc = Load(zip, "xl/sharedStrings.xml");
        if (doc is null)
        {
            return [];
        }

        // An item is its plain text or its rich-text runs; phonetic guides (rPh) are not part of the value.
        return doc.Descendants(Main + "si")
            .Select(si => string.Concat(si.Descendants(Main + "t").Where(t => t.Parent?.Name != Main + "rPh").Select(t => t.Value)))
            .ToList();
    }

    private static string CellText(XElement cell, List<string> shared)
    {
        var type = (string?)cell.Attribute("t");
        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants(Main + "t").Select(t => t.Value));
        }

        var value = cell.Element(Main + "v")?.Value ?? string.Empty;
        return type switch
        {
            "s" => int.TryParse(value, out var i) && i >= 0 && i < shared.Count ? shared[i] : string.Empty,
            "b" => value == "1" ? "true" : "false",
            _ => value,
        };
    }

    private static XDocument? Load(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        if (entry is null)
        {
            return null;
        }

        if (entry.Length > MaxPartBytes)
        {
            throw new InvalidDataException("The workbook is too large.");
        }

        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxPartBytes });
        return XDocument.Load(reader);
    }

    private static void Add(ZipArchive zip, string path, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    /// <summary>Drops the characters XML cannot carry, which no question can usefully contain either.</summary>
    private static string XmlSafe(string value) => new(value.Where(c => XmlConvert.IsXmlChar(c) || char.IsSurrogate(c)).ToArray());

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            name = (char)('A' + (n - 1) % 26) + name;
        }

        return name;
    }

    private static int? ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return null;
        }

        var index = 0;
        var letters = 0;
        foreach (var c in reference.TakeWhile(char.IsLetter))
        {
            index = index * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
            letters++;
        }

        return letters == 0 ? null : index - 1;
    }
}
