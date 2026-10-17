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
    private readonly Guid _actor = Guid.NewGuid();

    private InviteAuditTrail Trail()
    {
        _request.UserId.Returns(_actor);
        _request.Role.Returns("ExamAdmin");
        _request.CorrelationId.Returns("trace-1");
        return new InviteAuditTrail(_auditLogger, _request);
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
}
