namespace ExamPlatform.SharedKernel.Application;

/// <summary>
/// Where the current request came from: the caller's network address and a signature of their device and browser (FR-26). Lets code
/// that records a sign-in or a sitting name them without every command carrying the caller through. Outside a request there is
/// nothing to know.
/// </summary>
public interface IClientInfo
{
    /// <summary>The caller's IP address as the server sees it (behind the platform's trusted proxy), or null outside a request.</summary>
    string? IpAddress { get; }

    /// <summary>
    /// The device signature the web app sent (<see cref="ClientInfo.FingerprintHeader"/>), cleaned, or null when none was sent or it was
    /// not in the expected form.
    /// </summary>
    string? DeviceFingerprint { get; }
}

/// <summary>The rules for a device signature that arrives from a client, shared by every place that reads one.</summary>
public static class ClientInfo
{
    /// <summary>The request header the web app sends its device signature in.</summary>
    public const string FingerprintHeader = "X-Device-Fingerprint";

    /// <summary>The longest signature kept; the web app sends 32 hex characters, so this only stops a client filling the store.</summary>
    public const int MaxFingerprintLength = 64;

    /// <summary>The longest IP address text kept: an IPv6 address with a zone is 45 characters.</summary>
    public const int MaxIpLength = 45;

    /// <summary>
    /// Cleans a signature from a header. A signature is an opaque token of letters and digits; anything else (spaces, markup, a long
    /// string) is not one a client of ours would send, so it is dropped rather than stored.
    /// </summary>
    /// <param name="value">The raw header value.</param>
    /// <returns>The signature, or null if there is none or it is not in the expected form.</returns>
    public static string? CleanFingerprint(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxFingerprintLength)
            return null;

        return text.All(char.IsAsciiLetterOrDigit) ? text : null;
    }

    /// <summary>Keeps an address within the length the store holds.</summary>
    /// <param name="value">The address text.</param>
    public static string? CleanIp(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim() is { Length: <= MaxIpLength } text ? text : null;
}
