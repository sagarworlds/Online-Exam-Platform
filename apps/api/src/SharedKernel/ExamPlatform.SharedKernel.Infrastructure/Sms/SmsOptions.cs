namespace ExamPlatform.SharedKernel.Infrastructure.Sms;

/// <summary>
/// Settings for SMS, bound from the <c>Sms</c> configuration section. Its one value is the master switch, which stops every SMS the
/// platform would send. Provider settings arrive with a provider, and none is built in yet (see <see cref="UnconfiguredSmsProvider"/>).
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
}
