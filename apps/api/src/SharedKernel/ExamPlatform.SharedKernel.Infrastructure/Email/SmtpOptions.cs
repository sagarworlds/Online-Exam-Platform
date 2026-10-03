namespace ExamPlatform.SharedKernel.Infrastructure.Email;

/// <summary>
/// The one mail server everything the platform sends goes through (invitations, answers to attempt requests), bound from the
/// <c>Smtp</c> configuration section. With no <see cref="Host"/> set, e-mail is simply not configured and nothing is sent.
/// </summary>
public sealed class SmtpOptions
{
    /// <summary>The configuration section the options are read from.</summary>
    public const string SectionName = "Smtp";

    /// <summary>The mail server's host name; blank means e-mail is not configured.</summary>
    public string? Host { get; set; }

    /// <summary>The mail server's port (587 for STARTTLS submission by default).</summary>
    public int Port { get; set; } = 587;

    /// <summary>Whether to encrypt the connection with TLS.</summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>The account to authenticate as; blank for a server that needs none.</summary>
    public string? User { get; set; }

    /// <summary>The account's password.</summary>
    public string? Password { get; set; }

    /// <summary>The sender address shown on every message.</summary>
    public string From { get; set; } = "no-reply@examplatform.local";

    /// <summary>Whether a mail server is configured.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}
