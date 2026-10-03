namespace ExamPlatform.Modules.ExamRuntime.Application.Ports;

/// <summary>The e-mail that tells a candidate how their request for another attempt was answered.</summary>
/// <param name="To">The candidate's address.</param>
/// <param name="ExamName">The exam's name, for the subject and body.</param>
/// <param name="Approved">Whether they were given the attempt (otherwise the request was declined).</param>
/// <param name="Note">What the administrator said when declining, if anything.</param>
public sealed record AttemptRequestDecisionEmail(string To, string ExamName, bool Approved, string? Note);

/// <summary>The e-mail that tells a staff member a candidate has asked for another attempt.</summary>
/// <param name="To">The staff member's address.</param>
/// <param name="ExamName">The exam's name.</param>
/// <param name="CandidateEmail">The address the candidate was invited at, or null when it cannot be found.</param>
/// <param name="Message">Why the candidate asked, in their words, if they said.</param>
public sealed record NewAttemptRequestEmail(string To, string ExamName, string? CandidateEmail, string? Message);

/// <summary>Delivers e-mail about attempt requests. Real delivery is optional: with no SMTP configured nothing is sent.</summary>
public interface IAttemptRequestNotifier
{
    /// <summary>Tells the candidate their request was approved or declined.</summary>
    /// <param name="email">What to send and where.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the message was handed to a mail server; <see langword="false"/> when nothing was sent, either because
    /// e-mail is not configured or because the mail server refused it. The decision is already saved either way, so the administrator
    /// is told and can let the candidate know by other means.
    /// </returns>
    Task<bool> SendDecisionAsync(AttemptRequestDecisionEmail email, CancellationToken cancellationToken);

    /// <summary>Tells a staff member a candidate has asked for another attempt, so they can answer it in the queue.</summary>
    /// <param name="email">What to send and where.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the message was handed to a mail server; false means nothing was sent for that recipient.</returns>
    Task<bool> SendNewRequestAsync(NewAttemptRequestEmail email, CancellationToken cancellationToken);
}
