namespace ExamPlatform.SharedKernel.Infrastructure.Sms;

/// <summary>
/// Settings for SMS, bound from the <c>Sms</c> configuration section: the master switch, which stops every SMS the platform would send,
/// and the registrations an SMS needs once the switch is on (FR-39). No provider is built in yet (see <see cref="UnconfiguredSmsProvider"/>),
/// so the provider, sender ID and templates are checked at startup by <see cref="SmsOptionsValidator"/> and not used to send anything.
/// </summary>
public sealed class SmsOptions
{
    /// <summary>The configuration section the options are read from.</summary>
    public const string SectionName = "Sms";

    /// <summary>
    /// The master switch for every SMS. Off unless this is true, whatever else is configured, and setting it back to false stops every
    /// SMS. Blank counts as off. Off is the safe default: an SMS costs money and reaches a phone the person may not expect to hear from
    /// the platform.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>Whether the master switch is on.</summary>
    public bool IsEnabled => Enabled == true;

    /// <summary>
    /// The SMS provider to send through, by the name the platform has an adapter for. Blank means none. Which provider to use is an owner
    /// decision (issue #234), so this is set once one is chosen and built.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// The sender ID, the header every SMS is sent under. It is registered with the DLT platform before the first send, as Indian rules
    /// require. Blank means it is not registered yet.
    /// </summary>
    public string? SenderId { get; set; }

    /// <summary>
    /// The DLT template ID of each message template, keyed by the template's name. Each template is registered with DLT before its first
    /// send, and its message goes out under that ID. Empty means none is registered yet.
    /// </summary>
    public Dictionary<string, string> Templates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
