namespace ExamPlatform.Modules.Admin.Contracts;

/// <summary>
/// Cross-module capability: record an immutable audit entry (FR-40). Any module
/// depends on this abstraction via <c>Admin.Contracts</c> only — never on
/// Admin's storage or domain types — so the audit trail stays centrally owned
/// while every other module stays decoupled from it (DIP/ISP).
/// </summary>
public interface IAuditLogger
{
    /// <summary>Records one audit entry.</summary>
    /// <param name="entry">The fact to record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken);
}
