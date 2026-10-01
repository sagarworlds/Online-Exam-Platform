namespace ExamPlatform.Modules.Identity.Endpoints.OtpDelivery;

/// <summary>
/// Chooses which <c>IOtpSender</c> adapter delivers one-time codes and password-reset links,
/// bound from <see cref="SectionName"/>. There is deliberately no default: an environment
/// that names no provider fails at startup (see <see cref="OtpDeliveryOptionsValidator"/>)
/// rather than silently falling back to an adapter that logs codes (NFR-6).
/// </summary>
public sealed class OtpDeliveryOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "Identity:OtpDelivery";

    /// <summary>
    /// The provider that writes codes to the application log instead of sending them.
    /// Allowed only in the Development environment.
    /// </summary>
    public const string DevelopmentLog = "DevelopmentLog";

    /// <summary>
    /// The configured provider name, or null when none is set. <see cref="DevelopmentLog"/>
    /// is the only provider until the Notifications module brings real email and SMS
    /// delivery (FR-39).
    /// </summary>
    public string? Provider { get; set; }
}
