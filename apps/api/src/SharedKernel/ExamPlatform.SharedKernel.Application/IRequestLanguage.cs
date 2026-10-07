namespace ExamPlatform.SharedKernel.Application;

/// <summary>
/// The languages the caller of the current request asked for, most wanted first (FR-51): what the web app sends in
/// <c>Accept-Language</c> from the language its user chose, or what the browser sends when they have not chosen. Lets code that shows
/// content to a candidate pick a translation without every query carrying the language through. Outside a request there is no
/// preference.
/// </summary>
public interface IRequestLanguage
{
    /// <summary>The wanted languages as lower-case primary subtags such as "hi", best first; empty when none was asked for.</summary>
    IReadOnlyList<string> Preferred { get; }
}

/// <summary>Reads an <c>Accept-Language</c> header, shared by everything that needs one.</summary>
public static class RequestLanguage
{
    /// <summary>The longest header read; a real one is a few dozen characters, so this only stops a client making the server parse a novel.</summary>
    public const int MaxHeaderLength = 256;

    /// <summary>The most languages kept from one header.</summary>
    public const int MaxLanguages = 8;

    /// <summary>
    /// Puts the header's languages in order of preference. Only the primary subtag is kept ("hi-IN" is "hi"), because content is
    /// translated per language, not per region; a language the client ranked at zero is one it does not want, and "*" says nothing useful.
    /// </summary>
    /// <param name="header">The raw header value, such as <c>hi-IN,hi;q=0.9,en;q=0.5</c>; null or blank means no preference.</param>
    /// <returns>The distinct language codes, best first, at most <see cref="MaxLanguages"/>.</returns>
    public static IReadOnlyList<string> Parse(string? header)
    {
        if (string.IsNullOrWhiteSpace(header) || header.Length > MaxHeaderLength)
            return [];

        var ranked = new List<(string Code, double Quality)>();
        foreach (var part in header.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split(';', StringSplitOptions.TrimEntries);
            var code = pieces[0].Split('-')[0].ToLowerInvariant();
            if (code.Length is < 2 or > 3 || !code.All(char.IsAsciiLetter))
                continue;

            var quality = 1.0;
            var q = pieces.Skip(1).FirstOrDefault(p => p.StartsWith("q=", StringComparison.OrdinalIgnoreCase));
            if (q is not null && !double.TryParse(q[2..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out quality))
                continue;
            if (quality > 0)
                ranked.Add((code, quality));
        }

        // OrderBy is stable, so languages the client ranked equally keep the order it wrote them in.
        return ranked.OrderByDescending(r => r.Quality).Select(r => r.Code).Distinct().Take(MaxLanguages).ToList();
    }
}
