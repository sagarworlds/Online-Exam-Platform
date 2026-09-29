using ExamPlatform.Modules.Admin.Domain;

namespace ExamPlatform.Modules.Admin.UnitTests;

public class AuditLogTests
{
    [Fact]
    public void Create_SetsAllSuppliedFields()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var actorUserId = Guid.NewGuid();
        var metadata = new Dictionary<string, string> { ["roleName"] = "SuperAdmin" };

        var entry = AuditLog.Create(now, actorUserId, "SuperAdmin", "Identity.RoleAssigned", "User", "user-123", metadata, "corr-1");

        Assert.Equal(now, entry.OccurredAtUtc);
        Assert.Equal(actorUserId, entry.ActorUserId);
        Assert.Equal("SuperAdmin", entry.ActorRole);
        Assert.Equal("Identity.RoleAssigned", entry.Action);
        Assert.Equal("User", entry.EntityType);
        Assert.Equal("user-123", entry.EntityId);
        Assert.Equal("SuperAdmin", entry.Metadata["roleName"]);
        Assert.Equal("corr-1", entry.CorrelationId);
    }

    [Fact]
    public void Create_WithoutActor_AllowsNullActorFields()
    {
        var entry = AuditLog.Create(
            DateTime.UtcNow, null, null, "System.RetentionJobRan", "None", "n/a",
            new Dictionary<string, string>(), null);

        Assert.Null(entry.ActorUserId);
        Assert.Null(entry.ActorRole);
    }
}
