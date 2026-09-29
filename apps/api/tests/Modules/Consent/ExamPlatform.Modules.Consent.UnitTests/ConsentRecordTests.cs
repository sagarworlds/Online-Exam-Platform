using ExamPlatform.Modules.Consent.Domain;
using ExamPlatform.Modules.Consent.Domain.Events;
using ExamPlatform.Modules.Consent.Domain.Exceptions;

namespace ExamPlatform.Modules.Consent.UnitTests;

public class ConsentRecordTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Grant_CreatesActiveRecordAndEmitsConsentGrantedEvent()
    {
        var subjectId = Guid.NewGuid();
        var noticeVersionId = Guid.NewGuid();
        var givenById = Guid.NewGuid();

        var record = ConsentRecord.Grant(subjectId, ConsentPurpose.PrivacyNotice, noticeVersionId, givenById, Now);

        Assert.True(record.IsActive);
        var grantedEvent = Assert.Single(record.DomainEvents.OfType<ConsentGrantedEvent>());
        Assert.Equal(subjectId, grantedEvent.SubjectId);
        Assert.Equal(ConsentPurpose.PrivacyNotice, grantedEvent.Purpose);
    }

    [Fact]
    public void Withdraw_MarksRecordInactiveAndEmitsConsentWithdrawnEvent()
    {
        var record = ConsentRecord.Grant(Guid.NewGuid(), ConsentPurpose.PrivacyNotice, Guid.NewGuid(), Guid.NewGuid(), Now);
        var withdrawnById = Guid.NewGuid();

        record.Withdraw(withdrawnById, Now.AddDays(1));

        Assert.False(record.IsActive);
        var withdrawnEvent = Assert.Single(record.DomainEvents.OfType<ConsentWithdrawnEvent>());
        Assert.Equal(withdrawnById, withdrawnEvent.WithdrawnById);
    }

    [Fact]
    public void Withdraw_WhenAlreadyWithdrawn_ThrowsConsentAlreadyWithdrawnError()
    {
        var record = ConsentRecord.Grant(Guid.NewGuid(), ConsentPurpose.PrivacyNotice, Guid.NewGuid(), Guid.NewGuid(), Now);
        record.Withdraw(Guid.NewGuid(), Now.AddDays(1));

        var act = () => record.Withdraw(Guid.NewGuid(), Now.AddDays(2));

        Assert.Throws<ConsentAlreadyWithdrawnError>(act);
    }
}
