namespace ExamPlatform.SharedKernel.Application.Pdf;

/// <summary>
/// The glyphs of the embedded Devanagari font that one document draws. Each use is given a CID that stands for one glyph and the characters it
/// was drawn for, so the ToUnicode map can say what each CID copies as. A glyph can be drawn for different characters in different words (a
/// pre-base vowel sign, for one), and one glyph ID cannot copy as both, so such a glyph takes a second CID for its other use.
/// </summary>
/// <remarks>
/// A glyph's first use keeps its glyph ID as its CID, so most CIDs are the font's own glyph IDs. Later uses take CIDs past the font's last glyph.
/// The font's CIDToGIDMap says which glyph each CID draws.
/// </remarks>
internal sealed class DevanagariGlyphs
{
    private readonly Dictionary<(ushort GlyphId, string Text), GlyphEntry> _uses = new();
    private readonly HashSet<ushort> _firstUsed = [];
    private int _nextCid = -1;

    /// <summary>Whether any Devanagari text was drawn, so the font has to be written into the file.</summary>
    public bool Any => _uses.Count > 0;

    /// <summary>
    /// Records that a glyph is drawn for the given characters, and returns the CID the content stream must draw it with.
    /// </summary>
    /// <param name="glyphId">The glyph's index in the font.</param>
    /// <param name="width">The glyph's advance width, in thousandths of an em.</param>
    /// <param name="text">The characters the glyph is drawn for. Empty when the glyph carries none (a second glyph of a cluster).</param>
    /// <returns>The CID of this glyph-and-characters pair.</returns>
    public ushort Use(ushort glyphId, int width, string text)
    {
        if (_uses.TryGetValue((glyphId, text), out var existing))
            return existing.Cid;

        if (_nextCid < 0)
            _nextCid = DevanagariFont.Default.GlyphCount;

        // The first use of a glyph keeps its glyph ID; a glyph's later uses are given the next free CID.
        var cid = _firstUsed.Add(glyphId) ? glyphId : _nextCid++;
        if (cid > ushort.MaxValue)
            throw new InvalidOperationException("The document uses more Devanagari glyph forms than a font can address.");

        _uses[(glyphId, text)] = new GlyphEntry((ushort)cid, glyphId, width, text);
        return (ushort)cid;
    }

    /// <summary>Every CID the document draws, in CID order: the width array and the ToUnicode map are written from these.</summary>
    public IReadOnlyList<GlyphEntry> Ordered() => _uses.Values.OrderBy(entry => entry.Cid).ToList();
}

/// <summary>A CID the document draws: the glyph it draws, its advance width and the characters it copies as.</summary>
/// <param name="Cid">The CID the content stream draws with.</param>
/// <param name="GlyphId">The font glyph the CID draws.</param>
/// <param name="Width">The glyph's advance width, in thousandths of an em.</param>
/// <param name="Text">The characters the CID copies as; empty when none.</param>
internal sealed record GlyphEntry(ushort Cid, ushort GlyphId, int Width, string Text);
