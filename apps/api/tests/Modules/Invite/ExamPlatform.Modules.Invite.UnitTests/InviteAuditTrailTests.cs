using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Invite.Application;
using ExamPlatform.Modules.Invite.Domain.Events;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.Invite.UnitTests;

/// <summary>What the audit trail keeps of a change to an invitation: who, which invitation and which exam, and never an address (NFR-6).</summary>
public class InviteAuditTrailTests
{
    private readonly IAuditLogger _auditLogger = Substitute.For<IAuditLogger>();
    private readonly IRequestContext _request = Substitute.For<IRequestContext>();
    private readonly IClientInfo _client = Substitute.For<IClientInfo>();
    private readonly Guid _actor = Guid.NewGuid();

    private InviteAuditTrail Trail()
    {
        _request.UserId.Returns(_actor);
        _request.Role.Returns("ExamAdmin");
        _request.CorrelationId.Returns("trace-1");
        return new InviteAuditTrail(_auditLogger, _request, _client);
    }

    [Fact]
    public async Task AChangedAddress_IsRecordedForTheInvitation_WithoutEitherAddress()
    {
        var inviteId = Guid.NewGuid();
        var examId = Guid.NewGuid();

        await Trail().HandleAsync(new InviteEmailChangedEvent(inviteId, examId, "new@example.com"), CancellationToken.None);

        await _auditLogger.Received(1).RecordAsync(
            Arg.Is<AuditEntry>(entry =>
                entry.Action == "Invite.EmailChanged"
                && entry.EntityType == "Invite"
                && entry.EntityId == inviteId.ToString()
                && entry.ActorUserId == _actor
                && entry.ActorRole == "ExamAdmin"
                && entry.Metadata.Count == 1
                && entry.Metadata["examId"] == examId.ToString()
                && !entry.Metadata.Values.Any(value => value.Contains('@'))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADirectLinkMade_IsRecordedByCodeId_WithTheStaffMemberWhoMadeIt()
    {
        var inviteId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        var codeId = Guid.NewGuid();

        await Trail().HandleAsync(new InviteDirectLinkIssuedEvent(inviteId, examId, codeId), CancellationToken.None);

        await _auditLogger.Received(1).RecordAsync(
            Arg.Is<AuditEntry>(entry =>
                entry.Action == "Invite.DirectLinkIssued"
                && entry.EntityId == inviteId.ToString()
                && entry.ActorUserId == _actor
                && entry.Metadata["codeId"] == codeId.ToString()),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADirectLinkUsed_NamesTheStudentWhoSignedIn_NotWhoseTokenCameWithTheRequest_AndWhereFrom()
    {
        var student = Guid.NewGuid();
        var inviteId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        var codeId = Guid.NewGuid();
        _client.IpAddress.Returns("203.0.113.9");

        // Trail() sets the ambient request user to someone else: an anonymous call can carry a stale or foreign token.
        await Trail().HandleAsync(new InviteDirectLinkUsedEvent(inviteId, examId, codeId, student, AccountCreated: true), CancellationToken.None);

        await _auditLogger.Received(1).RecordAsync(
            Arg.Is<AuditEntry>(entry =>
                entry.Action == "Invite.DirectLinkUsed"
                && entry.EntityId == inviteId.ToString()
                && entry.ActorUserId == student
                && entry.ActorUserId != _actor
                && entry.ActorRole == "Candidate"
                && entry.Metadata["userId"] == student.ToString()
                && entry.Metadata["codeId"] == codeId.ToString()
                && entry.Metadata["accountCreated"] == "True"
                && entry.Metadata["ip"] == "203.0.113.9"
                && !entry.Metadata.Values.Any(value => value.Contains('@'))),
            Arg.Any<CancellationToken>());
    }
}
