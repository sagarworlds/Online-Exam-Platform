using System.Globalization;
using System.Text;

namespace ExamPlatform.SharedKernel.Application.Pdf;

/// <summary>
/// Text rules for the hand-written PDF writer: which characters the built-in fonts can print, how text is escaped inside a PDF string, and how
/// a line is measured, wrapped and shortened to fit a column.
/// </summary>
/// <remarks>
/// The built-in fonts cover printable ASCII and the Latin-1 letters from 0xA0 to 0xFF. Anything else (Devanagari, for one) has no glyph, so
/// it is refused or replaced rather than printed as a box. Measuring uses the real font widths, so wrapping never runs text past its column.
/// </remarks>
public static class PdfText
{
    /// <summary>The mark that stands in for a character the built-in font cannot print.</summary>
    public const char Substitute = '?';

    /// <summary>Whether every character of the text can be printed with the built-in fonts: printable ASCII and the Latin-1 letters from 0xA0 to 0xFF.</summary>
    /// <param name="text">The text to print.</param>
    /// <returns>True when nothing in the text needs a glyph the font lacks.</returns>
    public static bool CanShow(string text) => text.All(c => c is >= ' ' and <= '~' || c is >= '\u00A0' and <= '\u00FF');

    /// <summary>
    /// Makes text printable: line breaks and tabs become spaces, runs of spaces collapse, and every character the font cannot print becomes
    /// <see cref="Substitute"/>. A surrogate pair (one character outside the Basic Multilingual Plane) is replaced once, not twice.
    /// </summary>
    /// <param name="text">The text to make printable.</param>
    /// <returns>Text that <see cref="CanShow"/> accepts.</returns>
    public static string Printable(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value is '\t' or '\r' or '\n')
                builder.Append(' ');
            else if (rune.Value is >= 0x20 and <= 0x7E or >= 0xA0 and <= 0xFF)
                builder.Append((char)rune.Value);
            else
                builder.Append(Substitute);
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Escapes the characters that have a meaning inside a PDF literal string, so the text is printed as written.
    /// </summary>
    /// <param name="text">Printable text (see <see cref="CanShow"/>).</param>
    /// <returns>The text ready to sit between the parentheses of a string.</returns>
    public static string Escape(string text) => text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    /// <summary>
    /// The width of a line of text, in points, when set in the given face and size. Devanagari is measured from its shaped glyphs, the advances
    /// it is drawn with, and everything else from the Helvetica metrics.
    /// </summary>
    /// <param name="text">Printable text (see <see cref="CanShow"/>).</param>
    /// <param name="font">The face the Latin text is set in. Devanagari has only the one face.</param>
    /// <param name="size">The font size in points.</param>
    /// <returns>The width in points.</returns>
    public static double Width(string text, PdfFont font, double size)
    {
        var units = 0.0;
        foreach (var segment in Segments(text))
            units += segment.Devanagari ? DevanagariFont.Default.Shape(segment.Text).Advance : LatinUnits(segment.Text, font);

        return units * size / 1000.0;
    }

    /// <summary>The width of Latin text in thousandths of an em, from the Helvetica metrics.</summary>
    private static int LatinUnits(string text, PdfFont font)
    {
        var units = 0;
        foreach (var c in text)
            units += PdfFontMetrics.WidthOf(c, font);

        return units;
    }

    /// <summary>
    /// Splits a line into runs of one script: each Devanagari run, shaped with the embedded font, and the Latin text between them, which the
    /// Helvetica faces print. A space at either end of a Devanagari run belongs to the Latin text, so a run is a phrase, not a space-padded span.
    /// </summary>
    /// <param name="text">Printable text (see <see cref="CanShow"/>).</param>
    /// <returns>The runs in order; their texts join back to the original.</returns>
    internal static IReadOnlyList<TextSegment> Segments(string text)
    {
        var segments = new List<TextSegment>();
        var latin = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            if (!IsDevanagari(text[i]))
            {
                latin.Append(text[i]);
                i++;
                continue;
            }

            var start = i;
            while (i < text.Length && (IsDevanagari(text[i]) || IsJoiner(text[i]) || text[i] == ' '))
                i++;

            // The run ends at its last character that is not a space; the spaces after it are Latin, and the loop reads them next.
            var end = i;
            while (text[end - 1] == ' ')
                end--;

            i = end;
            if (latin.Length > 0)
            {
                segments.Add(new TextSegment(latin.ToString(), Devanagari: false));
                latin.Clear();
            }

            segments.Add(new TextSegment(text[start..end], Devanagari: true));
        }

        if (latin.Length > 0)
            segments.Add(new TextSegment(latin.ToString(), Devanagari: false));

        return segments;
    }

    /// <summary>Whether a character is in the Devanagari block, U+0900 to U+097F.</summary>
    internal static bool IsDevanagari(char c) => c is >= 'ऀ' and <= 'ॿ';

    /// <summary>Whether a character is a zero width joiner or non-joiner, which shaping reads inside a Devanagari word.</summary>
    internal static bool IsJoiner(char c) => c is '‌' or '‍';

    /// <summary>
    /// Whether a character attaches to the letter before it: a combining mark (a vowel sign, a virama, an anusvara) or a joiner. A line break
    /// must not fall before one, because a mark on its own is drawn on a dotted circle.
    /// </summary>
    private static bool IsAttachment(char c) =>
        IsJoiner(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;

    /// <summary>
    /// Breaks text into lines no wider than the given width, at spaces. A single word wider than a line is broken between its characters, so
    /// nothing is lost and nothing runs past the edge.
    /// </summary>
    /// <param name="text">Printable text (see <see cref="CanShow"/>).</param>
    /// <param name="font">The face the text is set in.</param>
    /// <param name="size">The font size in points.</param>
    /// <param name="maxWidth">The widest a line may be, in points; must be positive.</param>
    /// <returns>The lines in order; empty when the text has no words.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxWidth"/> is not positive.</exception>
    public static IReadOnlyList<string> Wrap(string text, PdfFont font, double size, double maxWidth)
    {
        if (maxWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxWidth), "A line needs a positive width.");

        var lines = new List<string>();
        var line = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Width(word, font, size) <= maxWidth)
            {
                var joined = line.Length == 0 ? word : line + " " + word;
                if (line.Length == 0 || Width(joined, font, size) <= maxWidth)
                {
                    line = joined;
                    continue;
                }

                lines.Add(line);
                line = word;
                continue;
            }

            // A word too wide for any line starts on a line of its own and is cut into full lines; only its last piece stays open, so the
            // next word can follow it on the same line.
            if (line.Length > 0)
                lines.Add(line);

            var pieces = BreakWord(word, font, size, maxWidth);
            lines.AddRange(pieces.Take(pieces.Count - 1));
            line = pieces[^1];
        }

        if (line.Length > 0)
            lines.Add(line);

        return lines;
    }

    /// <summary>
    /// Shortens a line to fit the width, marking the cut with three dots. A line that already fits is returned unchanged.
    /// </summary>
    /// <param name="text">Printable text (see <see cref="CanShow"/>).</param>
    /// <param name="font">The face the text is set in.</param>
    /// <param name="size">The font size in points.</param>
    /// <param name="maxWidth">The widest the result may be, in points; must be positive.</param>
    /// <returns>The text unchanged when it already fits; otherwise <see cref="Ellipsize"/>'s result.</returns>
    public static string Truncate(string text, PdfFont font, double size, double maxWidth) =>
        Width(text, font, size) <= maxWidth ? text : Ellipsize(text, font, size, maxWidth);

    /// <summary>
    /// Cuts a line so that it ends in three dots and fits the width, whether or not the line was too wide to begin with. Used where text
    /// continues past what is drawn, so the reader can see that it does.
    /// </summary>
    /// <param name="text">Printable text (see <see cref="CanShow"/>).</param>
    /// <param name="font">The face the text is set in.</param>
    /// <param name="size">The font size in points.</param>
    /// <param name="maxWidth">The widest the result may be, in points; must be positive.</param>
    /// <returns>The longest prefix of the text that fits with three dots after it, then the dots.</returns>
    public static string Ellipsize(string text, PdfFont font, double size, double maxWidth)
    {
        var kept = text;
        while (kept.Length > 0 && Width(kept.TrimEnd() + "...", font, size) > maxWidth)
            kept = kept[..^1];

        return kept.TrimEnd() + "...";
    }

    /// <summary>
    /// Cuts a word that is wider than a line into pieces that each fit, so a long unbroken string (a URL, say) wraps instead of overflowing.
    /// A piece is cut only between clusters (see <see cref="Clusters"/>), so a Devanagari vowel sign never starts a line.
    /// </summary>
    /// <returns>The pieces in order; never empty for a non-empty word.</returns>
    private static List<string> BreakWord(string word, PdfFont font, double size, double maxWidth)
    {
        var pieces = new List<string>();
        var piece = new StringBuilder();
        foreach (var cluster in Clusters(word))
        {
            if (piece.Length > 0 && Width(piece + cluster, font, size) > maxWidth)
            {
                pieces.Add(piece.ToString());
                piece.Clear();
            }

            piece.Append(cluster);
        }

        if (piece.Length > 0)
            pieces.Add(piece.ToString());

        return pieces;
    }

    /// <summary>
    /// Splits a word into clusters: each letter with the marks and joiners that attach to it. A Latin word splits into its letters, and a
    /// Devanagari word into its aksharas, so a line break never falls inside one.
    /// </summary>
    private static IEnumerable<string> Clusters(string word)
    {
        var start = 0;
        for (var i = 1; i <= word.Length; i++)
        {
            if (i == word.Length || !IsAttachment(word[i]))
            {
                yield return word[start..i];
                start = i;
            }
        }
    }
}

/// <summary>A run of text that is set in one face: Devanagari, shaped with the embedded font, or Latin text in a Helvetica face.</summary>
/// <param name="Text">The characters of the run.</param>
/// <param name="Devanagari">Whether the run is Devanagari (true) or Latin (false).</param>
internal readonly record struct TextSegment(string Text, bool Devanagari);
