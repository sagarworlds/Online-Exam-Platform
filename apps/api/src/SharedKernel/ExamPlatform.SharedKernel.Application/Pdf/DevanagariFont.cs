using System.Buffers.Binary;
using HarfBuzzSharp;
using HbBuffer = HarfBuzzSharp.Buffer;
using HbFont = HarfBuzzSharp.Font;

namespace ExamPlatform.SharedKernel.Application.Pdf;

/// <summary>The font metrics a PDF font descriptor states, in thousandths of an em.</summary>
/// <param name="Left">The left edge of the box that holds every glyph.</param>
/// <param name="Bottom">The bottom edge of that box, below the baseline when negative.</param>
/// <param name="Right">The right edge of that box.</param>
/// <param name="Top">The top edge of that box.</param>
/// <param name="Ascent">The height above the baseline that a line of text needs.</param>
/// <param name="Descent">The depth below the baseline that a line of text needs, negative.</param>
/// <param name="CapHeight">The height of a capital letter.</param>
internal sealed record DevanagariMetrics(int Left, int Bottom, int Right, int Top, int Ascent, int Descent, int CapHeight);

/// <summary>
/// Noto Sans Devanagari, Regular: the font the PDF writer embeds for Hindi and Marathi (FR-38). It is read from the file embedded in this
/// assembly (see Pdf/Fonts/README.md), and text is shaped with HarfBuzz, so conjuncts, vowel signs and marks come out as a reader draws them.
/// </summary>
/// <remarks>
/// The font is read once and kept for the life of the process. HarfBuzz objects are not used by two threads at once, so every call into them
/// takes the same lock. Positions and widths are in thousandths of an em, the units the Helvetica metrics use.
/// </remarks>
internal sealed class DevanagariFont
{
    /// <summary>The name of the font file's resource in this assembly.</summary>
    public const string ResourceName = "ExamPlatform.SharedKernel.Application.Pdf.Fonts.NotoSansDevanagari-Regular.ttf";

    /// <summary>The number of units to the em. Every width and position is in these units, so 1000 is one em.</summary>
    public const int UnitsPerEm = 1000;

    private static readonly Lazy<DevanagariFont> Shared = new(static () => new DevanagariFont(ReadResource(ResourceName)));

    private readonly object _gate = new();
    private readonly byte[] _data;
    private readonly Face _face;
    private readonly HbFont _font;
    private readonly DevanagariMetrics _metrics;

    private DevanagariFont(byte[] data)
    {
        _data = data;
        using var blob = Blob.FromStream(new MemoryStream(data, writable: false));
        _face = new Face(blob, 0);
        _font = new HbFont(_face);
        _font.SetScale(UnitsPerEm, UnitsPerEm);
        _metrics = ReadMetrics(_face, _font);
    }

    /// <summary>The font, read on first use.</summary>
    public static DevanagariFont Default => Shared.Value;

    /// <summary>The font file's bytes, exactly as embedded. A PDF embeds them so a reader can draw the glyphs.</summary>
    public ReadOnlyMemory<byte> Data => _data;

    /// <summary>The metrics a PDF font descriptor states for this font.</summary>
    public DevanagariMetrics Metrics => _metrics;

    /// <summary>
    /// Shapes a run of Devanagari text: the glyphs the font draws for it, after conjuncts are formed, vowel signs are moved to where they are
    /// written, and kerning and marks are placed.
    /// </summary>
    /// <param name="text">The text to shape. Only Devanagari characters, joiners and spaces are expected.</param>
    /// <returns>The shaped glyphs, in visual order.</returns>
    public ShapedRun Shape(string text)
    {
        lock (_gate)
        {
            using var buffer = new HbBuffer();
            buffer.AddUtf16(text);
            buffer.Direction = Direction.LeftToRight;
            buffer.Script = Script.Devanagari;

            // One cluster per character where the font allows, so a vowel sign's glyph maps back to its own character. The default would
            // merge a consonant with its signs, and a glyph shared by two words would then copy the wrong text from one of them.
            buffer.ClusterLevel = ClusterLevel.MonotoneCharacters;
            _font.Shape(buffer, []);

            var infos = buffer.GlyphInfos;
            var positions = buffer.GlyphPositions;
            var glyphs = new ShapedGlyph[infos.Length];
            var pen = 0.0;
            for (var i = 0; i < infos.Length; i++)
            {
                var position = positions[i];
                glyphs[i] = new ShapedGlyph(
                    GlyphIdOf(infos[i].Codepoint), (int)infos[i].Cluster, pen + position.XOffset, position.YOffset, position.XAdvance);
                pen += position.XAdvance;
            }

            return new ShapedRun(text, glyphs);
        }
    }

    /// <summary>
    /// The width the font gives one glyph on its own, in thousandths of an em. A PDF font states this width for each glyph it uses.
    /// </summary>
    /// <param name="glyphId">The glyph's index in the font.</param>
    /// <returns>The advance width.</returns>
    public int AdvanceOf(ushort glyphId)
    {
        lock (_gate)
        {
            return _font.GetHorizontalGlyphAdvance(glyphId);
        }
    }

    private static ushort GlyphIdOf(uint codepoint) =>
        codepoint <= ushort.MaxValue ? (ushort)codepoint : throw new InvalidOperationException("The shaped glyph index does not fit a PDF CID.");

    private static DevanagariMetrics ReadMetrics(Face face, HbFont font)
    {
        if (!font.TryGetHorizontalFontExtents(out var extents))
            throw new InvalidOperationException("The embedded Devanagari font has no horizontal metrics.");

        // The head table gives the bounding box in the font's own units, so the values are scaled to thousandths of an em.
        using var head = face.ReferenceTable(new Tag('h', 'e', 'a', 'd'));
        var headTable = head.AsSpan();
        if (headTable.Length < 44)
            throw new InvalidOperationException("The embedded Devanagari font has no head table.");

        var unitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(headTable[18..]);
        if (unitsPerEm == 0)
            throw new InvalidOperationException("The embedded Devanagari font has no units per em.");

        int Scaled(short value) => (int)Math.Round(value * (double)UnitsPerEm / unitsPerEm);

        var left = Scaled(BinaryPrimitives.ReadInt16BigEndian(headTable[36..]));
        var bottom = Scaled(BinaryPrimitives.ReadInt16BigEndian(headTable[38..]));
        var right = Scaled(BinaryPrimitives.ReadInt16BigEndian(headTable[40..]));
        var top = Scaled(BinaryPrimitives.ReadInt16BigEndian(headTable[42..]));

        // The OS/2 table holds the cap height from version 2 on. Without it the ascent stands in, which only affects an informational field.
        using var os2 = face.ReferenceTable(new Tag('O', 'S', '/', '2'));
        var os2Table = os2.AsSpan();
        var capHeight = os2Table.Length >= 90 && BinaryPrimitives.ReadUInt16BigEndian(os2Table) >= 2
            ? Scaled(BinaryPrimitives.ReadInt16BigEndian(os2Table[88..]))
            : extents.Ascender;

        return new DevanagariMetrics(left, bottom, right, top, extents.Ascender, extents.Descender, capHeight);
    }

    private static byte[] ReadResource(string name)
    {
        using var stream = typeof(DevanagariFont).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The embedded font {name} is missing from the assembly.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
