using ExamPlatform.SharedKernel.Application.Pdf;
using HarfBuzzSharp;
using HbBuffer = HarfBuzzSharp.Buffer;
using HbFont = HarfBuzzSharp.Font;

namespace ExamPlatform.SharedKernel.UnitTests.Pdf;

/// <summary>
/// The embedded Devanagari font, shaped by HarfBuzz directly, in the same settings the writer uses. The writer's glyphs, positions and widths are
/// checked against this, so a test never compares the writer with its own output.
/// </summary>
internal static class ReferenceShaping
{
    /// <summary>The resource name of the embedded font file.</summary>
    public const string FontResource = "ExamPlatform.SharedKernel.Application.Pdf.Fonts.NotoSansDevanagari-Regular.ttf";

    private static readonly Lazy<Reference> Shared = new(() => new Reference(FontBytes()));

    /// <summary>The font file exactly as it is embedded in the assembly.</summary>
    public static byte[] FontBytes()
    {
        using var stream = typeof(PdfText).Assembly.GetManifestResourceStream(FontResource)
            ?? throw new InvalidOperationException("The embedded font is missing.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>The glyphs HarfBuzz gives the text, in visual order, with each glyph's origin in thousandths of an em from the text's start.</summary>
    public static IReadOnlyList<(ushort GlyphId, double X, double Y, double Advance)> Shape(string text) => Shared.Value.Shape(text);

    /// <summary>The width of the shaped text, in thousandths of an em.</summary>
    public static double Width(string text) => Shape(text).Sum(glyph => glyph.Advance);

    /// <summary>The width of one glyph on its own, in thousandths of an em.</summary>
    public static int GlyphAdvance(ushort glyphId) => Shared.Value.Advance(glyphId);

    private sealed class Reference
    {
        private readonly object _gate = new();
        private readonly Face _face;
        private readonly HbFont _font;

        public Reference(byte[] data)
        {
            // Same as the writer: HarfBuzz copies the bytes, so the managed array's lifetime does not matter.
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(data, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                using var blob = new Blob(handle.AddrOfPinnedObject(), data.Length, MemoryMode.Duplicate);
                _face = new Face(blob, 0);
            }
            finally
            {
                handle.Free();
            }

            _font = new HbFont(_face);
            _font.SetScale(1000, 1000);
        }

        public IReadOnlyList<(ushort GlyphId, double X, double Y, double Advance)> Shape(string text)
        {
            lock (_gate)
            {
                using var buffer = new HbBuffer();
                buffer.AddUtf16(text);
                buffer.Direction = Direction.LeftToRight;
                buffer.Script = Script.Devanagari;
                buffer.ClusterLevel = ClusterLevel.MonotoneCharacters;
                _font.Shape(buffer, []);

                var infos = buffer.GlyphInfos;
                var positions = buffer.GlyphPositions;
                var glyphs = new List<(ushort, double, double, double)>(infos.Length);
                var pen = 0.0;
                for (var i = 0; i < infos.Length; i++)
                {
                    glyphs.Add(((ushort)infos[i].Codepoint, pen + positions[i].XOffset, positions[i].YOffset, positions[i].XAdvance));
                    pen += positions[i].XAdvance;
                }

                return glyphs;
            }
        }

        public int Advance(ushort glyphId)
        {
            lock (_gate)
            {
                return _font.GetHorizontalGlyphAdvance(glyphId);
            }
        }
    }
}
