namespace ExamPlatform.SharedKernel.Infrastructure.Email;

/// <summary>
/// Settings for sending mail through Brevo's HTTPS transactional API, bound from the <c>Brevo</c> configuration section.
/// This exists alongside <see cref="SmtpOptions"/> because some hosts (e.g. Render's free plan) block outbound SMTP
/// ports, while the same mail can be submitted over HTTPS instead; <see cref="MailOptions.Provider"/> picks between them.
/// </summary>
public sealed class BrevoOptions
{
    /// <summary>The configuration section the options are read from.</summary>
    public const string SectionName = "Brevo";

    /// <summary>Brevo's transactional-email API key (not an SMTP key). Blank means Brevo is not configured.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The sender address shown on every message; must be a verified sender in the Brevo account.</summary>
    public string? SenderEmail { get; set; }

    /// <summary>Whether enough settings are present to call the API.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(SenderEmail);
}
