using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain.Exceptions;

/// <summary>A display name is blank or longer than <see cref="User.MaxDisplayNameLength"/> characters once trimmed.</summary>
public sealed class InvalidDisplayNameError() : DomainException(
    $"The display name must be 1 to {User.MaxDisplayNameLength} characters long, not counting leading or trailing spaces.")
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_display_name";
}
