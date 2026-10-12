using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace ExamPlatform.SharedKernel.Application.Pdf;

/// <summary>
/// The objects of the embedded Devanagari font (FR-38): a Type0 font with Identity-H over a CIDFontType2, its font descriptor, the whole font file
/// Flate-compressed, a ToUnicode map so a reader copies the text back, and the CIDToGIDMap that says which font glyph each CID draws.
/// </summary>
internal static class DevanagariFontObjects
{
    /// <summary>The number of objects the font takes.</summary>
    public const int Count = 6;

    private const string BaseFont = "NotoSansDevanagari-Regular";

    // The PDF specification limits a ToUnicode block to 100 mappings, and some readers reject a longer one.
    private const int MappingsPerBlock = 100;

    /// <summary>
    /// The bodies of the font's objects, numbered from <paramref name="first"/>: the Type0 font, its descendant, the descriptor, the font file, the
    /// ToUnicode map and the CIDToGIDMap, in that order.
    /// </summary>
    /// <param name="glyphs">The CIDs the document draws. Must not be empty.</param>
    /// <param name="first">The number of the Type0 font, the first of the six.</param>
    /// <returns>The six object bodies, in number order.</returns>
    public static IReadOnlyList<string> For(DevanagariGlyphs glyphs, int first)
    {
        var descendant = first + 1;
        var descriptor = first + 2;
        var file = first + 3;
        var toUnicode = first + 4;
        var cidToGid = first + 5;

        var font = DevanagariFont.Default;
        var metrics = font.Metrics;
        var data = font.Data.Span;
        var compressed = Flate(data);
        var used = glyphs.Ordered();
        var widths = string.Join(' ', used.Select(entry => $"{entry.Cid} [{entry.Width}]"));
        var cmap = ToUnicodeMap(used.Where(entry => entry.Text.Length > 0));
        var map = Flate(CidToGidBytes(used));

        return
        [
            $"<< /Type /Font /Subtype /Type0 /BaseFont /{BaseFont} /Encoding /Identity-H /DescendantFonts [{descendant} 0 R] " +
                $"/ToUnicode {toUnicode} 0 R >>",
            $"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{BaseFont} /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
                $"/FontDescriptor {descriptor} 0 R /W [{widths}] /CIDToGIDMap {cidToGid} 0 R >>",
            $"<< /Type /FontDescriptor /FontName /{BaseFont} /Flags 32 " +
                $"/FontBBox [{metrics.Left} {metrics.Bottom} {metrics.Right} {metrics.Top}] /ItalicAngle 0 /Ascent {metrics.Ascent} " +
                $"/Descent {metrics.Descent} /CapHeight {metrics.CapHeight} /StemV 80 /FontFile2 {file} 0 R >>",
            $"<< /Length {compressed.Length} /Length1 {data.Length} /Filter /FlateDecode >>\nstream\n{Latin1(compressed)}\nendstream",
            $"<< /Length {Encoding.Latin1.GetByteCount(cmap)} >>\nstream\n{cmap}endstream",
            $"<< /Length {map.Length} /Filter /FlateDecode >>\nstream\n{Latin1(map)}\nendstream",
        ];
    }

    /// <summary>Compresses a stream in the zlib format a PDF's FlateDecode filter reads.</summary>
    private static byte[] Flate(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(data);

        return output.ToArray();
    }

    /// <summary>
    /// The CIDToGIDMap: two bytes for each CID, from 0 up to the last one the document uses, holding the glyph that CID draws. A CID the document
    /// does not use draws glyph 0, which is the font's empty glyph.
    /// </summary>
    private static byte[] CidToGidBytes(IReadOnlyList<GlyphEntry> used)
    {
        var last = used.Count == 0 ? 0 : used.Max(entry => entry.Cid);
        var bytes = new byte[(last + 1) * 2];
        foreach (var entry in used)
        {
            bytes[entry.Cid * 2] = (byte)(entry.GlyphId >> 8);
            bytes[entry.Cid * 2 + 1] = (byte)(entry.GlyphId & 0xFF);
        }

        return bytes;
    }

    /// <summary>
    /// A ToUnicode CMap: each CID maps to the characters it copies as, in UTF-16. A CID that copies as several characters maps to all of them,
    /// which is how a conjunct copies as its letters.
    /// </summary>
    private static string ToUnicodeMap(IEnumerable<GlyphEntry> entries)
    {
        var mapped = entries.ToList();
        var cmap = new StringBuilder();
        cmap.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n");
        cmap.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        cmap.Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n");
        cmap.Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        for (var start = 0; start < mapped.Count; start += MappingsPerBlock)
        {
            var block = mapped.Skip(start).Take(MappingsPerBlock).ToList();
            cmap.Append(CultureInfo.InvariantCulture, $"{block.Count} beginbfchar\n");
            foreach (var entry in block)
                cmap.Append(CultureInfo.InvariantCulture, $"<{entry.Cid:X4}> <{PdfText.Utf16Hex(entry.Text)}>\n");

            cmap.Append("endbfchar\n");
        }

        cmap.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        return cmap.ToString();
    }

    /// <summary>The bytes as the file's Latin-1 text, one character a byte, which is how the document carries binary streams.</summary>
    private static string Latin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);
}
