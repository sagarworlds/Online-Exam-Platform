using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.Invite.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>What one pass of the notification run did.</summary>
/// <param name="MailAvailable">Whether there was a mail server to send to; when not, nothing was looked at.</param>
/// <param name="RemindersSent">Reminders that an exam is about to start.</param>
/// <param name="ResultNoticesSent">Notices that a result can be seen.</param>
/// <param name="RevisionNoticesSent">Notices that a score changed.</param>
/// <param name="NotSent">Messages the mail server did not take; each is tried again later, a few times.</param>
/// <param name="Errors">Things that went wrong looking up what to send (an exam or a roster that could not be read), skipped for this pass.</param>
public sealed record NotificationRunSummary(bool MailAvailable, int RemindersSent, int ResultNoticesSent, int RevisionNoticesSent, int NotSent, int Errors)
{
    /// <summary>The summary of a pass that found no mail server and so did nothing.</summary>
    public static NotificationRunSummary NoMail { get; } = new(false, 0, 0, 0, 0, 0);
}

/// <summary>
/// The platform's scheduled e-mails (FR-39): a reminder to each enrolled candidate when an exam is within a day and again within the
/// hour, a notice when a candidate's result can be seen, and a notice when their score is revised after they saw it. Invitations and
/// attempt requests are sent where they happen and are not part of this.
/// <para>
/// One pass looks at what is due <em>now</em> and sends it; it is meant to be run every few minutes, by a timer in the host and by an
/// outside scheduler that also wakes a host that sleeps. Running it again, or twice at once, sends nothing twice: each message is
/// recorded (<see cref="NotificationDelivery"/>) as it goes. Nothing is sent about the past without limit either: a result or revision
/// more than a day old is left alone, so switching this on does not mail every candidate their old results.
/// </para>
/// </summary>
public sealed class NotificationRun(
    IExamCatalog catalog,
    IExamRoster roster,
    INotificationQueries queries,
    INotificationDeliveryRepository deliveries,
    IExamNotificationMailer mailer,
    IExamRuntimeUnitOfWork unitOfWork)
{
    /// <summary>How far ahead of an exam's start the first reminder goes.</summary>
    public static readonly TimeSpan FirstReminderLead = TimeSpan.FromHours(24);

    /// <summary>How far ahead of an exam's start the second reminder goes.</summary>
    public static readonly TimeSpan SecondReminderLead = TimeSpan.FromHours(1);

    /// <summary>
    /// How long after something happened it is still announced. A message later than this is noise, and the limit is what keeps a first
    /// run from mailing candidates results released long ago.
    /// </summary>
    public static readonly TimeSpan AnnounceWithin = TimeSpan.FromHours(24);

    /// <summary>
    /// How far back the run looks for submitted attempts whose result may have come out since. An exam can release results days after
    /// it ends; one that releases later than this after a submission is not announced.
    /// </summary>
    public static readonly TimeSpan SubmissionLookback = TimeSpan.FromDays(7);

    private readonly Dictionary<Guid, ExamSnapshot?> _exams = [];
    private readonly Dictionary<Guid, IReadOnlyDictionary<Guid, string>> _addresses = [];
    private int _reminders;
    private int _results;
    private int _revisions;
    private int _notSent;
    private int _errors;

    /// <summary>Sends what is due at <paramref name="nowUtc"/>.</summary>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="onError">Told of anything that could not be looked up, so the caller can log it; the pass goes on without it.</param>
    public async Task<NotificationRunSummary> RunAsync(DateTime nowUtc, CancellationToken cancellationToken, Action<Exception>? onError = null)
    {
        // What one pass learned (the exams, the rosters, the counts) is that pass's alone.
        _exams.Clear();
        _addresses.Clear();
        _reminders = _results = _revisions = _notSent = _errors = 0;

        if (!mailer.IsAvailable)
            return NotificationRunSummary.NoMail;

        // Each part is on its own, so one that cannot be read does not hold up the others.
        await GuardedAsync(() => RemindAsync(nowUtc, cancellationToken), onError);
        await GuardedAsync(() => AnnounceResultsAsync(nowUtc, cancellationToken), onError);
        await GuardedAsync(() => AnnounceRevisionsAsync(nowUtc, cancellationToken), onError);

        return new NotificationRunSummary(true, _reminders, _results, _revisions, _notSent, _errors);
    }

    private async Task GuardedAsync(Func<Task> part, Action<Exception>? onError)
    {
        try
        {
            await part();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _errors++;
            onError?.Invoke(exception);
        }
    }

    // ---- reminders -------------------------------------------------------------------------------------

    private async Task RemindAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        foreach (var exam in await catalog.FindPublishedStartingBetweenAsync(nowUtc, nowUtc + FirstReminderLead, cancellationToken))
        {
            // Inside the last hour only the closer reminder is of any use, even to a candidate who was never sent the first.
            var withinAnHour = exam.StartUtc - nowUtc <= SecondReminderLead;
            var kind = withinAnHour ? NotificationKind.ExamReminderOneHour : NotificationKind.ExamReminder24Hours;
            var known = await DeliveriesAsync(kind, [exam.Id], cancellationToken);

            foreach (var (candidateId, address) in await AddressesAsync(exam.Id, cancellationToken))
            {
                await DeliverAsync(
                    known, kind, exam.Id, candidateId, nowUtc,
                    () => mailer.SendReminderAsync(new ExamReminderEmail(address, exam.Name, exam.StartUtc, withinAnHour), cancellationToken),
                    () => _reminders++,
                    cancellationToken);
            }
        }
    }

    // ---- results ---------------------------------------------------------------------------------------

    private async Task AnnounceResultsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var rows = await queries.ListAwaitingResultNoticeAsync(nowUtc - SubmissionLookback, cancellationToken);
        if (rows.Count == 0)
            return;

        var known = await DeliveriesAsync(NotificationKind.ResultReleased, rows.Select(r => r.AttemptId).ToList(), cancellationToken);
        foreach (var row in rows)
        {
            var exam = await ExamAsync(row.ExamId, cancellationToken);
            if (exam is null || ResultRelease.ReleasedAtUtc(exam, row.SubmittedAtUtc) is not { } releasedAt)
                continue;

            // Not out yet, or out so long ago that saying so now would only confuse.
            if (releasedAt > nowUtc || nowUtc - releasedAt > AnnounceWithin)
                continue;

            if (!(await AddressesAsync(row.ExamId, cancellationToken)).TryGetValue(row.CandidateId, out var address))
                continue;

            await DeliverAsync(
                known, NotificationKind.ResultReleased, row.AttemptId, row.CandidateId, nowUtc,
                () => mailer.SendResultReleasedAsync(new ResultReleasedEmail(address, exam.Name, row.Number), cancellationToken),
                () => _results++,
                cancellationToken);
        }
    }

    // ---- revised scores --------------------------------------------------------------------------------

    private async Task AnnounceRevisionsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var rows = await queries.ListAwaitingRevisionNoticeAsync(nowUtc - AnnounceWithin, cancellationToken);
        if (rows.Count == 0)
            return;

        var known = await DeliveriesAsync(NotificationKind.ScoreRevised, rows.Select(r => r.RevisionId).ToList(), cancellationToken);
        foreach (var row in rows)
        {
            var exam = await ExamAsync(row.ExamId, cancellationToken);
            if (exam is null)
                continue;

            // A score changed before the candidate could see any result is not news: they are told the result, as it then is, when it is out.
            if (ResultRelease.ReleasedAtUtc(exam, row.SubmittedAtUtc) is not { } releasedAt || releasedAt > row.RevisedAtUtc)
                continue;

            if (!(await AddressesAsync(row.ExamId, cancellationToken)).TryGetValue(row.CandidateId, out var address))
                continue;

            await DeliverAsync(
                known, NotificationKind.ScoreRevised, row.RevisionId, row.CandidateId, nowUtc,
                () => mailer.SendScoreRevisedAsync(
                    new ScoreRevisedEmail(address, exam.Name, row.PreviousScore, row.PreviousMaxScore, row.NewScore, row.NewMaxScore, row.Reason),
                    cancellationToken),
                () => _revisions++,
                cancellationToken);
        }
    }

    // ---- shared ----------------------------------------------------------------------------------------

    /// <summary>
    /// Sends one message unless it was already sent, was given up on, or was tried too recently, and records the try before moving on, so a
    /// crash right after sending costs at most the record of that one message.
    /// </summary>
    private async Task DeliverAsync(
        Dictionary<(Guid Subject, Guid Recipient), NotificationDelivery> known,
        NotificationKind kind,
        Guid subjectId,
        Guid recipientId,
        DateTime nowUtc,
        Func<Task<bool>> send,
        Action counted,
        CancellationToken cancellationToken)
    {
        var key = (subjectId, recipientId);
        if (!known.TryGetValue(key, out var delivery))
        {
            delivery = NotificationDelivery.Start(kind, subjectId, recipientId);
            deliveries.Add(delivery);
            known[key] = delivery;
        }

        if (!delivery.IsDueAt(nowUtc))
            return;

        var sent = await send();
        delivery.RecordAttempt(sent, nowUtc);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (sent)
            counted();
        else
            _notSent++;
    }

    private async Task<Dictionary<(Guid Subject, Guid Recipient), NotificationDelivery>> DeliveriesAsync(
        NotificationKind kind, IReadOnlyCollection<Guid> subjectIds, CancellationToken cancellationToken) =>
        (await deliveries.ListAsync(kind, subjectIds, cancellationToken)).ToDictionary(d => (d.SubjectId, d.RecipientId));

    private async Task<ExamSnapshot?> ExamAsync(Guid examId, CancellationToken cancellationToken)
    {
        if (!_exams.TryGetValue(examId, out var exam))
            _exams[examId] = exam = await catalog.FindAsync(examId, cancellationToken);

        return exam;
    }

    /// <summary>The address each enrolled candidate was invited at, which the account holds.</summary>
    private async Task<IReadOnlyDictionary<Guid, string>> AddressesAsync(Guid examId, CancellationToken cancellationToken)
    {
        if (!_addresses.TryGetValue(examId, out var addresses))
        {
            addresses = (await roster.GetEnrolledCandidatesAsync(examId, cancellationToken))
                .Where(c => !string.IsNullOrWhiteSpace(c.Email))
                .GroupBy(c => c.UserId)
                .ToDictionary(g => g.Key, g => g.First().Email);
            _addresses[examId] = addresses;
        }

        return addresses;
    }
}
