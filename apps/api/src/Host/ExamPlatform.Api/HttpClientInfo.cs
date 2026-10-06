using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Api;

/// <summary>Reads the caller's address and device signature from the current HTTP request (FR-26).</summary>
public sealed class HttpClientInfo(IHttpContextAccessor accessor) : IClientInfo
{
    /// <inheritdoc />
    public string? IpAddress => ClientInfo.CleanIp(accessor.HttpContext?.Connection.RemoteIpAddress?.ToString());

    /// <inheritdoc />
    public string? DeviceFingerprint =>
        accessor.HttpContext?.Request.Headers.TryGetValue(ClientInfo.FingerprintHeader, out var value) == true
            ? ClientInfo.CleanFingerprint(value.ToString())
            : null;
}
