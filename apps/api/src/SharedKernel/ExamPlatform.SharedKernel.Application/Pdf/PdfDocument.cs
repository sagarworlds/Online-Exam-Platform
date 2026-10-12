using System.Globalization;
using System.Text;

namespace ExamPlatform.SharedKernel.Application.Pdf;

/// <summary>
/// A PDF document of one or more A4 pages, written by hand: the header, the objects, and a cross-reference table computed from the real byte
/// offsets. It uses no third-party library, so there is no licence to carry and no font file to ship (FR-34, FR-38).
/// </summary>
/// <remarks>
/// Object numbers are fixed by the layout, so the first page of a one-page document has the same numbers the certificate has always used: the
/// catalogue is 1, the page tree 2, the pages from 3, the two fonts after them, the page contents after those, and the document information
/// last. Every string is escaped, so a name with brackets or a backslash cannot break the file. Every character is below 0x100 (see
/// <see cref="PdfText.CanShow"/>), so Latin-1 writes each one as the single byte the offsets count.
/// </remarks>
public sealed class PdfDocument
{
    /// <summary>The width of an A4 page, in points.</summary>
    public const int PageWidth = 595;

    /// <summary>The height of an A4 page, in points.</summary>
    public const int PageHeight = 842;

    private readonly string _title;
    private readonly List<PdfPage> _pages = [];

    /// <summary>Starts an empty document. Pages are added with <see cref="AddPage"/>.</summary>
    /// <param name="title">The document's title, shown in the reader's title bar and kept in the file's information.</param>
    /// <exception cref="ArgumentException"><paramref name="title"/> has a character the built-in font cannot print.</exception>
    public PdfDocument(string title)
    {
        if (!PdfText.CanShow(title))
            throw new ArgumentException("The title uses a character the built-in font cannot show.", nameof(title));

        _title = title;
    }

    /// <summary>The number of pages added so far.</summary>
    public int PageCount => _pages.Count;

    /// <summary>Adds an empty A4 page after the existing ones.</summary>
    /// <returns>The page, to draw on until the document is built.</returns>
    public PdfPage AddPage()
    {
        var page = new PdfPage();
        _pages.Add(page);
        return page;
    }

    /// <summary>Assembles the file: the header, every object, the cross-reference table and the trailer.</summary>
    /// <returns>The PDF file's bytes.</returns>
    /// <exception cref="InvalidOperationException">The document has no pages; a PDF with none is not one a reader can show.</exception>
    public byte[] Build()
    {
        if (_pages.Count == 0)
            throw new InvalidOperationException("A PDF needs at least one page.");

        var objects = ObjectsOf();
        var sb = new StringBuilder();
        // The header's second line holds bytes above 0x7F, so transfer tools treat the file as binary rather than text. Written as escapes
        // so the source file's own encoding cannot change them.
        sb.Append("%PDF-1.4\n%\u00E2\u00E3\u00CF\u00D3\n");

        // Every character is below 0x100, so a string's length is its byte count, and an offset is the length so far.
        var offsets = new List<int>(objects.Count);
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = sb.Length;
        sb.Append($"xref\n0 {objects.Count + 1}\n");
        // Entry 0 is the free head; every other entry is exactly twenty bytes, as the format requires.
        sb.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
            sb.Append($"{offset:D10} 00000 n \n");

        sb.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /Info {objects.Count} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    /// <summary>Lays out the object bodies in the order the layout fixes their numbers.</summary>
    private List<string> ObjectsOf()
    {
        var n = _pages.Count;
        var regularFont = 3 + n;
        var boldFont = regularFont + 1;
        var firstContent = boldFont + 1;
        var info = firstContent + n;

        var objects = new List<string>(info)
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', Enumerable.Range(0, n).Select(i => $"{3 + i} 0 R"))}] /Count {n} >>",
        };

        for (var i = 0; i < n; i++)
        {
            objects.Add(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] " +
                $"/Resources << /Font << /F1 {regularFont} 0 R /F2 {boldFont} 0 R >> >> /Contents {firstContent + i} 0 R >>");
        }

        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

        foreach (var page in _pages)
        {
            var content = page.Content;
            objects.Add($"<< /Length {Encoding.Latin1.GetByteCount(content)} >>\nstream\n{content}endstream");
        }

        objects.Add($"<< /Title ({PdfText.Escape(_title)}) /Producer (Online Exam Platform) >>");
        return objects;
    }
}

/// <summary>
/// One A4 page's drawing: text, rules and filled boxes, in PDF's coordinate system, where the origin is the bottom left corner and y grows
/// upwards. Coordinates are in points.
/// </summary>
public sealed class PdfPage
{
    private readonly StringBuilder _content = new();

    /// <summary>The page's content stream, as the PDF operators that draw it.</summary>
    internal string Content => _content.ToString();

    /// <summary>Draws one line of text with its baseline at the given point.</summary>
    /// <param name="text">The text, printable (see <see cref="PdfText.CanShow"/>); it is printed as written.</param>
    /// <param name="font">The face to set the text in.</param>
    /// <param name="size">The font size in points.</param>
    /// <param name="x">The left edge of the text, in points from the page's left edge.</param>
    /// <param name="y">The baseline, in points from the page's bottom edge.</param>
    /// <exception cref="ArgumentException">The text has a character the built-in font cannot print.</exception>
    public void Text(string text, PdfFont font, double size, double x, double y)
    {
        if (!PdfText.CanShow(text))
            throw new ArgumentException("The text uses a character the built-in font cannot show.", nameof(text));

        var face = font == PdfFont.Bold ? "F2" : "F1";
        _content.Append($"BT /{face} {Num(size)} Tf {Num(x)} {Num(y)} Td ({PdfText.Escape(text)}) Tj ET\n");
    }

    /// <summary>Draws the outline of a rectangle.</summary>
    /// <param name="x">The left edge, in points.</param>
    /// <param name="y">The bottom edge, in points.</param>
    /// <param name="width">The width, in points.</param>
    /// <param name="height">The height, in points.</param>
    /// <param name="lineWidth">The stroke's width, in points.</param>
    /// <param name="color">The stroke's colour.</param>
    public void StrokeRectangle(double x, double y, double width, double height, double lineWidth, PdfColor color)
    {
        // Wrapped in a saved graphics state, so the colour does not leak into the text drawn after the box.
        _content.Append($"q {Color(color)} RG {Num(lineWidth)} w {Num(x)} {Num(y)} {Num(width)} {Num(height)} re S Q\n");
    }

    /// <summary>Fills a rectangle with a solid colour.</summary>
    /// <param name="x">The left edge, in points.</param>
    /// <param name="y">The bottom edge, in points.</param>
    /// <param name="width">The width, in points.</param>
    /// <param name="height">The height, in points.</param>
    /// <param name="color">The fill's colour.</param>
    public void FillRectangle(double x, double y, double width, double height, PdfColor color)
    {
        // A fill colour is the colour text is drawn in, so it is restored afterwards: the text below must not inherit it.
        _content.Append($"q {Color(color)} rg {Num(x)} {Num(y)} {Num(width)} {Num(height)} re f Q\n");
    }

    /// <summary>Draws a straight line.</summary>
    /// <param name="x1">The start's x, in points.</param>
    /// <param name="y1">The start's y, in points.</param>
    /// <param name="x2">The end's x, in points.</param>
    /// <param name="y2">The end's y, in points.</param>
    /// <param name="lineWidth">The line's width, in points.</param>
    /// <param name="color">The line's colour.</param>
    public void Line(double x1, double y1, double x2, double y2, double lineWidth, PdfColor color)
    {
        _content.Append($"q {Color(color)} RG {Num(lineWidth)} w {Num(x1)} {Num(y1)} m {Num(x2)} {Num(y2)} l S Q\n");
    }

    private static string Color(PdfColor color) => $"{Num(color.Red)} {Num(color.Green)} {Num(color.Blue)}";

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
