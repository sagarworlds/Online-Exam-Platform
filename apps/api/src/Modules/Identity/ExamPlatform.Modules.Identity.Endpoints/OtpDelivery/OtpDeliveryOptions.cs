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
    /// The provider that e-mails codes through the platform's SMTP server (the <c>Smtp</c>
    /// section). Allowed in every environment. It cannot send SMS.
    /// </summary>
    public const string Smtp = "Smtp";

    /// <summary>
    /// The configured provider name, or null when none is set. <see cref="Smtp"/> is the only
    /// provider a deployed host can use until the Notifications module brings SMS delivery
    /// (FR-39); <see cref="DevelopmentLog"/> exists for local development.
    /// </summary>
    public string? Provider { get; set; }
}
