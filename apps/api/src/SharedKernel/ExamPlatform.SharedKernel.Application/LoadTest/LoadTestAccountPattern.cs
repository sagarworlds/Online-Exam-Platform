namespace ExamPlatform.SharedKernel.Application.LoadTest;

/// <summary>
/// The addresses of the load test's candidate pool: a pattern with <c>{n}</c> that is replaced by 1, 2, 3, and so on, as in the
/// load scripts' <c>LOAD_TEST_EMAIL_PATTERN</c>. Both commands that build the pool read it through this one class, so the accounts
/// one creates are exactly the accounts the other enrols.
/// </summary>
/// <remarks>
/// The pattern has to end in <c>.invalid</c>, a top-level domain reserved so that it can never exist (RFC 2606). Mail to such an
/// address cannot be delivered and nobody can own it, so a pool account is never a real person and nothing a test sends reaches
/// anyone. This is also what keeps the commands harmless if they are ever run against the wrong environment.
/// </remarks>
public sealed class LoadTestAccountPattern
{
    /// <summary>The placeholder in the pattern that the account number replaces.</summary>
    public const string NumberPlaceholder = "{n}";

    /// <summary>The top-level domain every pool address must end in.</summary>
    public const string RequiredSuffix = ".invalid";

    /// <summary>The most accounts one pool may have, a little above the 2x cohort of 20,000 at the load test's defaults.</summary>
    public const int MaxCount = 50_000;

    private readonly string _pattern;

    private LoadTestAccountPattern(string pattern, int count)
    {
        _pattern = pattern;
        Count = count;
    }

    /// <summary>How many accounts the pool has, numbered from 1.</summary>
    public int Count { get; }

    /// <summary>Checks a pattern and a size.</summary>
    /// <param name="pattern">The pattern, such as <c>candidate-{n}@load.invalid</c>.</param>
    /// <param name="count">How many accounts, from 1 to <see cref="MaxCount"/>.</param>
    /// <param name="problem">Why the pattern or size is refused, when this returns false.</param>
    /// <param name="result">The checked pattern, when this returns true.</param>
    /// <returns>True when the pattern and size can be used.</returns>
    public static bool TryCreate(string? pattern, int count, out string problem, out LoadTestAccountPattern result)
    {
        result = null!;
        pattern = pattern?.Trim();

        if (string.IsNullOrEmpty(pattern))
        {
            problem = $"The e-mail pattern is missing. Give one such as candidate-{NumberPlaceholder}@load{RequiredSuffix}.";
            return false;
        }

        if (CountOccurrences(pattern, NumberPlaceholder) != 1)
        {
            problem = $"The e-mail pattern must contain {NumberPlaceholder} exactly once, where the account number goes.";
            return false;
        }

        if (!pattern.EndsWith(RequiredSuffix, StringComparison.OrdinalIgnoreCase))
        {
            problem = $"The e-mail pattern must end in {RequiredSuffix}, a domain that cannot exist, so a pool account can never be a real person.";
            return false;
        }

        if (count is < 1 or > MaxCount)
        {
            problem = $"The count must be from 1 to {MaxCount}.";
            return false;
        }

        problem = string.Empty;
        result = new LoadTestAccountPattern(pattern, count);
        return true;
    }

    /// <summary>The address of account number <paramref name="n"/>.</summary>
    /// <param name="n">The account number, from 1 to <see cref="Count"/>.</param>
    public string AddressOf(int n) => _pattern.Replace(NumberPlaceholder, n.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>Every address of the pool, in account order.</summary>
    public IEnumerable<string> Addresses() => Enumerable.Range(1, Count).Select(AddressOf);

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
