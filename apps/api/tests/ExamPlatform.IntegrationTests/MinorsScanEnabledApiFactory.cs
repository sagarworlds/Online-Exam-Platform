namespace ExamPlatform.IntegrationTests;

/// <summary>
/// An API in an environment where scoring candidates under 18 has been switched on (<c>Proctoring:MinorsScanEnabled</c>). Used by the one
/// test class that checks that path; every other class runs with the switch off, as production does until counsel's opinion is recorded.
/// </summary>
public sealed class MinorsScanEnabledApiFactory : ApiFactory
{
    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> AdditionalConfiguration =>
        new Dictionary<string, string?>(base.AdditionalConfiguration)
        {
            ["Proctoring:MinorsScanEnabled"] = "true",
        };
}
