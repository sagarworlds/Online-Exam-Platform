using System.Text;
using System.Text.RegularExpressions;
using ExamPlatform.SharedKernel.Infrastructure.Observability;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>
/// The redaction rules are source-generated, and a rule is skipped when the text cannot hold its match. Both are there only to make every
/// log line cheaper, so neither may change a single character of what is written (NFR-6). This holds the current implementation against the
/// seven interpreted rules it replaced, kept here word for word, over a large seeded set of lines built from the shapes the rules look for
/// and the near-misses that sit beside them.
/// </summary>
public class LogRedactionEquivalenceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private static readonly (Regex Pattern, string Replacement)[] ReferenceRules =
    [
        (Rule(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)+"), "[redacted-email]"),
        (Rule(@"\beyJ[A-Za-z0-9_\-]{4,}\.[A-Za-z0-9_\-]{4,}\.[A-Za-z0-9_\-]{4,}"), "[redacted-token]"),
        (Rule(@"\bBearer\s+[A-Za-z0-9._~+/=\-]{8,}", RegexOptions.IgnoreCase), "Bearer [redacted-token]"),
        (Rule(
                @"(?<![A-Za-z0-9])(password|passwd|pwd|secret|token|access[_-]?token|refresh[_-]?token|api[_-]?key|authorization|otp|answer|cookie)(\s*[:=]\s*)(""[^""]*""|'[^']*'|[^\s,;&}\]]+)",
                RegexOptions.IgnoreCase),
            "$1$2" + LogRedaction.Redacted),
        (Rule(@"\+\d[\d\s\-]{7,}\d"), "[redacted-phone]"),
        (Rule(@"(?<!\d)\d{5}[\s\-]\d{5}(?!\d)"), "[redacted-phone]"),
        (Rule(@"(?<![\w.\-])\d{10,15}(?![\w.\-])"), "[redacted-number]"),
    ];

    // Whole shapes, parts of them, and lookalikes: a masked address, a digit run one short of a number, a label without its value, a label
    // that merely ends a longer word, digits and letters from other scripts that the patterns treat as their ASCII kin (or do not).
    private static readonly string[] Fragments =
    [
        "student@example.com", "a***@example.com", "first.last+tag@school.edu.in", "@", "x@y", "+", "-", ".", "_", "=", ":", ",", ";", "&", "}", "]",
        "+91 98765 43210", "+919876543210", "+1 2", "98765 43210", "98765-43210", "98765  43210", "9876543210", "987654321", "12345678901234567890",
        "123456789012345", "1234567890123456", "2026-10-10", "1234", "42", "0", "5", "٣٤٥٦٧٨٩٠١٢٣", "٣٤٥٦٧ ٨٩٠١٢",
        "password=hunter2", "Password: correct horse", "passwd", "pwd = 1", "secret", "secretx", "xsecret=1", "token ", "token:", "refresh_token=abc.def",
        "access-token: t", "api-key: example-value", "api_key=\"a b\"", "authorization: 'x y'", "OTP: 123456", "otp", "answer=B", "cookie=a;b",
        "hub.verify_token=s3cr3t", "ſecret=1", "api_Key: v", "ANSWER:Z",
        "Authorization: Bearer abcdefghijklmnop", "bearer ABCDEFGH12", "BEARER abcdefgh", "Bearer short", "Bearer abcdefghijkl", "Bearer",
        "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl", "eyJ", "xeyJabcd.efgh.ijkl", "eyJabcd.efgh", "token eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl",
        " ", "  ", "\n", "\t", " ", "\"quoted value\"", "'single'", "x", "Y", "z9", "hello world", "İ", "ı",
        "SELECT a.\"Id\" FROM \"examRuntime\".\"Attempts\" AS a WHERE a.\"Id\" = @__attemptId_0 LIMIT 1",
        "Executed DbCommand (2ms) [Parameters=[@p0='3f2504e0-4f89-11d3-9a0c-0305e82c3301'], CommandType='Text', CommandTimeout='30']",
        "HTTP PUT /v1/me/attempts/{attemptId}/answers/{questionId} responded 200 in 41 ms",
    ];

    [Fact]
    public void TheCurrentRules_WriteExactlyWhatTheInterpretedRulesWrote_OverALargeSeededSet()
    {
        var random = new Random(20261108);

        for (var i = 0; i < 40_000; i++)
        {
            var line = Build(random);

            var expected = Reference(line);
            var actual = LogRedaction.RedactText(line);

            Assert.True(expected == actual, $"Different output for {Show(line)}: expected {Show(expected)}, got {Show(actual)}.");
        }
    }

    [Fact]
    public void EachFragmentOnItsOwn_WritesWhatTheInterpretedRulesWrote()
    {
        foreach (var fragment in Fragments)
            Assert.True(Reference(fragment) == LogRedaction.RedactText(fragment), $"Different output for {Show(fragment)}.");
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Test data: a seeded generator, so the same lines are tried on every run. It is not a secret or a token.")]
    private static string Build(Random random)
    {
        var line = new StringBuilder();
        var parts = random.Next(1, 11);
        for (var i = 0; i < parts; i++)
            line.Append(Fragments[random.Next(Fragments.Length)]);

        return line.ToString();
    }

    private static string Reference(string text)
    {
        var result = text;
        foreach (var (pattern, replacement) in ReferenceRules)
            result = pattern.Replace(result, replacement);

        return result;
    }

    private static string Show(string text) => "\"" + text.Replace("\n", "\\n", StringComparison.Ordinal) + "\"";

    private static Regex Rule(string pattern, RegexOptions options = RegexOptions.None) =>
        new(pattern, options | RegexOptions.CultureInvariant, Timeout);
}
