namespace ExamPlatform.SharedKernel.Application;

/// <summary>
/// Reads the <c>--name value</c> pairs of an operator command's command line (<see cref="IOperatorCommand"/>), so every command parses
/// its options the same way and a value is never mistaken for the next flag.
/// </summary>
public static class OperatorArguments
{
    /// <summary>The value that follows a flag.</summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="flag">The flag to look for, such as <c>--count</c>.</param>
    /// <returns>The next argument, or null when the flag is absent, last, or followed by another flag.</returns>
    public static string? ValueOf(IReadOnlyList<string> args, string flag)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.Ordinal))
            {
                var next = args[i + 1];
                return next.StartsWith("--", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(next) ? null : next;
            }
        }

        return null;
    }
}
