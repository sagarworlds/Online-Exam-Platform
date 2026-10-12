namespace ExamPlatform.SharedKernel.Application.Pdf;

/// <summary>
/// One glyph of a shaped run. Its position is measured from the start of the run, in thousandths of an em, so it can be scaled to any size.
/// </summary>
/// <param name="GlyphId">The glyph's index in the font. In the PDF it is also the glyph's CID.</param>
/// <param name="Cluster">The index, in the run's text, of the first character the glyph stands for.</param>
/// <param name="X">Where the glyph's origin sits: the pen's position before the glyph, plus the glyph's own horizontal offset.</param>
/// <param name="Y">Where the glyph's origin sits above the baseline, after the glyph's own vertical offset (a mark's position over its base).</param>
/// <param name="Advance">How far the pen moves after the glyph.</param>
internal readonly record struct ShapedGlyph(ushort GlyphId, int Cluster, double X, double Y, double Advance);

/// <summary>A glyph together with the characters of the text it was made from.</summary>
/// <param name="Glyph">The glyph and where it sits.</param>
/// <param name="Text">The characters the glyph stands for. Empty when an earlier glyph in the same cluster already carries them.</param>
internal readonly record struct LogicalGlyph(ShapedGlyph Glyph, string Text);

/// <summary>
/// A run of Devanagari text after shaping: the glyphs a reader draws, in the order they are written on the line.
/// </summary>
/// <remarks>
/// Shaping can reorder glyphs (a vowel sign written before its consonant) and merge characters into one glyph (a conjunct). So the glyphs are
/// kept in the visual order they were laid out in, and <see cref="InLogicalOrder"/> gives the order of the characters, for copy and search.
/// </remarks>
internal sealed class ShapedRun
{
    /// <summary>Starts a shaped run.</summary>
    /// <param name="text">The text that was shaped.</param>
    /// <param name="glyphs">The glyphs in visual order, from the left of the run.</param>
    public ShapedRun(string text, IReadOnlyList<ShapedGlyph> glyphs)
    {
        Text = text;
        Glyphs = glyphs;
        Advance = glyphs.Sum(g => g.Advance);
    }

    /// <summary>The text that was shaped.</summary>
    public string Text { get; }

    /// <summary>The glyphs in visual order, from the left of the run.</summary>
    public IReadOnlyList<ShapedGlyph> Glyphs { get; }

    /// <summary>The width of the run, in thousandths of an em: the pen's travel over all its glyphs.</summary>
    public double Advance { get; }

    /// <summary>
    /// The glyphs in the order of the characters they stand for. Glyphs of one cluster stay together, in their visual order; the first of
    /// them carries the cluster's characters and the rest carry none, so copying the text does not repeat them.
    /// </summary>
    /// <returns>Each glyph with the characters it stands for.</returns>
    public IReadOnlyList<LogicalGlyph> InLogicalOrder()
    {
        var spans = ClusterSpans();
        var seen = new HashSet<int>();
        var ordered = new List<LogicalGlyph>(Glyphs.Count);

        // OrderBy is stable, so glyphs of one cluster keep the order they were laid out in.
        foreach (var glyph in Glyphs.OrderBy(g => g.Cluster))
        {
            var text = seen.Add(glyph.Cluster) ? spans[glyph.Cluster] : string.Empty;
            ordered.Add(new LogicalGlyph(glyph, text));
        }

        return ordered;
    }

    /// <summary>
    /// The characters each cluster covers: from its start to the next cluster's start, so a character that makes no glyph of its own (a joiner,
    /// say) still belongs to some cluster and is copied with it.
    /// </summary>
    private Dictionary<int, string> ClusterSpans()
    {
        var starts = Glyphs.Select(g => g.Cluster).Distinct().Order().ToList();
        var spans = new Dictionary<int, string>(starts.Count);
        for (var k = 0; k < starts.Count; k++)
        {
            var from = k == 0 ? 0 : Math.Min(starts[k], Text.Length);
            var to = k + 1 < starts.Count ? Math.Min(starts[k + 1], Text.Length) : Text.Length;
            spans[starts[k]] = Text[from..Math.Max(from, to)];
        }

        return spans;
    }
}
