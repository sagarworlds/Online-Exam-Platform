using System.Globalization;
using System.Text;

namespace ExamPlatform.Modules.Analytics.Application;

/// <summary>Names a downloaded report so it can be saved safely on any operating system.</summary>
public static class ReportFileName
{
    /// <summary>The name of an item analysis file: <c>item-analysis-{exam}-{yyyyMMdd-HHmm}.csv</c>, the time in UTC.</summary>
    /// <remarks>
    /// The exam's name is reduced to lowercase letters and digits joined by hyphens, so a name with slashes, quotes or non-Latin letters cannot
    /// break the header or write outside the download folder. A name with none of those left becomes <c>exam</c>.
    /// </remarks>
    /// <param name="examName">The exam's name.</param>
    /// <param name="nowUtc">When the file is made.</param>
    /// <returns>The file name.</returns>
    public static string ForItemAnalysis(string examName, DateTime nowUtc) =>
        $"item-analysis-{Slug(examName)}-{nowUtc.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.csv";

    private static string Slug(string name)
    {
        var builder = new StringBuilder();
        var pendingHyphen = false;
        foreach (var c in name.ToLowerInvariant())
        {
            // Only ASCII letters and digits are kept: anything else, including accented letters, separates words instead.
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingHyphen && builder.Length > 0)
                    builder.Append('-');
                builder.Append(c);
                pendingHyphen = false;
            }
            else
            {
                pendingHyphen = true;
            }
        }

        return builder.Length == 0 ? "exam" : builder.ToString();
    }
}
