using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>Stands in for the caller's address and device signature (FR-26); a test sets what the "request" came from.</summary>
internal sealed class FakeClientInfo : IClientInfo
{
    public string? IpAddress { get; set; } = "203.0.113.10";

    public string? DeviceFingerprint { get; set; } = "device0000000001";
}
