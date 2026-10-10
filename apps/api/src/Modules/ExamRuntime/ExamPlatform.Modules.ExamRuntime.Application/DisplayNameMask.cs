namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// Shortens a person's name for a leaderboard that other candidates can read (FR-35). Personal data is limited under DPDP, so a board shows the
/// first name and the initial of the last name, never the whole name. Pure: the rule is tested on its own.
/// </summary>
/// <remarks>
/// The last word of the name stands in for the surname. That is right for most names but not all, and it is the decision flagged for counsel
/// (issue #11): a name is shortened, not anonymised, so a small batch can still identify someone.
/// </remarks>
public static class DisplayNameMask
{
    /// <summary>The name as a board shows it: "Asha K." for "Asha Kumar", "Asha" for "Asha", and null when there is no name.</summary>
    /// <param name="displayName">The name the person registered with, or null.</param>
    /// <returns>The shortened name, or null when there is no name to show.</returns>
    public static string? Mask(string? displayName)
    {
        var words = (displayName ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length switch
        {
            0 => null,
            1 => words[0],
            _ => $"{words[0]} {char.ToUpperInvariant(words[^1][0])}.",
        };
    }
}
