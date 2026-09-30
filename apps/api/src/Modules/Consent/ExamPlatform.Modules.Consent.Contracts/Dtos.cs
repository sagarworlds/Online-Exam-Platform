namespace ExamPlatform.Modules.Consent.Contracts;

/// <summary>Whether a subject currently has active consent for a purpose.</summary>
/// <param name="SubjectId">Whose consent this describes.</param>
/// <param name="Purpose">What the consent covers.</param>
/// <param name="IsActive">Whether an active (not withdrawn) consent record exists.</param>
/// <param name="ConsentRecordId">The relevant consent record's id, if one exists.</param>
/// <param name="CurrentNoticeVersionId">
/// The most recently effective notice version for this purpose, if one has been seeded —
/// what a caller should pass as <see cref="RecordConsentRequest.NoticeVersionId"/> to grant.
/// </param>
public sealed record ConsentStatusDto(
    Guid SubjectId, ConsentPurpose Purpose, bool IsActive, Guid? ConsentRecordId, Guid? CurrentNoticeVersionId);

/// <summary>A single consent ledger entry.</summary>
/// <param name="ConsentRecordId">The record's id.</param>
/// <param name="SubjectId">Whose data the consent covers.</param>
/// <param name="Purpose">What the consent covers.</param>
/// <param name="NoticeVersionId">The specific notice version accepted.</param>
/// <param name="GrantedAtUtc">When consent was granted.</param>
/// <param name="WithdrawnAtUtc">When consent was withdrawn, if it has been.</param>
public sealed record ConsentRecordDto(
    Guid ConsentRecordId,
    Guid SubjectId,
    ConsentPurpose Purpose,
    Guid NoticeVersionId,
    DateTime GrantedAtUtc,
    DateTime? WithdrawnAtUtc);

/// <summary>Request to record a new consent grant.</summary>
/// <param name="SubjectId">Whose data the consent covers.</param>
/// <param name="Purpose">What the consent covers.</param>
/// <param name="NoticeVersionId">The specific notice version being accepted.</param>
/// <param name="GivenById">Who is giving this consent (the subject or their guardian).</param>
public sealed record RecordConsentRequest(Guid SubjectId, ConsentPurpose Purpose, Guid NoticeVersionId, Guid GivenById);
