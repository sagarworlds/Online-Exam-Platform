using ExamPlatform.Modules.Consent.Domain.Events;
using ExamPlatform.Modules.Consent.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Consent.Domain;

/// <summary>
/// One entry in the consent ledger (FR-44): a record that a specific notice
/// version was accepted, by whom, and when — plus, if applicable, when it was
/// withdrawn. Immutable history: withdrawing never deletes the grant, it only
/// marks it withdrawn, so the ledger always shows the full timeline.
/// </summary>
public sealed class ConsentRecord : AggregateRoot
{
    /// <summary>Whose data this consent covers (a candidate, or a minor candidate represented by a guardian).</summary>
    public Guid SubjectId { get; private set; }

    /// <summary>What this consent covers.</summary>
    public ConsentPurpose Purpose { get; private set; }

    /// <summary>The exact notice version that was presented and accepted.</summary>
    public Guid NoticeVersionId { get; private set; }

    /// <summary>When consent was granted.</summary>
    public DateTime GrantedAtUtc { get; private set; }

    /// <summary>When consent was withdrawn, if it has been.</summary>
    public DateTime? WithdrawnAtUtc { get; private set; }

    /// <summary>Who gave this consent — the subject themselves, or a guardian acting for a minor.</summary>
    public Guid GivenById { get; private set; }

    /// <summary>Who withdrew this consent, if it has been withdrawn.</summary>
    public Guid? WithdrawnById { get; private set; }

    /// <summary>Whether this consent is currently in effect.</summary>
    public bool IsActive => WithdrawnAtUtc is null;

    private ConsentRecord(
        Guid id, Guid subjectId, ConsentPurpose purpose, Guid noticeVersionId, DateTime grantedAtUtc, Guid givenById)
        : base(id)
    {
        SubjectId = subjectId;
        Purpose = purpose;
        NoticeVersionId = noticeVersionId;
        GrantedAtUtc = grantedAtUtc;
        GivenById = givenById;
    }

    /// <summary>Grants consent, recording it in the ledger.</summary>
    /// <param name="subjectId">Whose data the consent covers.</param>
    /// <param name="purpose">What the consent covers.</param>
    /// <param name="noticeVersionId">The specific notice version being accepted.</param>
    /// <param name="givenById">Who is giving this consent (the subject or their guardian).</param>
    /// <param name="nowUtc">The current instant.</param>
    public static ConsentRecord Grant(
        Guid subjectId, ConsentPurpose purpose, Guid noticeVersionId, Guid givenById, DateTime nowUtc)
    {
        var record = new ConsentRecord(Guid.NewGuid(), subjectId, purpose, noticeVersionId, nowUtc, givenById);
        record.AddDomainEvent(new ConsentGrantedEvent(record.Id, subjectId, purpose, nowUtc));
        return record;
    }

    /// <summary>Withdraws this consent.</summary>
    /// <param name="withdrawnById">Who is withdrawing the consent (the subject or their guardian).</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="ConsentAlreadyWithdrawnError">This consent was already withdrawn.</exception>
    public void Withdraw(Guid withdrawnById, DateTime nowUtc)
    {
        if (WithdrawnAtUtc is not null)
        {
            throw new ConsentAlreadyWithdrawnError();
        }

        WithdrawnAtUtc = nowUtc;
        WithdrawnById = withdrawnById;
        AddDomainEvent(new ConsentWithdrawnEvent(Id, SubjectId, Purpose, withdrawnById, nowUtc));
    }
}
