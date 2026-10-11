namespace ExamPlatform.Modules.Consent.Domain;

/// <summary>
/// Where an incident stands. Three states, each answering a question staff must be able to answer:
/// <list type="bullet">
/// <item><see cref="Logged"/>: nothing has been reported to an outside authority yet. The escalation timer applies only here.</item>
/// <item><see cref="Reported"/>: staff have notified CERT-In or the Data Protection Board outside the app and recorded it here.</item>
/// <item><see cref="Closed"/>: no further action is needed. Terminal.</item>
/// </list>
/// An "investigating" or "contained" state is deliberately absent. Neither changes a timer or a rule, so the note on a status
/// change carries that detail instead.
/// </summary>
public enum IncidentStatus
{
    /// <summary>Recorded in the log, not yet reported to an outside authority.</summary>
    Logged,

    /// <summary>An outside authority has been notified, and the notice is recorded in the log.</summary>
    Reported,

    /// <summary>Finished. The record is kept and can no longer change.</summary>
    Closed
}
