using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain.Exceptions;

/// <summary>
/// The date of birth given at registration is missing or implausible. It decides whether
/// the account is a minor's (FR-43), so it is refused rather than stored as given.
/// </summary>
public sealed class InvalidDateOfBirthError : DomainException
{
    private InvalidDateOfBirthError(string message) : base(message)
    {
    }

    /// <inheritdoc />
    public override string ErrorCode => "invalid_date_of_birth";

    /// <summary>No date of birth was given.</summary>
    public static InvalidDateOfBirthError Missing() => new("A date of birth is required.");

    /// <summary>The date of birth is later than today.</summary>
    public static InvalidDateOfBirthError InTheFuture() => new("The date of birth cannot be in the future.");

    /// <summary>The date of birth is further back than any living person's.</summary>
    public static InvalidDateOfBirthError TooLongAgo() =>
        new($"The date of birth cannot be more than {User.MaximumPlausibleAgeYears} years ago.");
}
