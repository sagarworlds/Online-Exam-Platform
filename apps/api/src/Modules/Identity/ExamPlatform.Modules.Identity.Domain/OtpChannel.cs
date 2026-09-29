namespace ExamPlatform.Modules.Identity.Domain;

/// <summary>Delivery channel for a one-time-password code.</summary>
public enum OtpChannel
{
    /// <summary>Sent to an email address.</summary>
    Email,

    /// <summary>Sent to a phone number via SMS.</summary>
    Sms
}
