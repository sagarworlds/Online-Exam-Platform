namespace ExamPlatform.Modules.Identity.Domain.DataRequests;

/// <summary>Where a data request stands. A request is open only while it is <see cref="Received"/>.</summary>
public enum DataRequestStatus
{
    /// <summary>Received and waiting for staff to answer it.</summary>
    Received,

    /// <summary>Answered and carried out.</summary>
    Completed,

    /// <summary>Answered with a refusal; the note says why.</summary>
    Rejected,
}
