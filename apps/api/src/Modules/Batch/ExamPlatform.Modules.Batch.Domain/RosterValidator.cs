namespace ExamPlatform.Modules.Batch.Domain;

/// Validates roster data from CSV imports before adding members to batch.
public class RosterValidator
{
    private const int MaxBatchSize = 1000;
    private const int MinBatchSize = 1;

    public record ValidationResult(bool IsValid, List<string> Errors);

    public ValidationResult ValidateRoster(List<RosterEntry>? entries)
    {
        var errors = new List<string>();

        if (entries == null || entries.Count == 0)
        {
            errors.Add("Roster cannot be empty.");
            return new ValidationResult(false, errors);
        }

        if (entries.Count > MaxBatchSize)
            errors.Add($"Roster exceeds maximum size of {MaxBatchSize} members.");

        if (entries.Count < MinBatchSize)
            errors.Add($"Roster must have at least {MinBatchSize} member.");

        var emailDuplicates = entries
            .GroupBy(e => e.Email?.ToLowerInvariant())
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var email in emailDuplicates)
        {
            errors.Add($"Duplicate email in roster: {email}");
        }

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Email))
                errors.Add("Email is required for all roster entries.");

            if (!IsValidEmail(entry.Email))
                errors.Add($"Invalid email format: {entry.Email}");

            if (!string.IsNullOrWhiteSpace(entry.Phone) && !IsValidPhone(entry.Phone))
                errors.Add($"Invalid phone format: {entry.Phone}");
        }

        return new ValidationResult(errors.Count == 0, errors);
    }

    // One definition of a valid contact, shared with Batch.AddMember, so an imported roster cannot
    // hold an address the batch would refuse.
    private static bool IsValidEmail(string? email) => MemberContact.TryNormalizeEmail(email, out _);

    private static bool IsValidPhone(string? phone) =>
        string.IsNullOrWhiteSpace(phone) || MemberContact.TryNormalizePhone(phone, out _);

    public record RosterEntry(string Email, string? Phone = null);
}
