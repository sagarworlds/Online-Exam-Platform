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

    /// <summary>The width of a line of text, in points, when set in the given face and size.</summary>
    /// <param name="text">Printable text (see <see cref="CanShow"/>).</param>
    /// <param name="font">The face the text is set in.</param>
    /// <param name="size">The font size in points.</param>
    /// <returns>The width in points.</returns>
    public static double Width(string text, PdfFont font, double size)
    {
        var units = 0;
        foreach (var c in text)
            units += PdfFontMetrics.WidthOf(c, font);

        return units * size / 1000.0;
    }

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
    /// </summary>
    /// <returns>The pieces in order; never empty for a non-empty word.</returns>
    private static List<string> BreakWord(string word, PdfFont font, double size, double maxWidth)
    {
        var pieces = new List<string>();
        var piece = new StringBuilder();
        foreach (var c in word)
        {
            if (piece.Length > 0 && Width(piece + c.ToString(), font, size) > maxWidth)
            {
                pieces.Add(piece.ToString());
                piece.Clear();
            }

            piece.Append(c);
        }

        if (piece.Length > 0)
            pieces.Add(piece.ToString());

        return pieces;
    }
}
