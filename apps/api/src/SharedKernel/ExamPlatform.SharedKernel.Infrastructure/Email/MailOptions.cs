namespace ExamPlatform.SharedKernel.Infrastructure.Email;

/// <summary>Chooses which <see cref="IMailSender"/> adapter the platform sends mail through, bound from <c>Mail:Provider</c>.</summary>
public sealed class MailOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "Mail";

    /// <summary>Delivers over SMTP (<see cref="SmtpOptions"/>). The default, so a host that already worked keeps working.</summary>
    public const string Smtp = "Smtp";

    /// <summary>Delivers through Brevo's HTTPS transactional API (<see cref="BrevoOptions"/>), for a host that blocks outbound SMTP.</summary>
    public const string BrevoApi = "BrevoApi";

    /// <summary>The configured provider; defaults to <see cref="Smtp"/>.</summary>
    public string Provider { get; set; } = Smtp;
}
