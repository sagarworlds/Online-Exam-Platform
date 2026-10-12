using System.Globalization;

namespace ExamPlatform.SharedKernel.Application.Pdf;

/// <summary>A table column: its heading, its width in points, and whether its figures sit on the right.</summary>
/// <param name="Header">The heading printed at the top of the column, on every page the table starts on.</param>
/// <param name="Width">The column's width in points.</param>
/// <param name="RightAligned">Whether the cell text sits against the column's right edge, as figures do.</param>
public sealed record PdfColumn(string Header, double Width, bool RightAligned = false);

/// <summary>
/// Lays out a report of headings, paragraphs and tables over as many A4 pages as it needs (FR-38), on top of <see cref="PdfDocument"/>.
/// </summary>
/// <remarks>
/// The layout is one downward cursor: each block is measured, moved to a new page if it does not fit, and drawn. A table that runs over a
/// page break repeats its header on the next page, so each page can be read without the one before. A cell longer than
/// <see cref="MaxCellLines"/> lines is cut with three dots, which keeps a row's height bounded. Every page has a footer: the report's title on
/// the left and the page number on the right, written when the report is built, once the page count is known.
/// </remarks>
public sealed class PdfReport
{
    /// <summary>The left and right margin, in points.</summary>
    public const double Margin = 50;

    /// <summary>The space kept clear at the top of each page, in points.</summary>
    public const double TopMargin = 60;

    /// <summary>The space kept clear at the bottom of each page for the footer, in points.</summary>
    public const double BottomMargin = 60;

    /// <summary>The most lines one table cell may take before it is cut with three dots.</summary>
    public const int MaxCellLines = 6;

    /// <summary>The width between the left and right margins, in points: the widest a table may be.</summary>
    public const double UsableWidth = PdfDocument.PageWidth - 2 * Margin;

    private const double BodySize = 9.5;
    private const double TableSize = 8.5;
    private const double HeadingSize = 14;
    private const double SubHeadingSize = 11;
    private const double FooterSize = 8;
    private const double CellPadding = 4;
    private const double LineSpacing = 1.3;

    private static readonly PdfColor RuleColor = PdfColor.Gray(0.75);
    private static readonly PdfColor HeaderFill = PdfColor.Gray(0.9);

    private readonly PdfDocument _document;
    private readonly string _title;
    private readonly List<PdfPage> _pages = [];
    private PdfPage _page;
    private double _cursor;
    private bool _built;

    /// <summary>Starts a report with its first page ready for content.</summary>
    /// <param name="title">The report's title, shown in each page's footer and in the file's information. A character the font cannot print is replaced.</param>
    public PdfReport(string title)
    {
        _title = Prepare(title);
        _document = new PdfDocument(_title);
        _page = NewPage();
    }

    /// <summary>Whether any text had a character the built-in font cannot print, so it was replaced with a question mark.</summary>
    /// <remarks>
    /// A report that sets this should say so to its reader, because a question mark stands for a letter the file does not show. The CSV of the
    /// same figures carries the text in full.
    /// </remarks>
    public bool SubstitutedCharacters { get; private set; }

    /// <summary>Adds a heading in bold, above the content that follows it.</summary>
    /// <param name="text">The heading's text.</param>
    /// <exception cref="InvalidOperationException">The report has already been built.</exception>
    public void Heading(string text) => WriteBlock(Prepare(text), PdfFont.Bold, HeadingSize, spaceBefore: 4, spaceAfter: 6);

    /// <summary>Adds a smaller bold heading, for a section within the report.</summary>
    /// <param name="text">The heading's text.</param>
    /// <exception cref="InvalidOperationException">The report has already been built.</exception>
    public void SubHeading(string text) => WriteBlock(Prepare(text), PdfFont.Bold, SubHeadingSize, spaceBefore: 8, spaceAfter: 4);

    /// <summary>Adds a paragraph of body text, wrapped to the margins.</summary>
    /// <param name="text">The paragraph's text. Line breaks become spaces.</param>
    /// <exception cref="InvalidOperationException">The report has already been built.</exception>
    public void Paragraph(string text) => WriteBlock(Prepare(text), PdfFont.Regular, BodySize, spaceBefore: 0, spaceAfter: 6);

    /// <summary>
    /// Adds a table: a shaded header row, then one row per entry, each cell wrapped to its column. A row that does not fit on the page moves to
    /// the next one, and the header is drawn again there.
    /// </summary>
    /// <param name="columns">The columns, in order. Their widths must add up to no more than <see cref="UsableWidth"/>.</param>
    /// <param name="rows">The rows. Each has one cell per column; a cell may be empty.</param>
    /// <exception cref="ArgumentException">The columns are wider than the margins allow, there are none, or a row does not have one cell per column.</exception>
    /// <exception cref="InvalidOperationException">The report has already been built.</exception>
    public void Table(IReadOnlyList<PdfColumn> columns, IEnumerable<IReadOnlyList<string>> rows)
    {
        if (columns.Count == 0)
            throw new ArgumentException("A table needs at least one column.", nameof(columns));
        if (columns.Sum(c => c.Width) > UsableWidth + 0.01)
            throw new ArgumentException($"The columns are wider than the {UsableWidth.ToString(CultureInfo.InvariantCulture)} points between the margins.", nameof(columns));

        EnsureOpen();
        var headers = columns.Select(c => Prepare(c.Header)).ToList();
        DrawHeader(columns, headers);

        foreach (var row in rows)
        {
            if (row.Count != columns.Count)
                throw new ArgumentException($"Every row needs {columns.Count} cells; one has {row.Count}.", nameof(rows));

            var cells = columns.Select((column, i) => WrapCell(Prepare(row[i]), column.Width, PdfFont.Regular)).ToList();
            var height = RowHeight(cells);
            if (_cursor - height < BottomMargin)
            {
                _page = NewPage();
                DrawHeader(columns, headers);
            }

            DrawCells(columns, cells, PdfFont.Regular);
            _page.Line(Margin, _cursor - height, Margin + UsableWidth, _cursor - height, 0.5, RuleColor);
            _cursor -= height;
        }

        _cursor -= BodySize;
    }

    /// <summary>
    /// Finishes the report: writes each page's footer, now that the number of pages is known, and assembles the file.
    /// </summary>
    /// <returns>The PDF file's bytes.</returns>
    /// <exception cref="InvalidOperationException">The report has already been built.</exception>
    public byte[] Build()
    {
        EnsureOpen();
        _built = true;

        for (var i = 0; i < _pages.Count; i++)
        {
            var footer = $"Page {(i + 1).ToString(CultureInfo.InvariantCulture)} of {_pages.Count.ToString(CultureInfo.InvariantCulture)}";
            var footerLeft = PdfText.Truncate(_title, PdfFont.Regular, FooterSize, UsableWidth - 80);
            _pages[i].Text(footerLeft, PdfFont.Regular, FooterSize, Margin, 30);
            _pages[i].Text(footer, PdfFont.Regular, FooterSize, PdfDocument.PageWidth - Margin - PdfText.Width(footer, PdfFont.Regular, FooterSize), 30);
        }

        return _document.Build();
    }

    /// <summary>Makes text printable, recording in <see cref="SubstitutedCharacters"/> whether a character had to be replaced.</summary>
    private string Prepare(string text)
    {
        // Line breaks and tabs are layout, not glyphs, so they do not count as substitutions.
        if (!PdfText.CanShow(text.ReplaceLineEndings(" ").Replace('\t', ' ')))
            SubstitutedCharacters = true;

        return PdfText.Printable(text);
    }

    private void EnsureOpen()
    {
        if (_built)
            throw new InvalidOperationException("The report has already been built.");
    }

    private PdfPage NewPage()
    {
        var page = _document.AddPage();
        _pages.Add(page);
        _cursor = PdfDocument.PageHeight - TopMargin;
        return page;
    }

    private void WriteBlock(string text, PdfFont font, double size, double spaceBefore, double spaceAfter)
    {
        EnsureOpen();
        var lineHeight = size * LineSpacing;
        _cursor -= spaceBefore;
        foreach (var line in PdfText.Wrap(text, font, size, UsableWidth))
        {
            if (_cursor - lineHeight < BottomMargin)
                _page = NewPage();

            _page.Text(line, font, size, Margin, _cursor - size);
            _cursor -= lineHeight;
        }

        _cursor -= spaceAfter;
    }

    /// <summary>Draws the shaded header row, starting a new page first when it would not fit on this one.</summary>
    private void DrawHeader(IReadOnlyList<PdfColumn> columns, IReadOnlyList<string> headers)
    {
        var cells = columns.Select((column, i) => WrapCell(headers[i], column.Width, PdfFont.Bold)).ToList();
        var height = RowHeight(cells);
        if (_cursor - height < BottomMargin)
            _page = NewPage();

        _page.FillRectangle(Margin, _cursor - height, UsableWidth, height, HeaderFill);
        DrawCells(columns, cells, PdfFont.Bold);
        _cursor -= height;
    }

    /// <summary>Writes each cell's lines, starting at the row's top edge, which is the cursor.</summary>
    private void DrawCells(IReadOnlyList<PdfColumn> columns, IReadOnlyList<IReadOnlyList<string>> cells, PdfFont font)
    {
        var lineHeight = TableSize * LineSpacing;
        var x = Margin;
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            for (var line = 0; line < cells[i].Count; line++)
            {
                var text = cells[i][line];
                var left = column.RightAligned
                    ? x + column.Width - CellPadding - PdfText.Width(text, font, TableSize)
                    : x + CellPadding;
                var top = _cursor - CellPadding - line * lineHeight;
                _page.Text(text, font, TableSize, left, top - TableSize);
            }

            x += column.Width;
        }
    }

    /// <summary>Wraps one cell to its column, cutting a cell that runs past <see cref="MaxCellLines"/> with three dots.</summary>
    private static IReadOnlyList<string> WrapCell(string text, double columnWidth, PdfFont font)
    {
        var width = columnWidth - 2 * CellPadding;
        var lines = PdfText.Wrap(text, font, TableSize, width);
        if (lines.Count <= MaxCellLines)
            return lines;

        // The last drawn line is cut with dots, so the reader sees that the cell goes on. Only that line is measured, so a very long cell
        // costs no more than a short one.
        var kept = lines.Take(MaxCellLines - 1).ToList();
        kept.Add(PdfText.Ellipsize(lines[MaxCellLines - 1], font, TableSize, width));
        return kept;
    }

    /// <summary>A row is as tall as its tallest cell, plus padding above and below; an empty row is still one line tall.</summary>
    private static double RowHeight(IReadOnlyList<IReadOnlyList<string>> cells)
    {
        var lines = Math.Max(1, cells.Max(c => c.Count));
        return 2 * CellPadding + lines * TableSize * LineSpacing;
    }
}
