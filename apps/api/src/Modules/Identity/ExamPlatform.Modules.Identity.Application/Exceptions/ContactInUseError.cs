using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Application.Exceptions;

/// <summary>The e-mail address or mobile number staff want to give a student already belongs to another account.</summary>
public sealed class ContactInUseError : DomainException
{
    private ContactInUseError(string message) : base(message)
    {
    }

    /// <summary>Another account already holds the e-mail address.</summary>
    public static ContactInUseError Email() => new("Another account already uses that e-mail address.");

    /// <summary>Another account already holds the mobile number.</summary>
    public static ContactInUseError Phone() => new("Another account already uses that mobile number.");

    /// <inheritdoc />
    public override string ErrorCode => "contact_in_use";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
