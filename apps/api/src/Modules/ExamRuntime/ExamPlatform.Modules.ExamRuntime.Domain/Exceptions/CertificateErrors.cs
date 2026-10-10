using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The candidate has no name on their account, which a certificate cannot be issued without.</summary>
public sealed class CertificateNameMissingError() : DomainException("Add your name to your profile before a certificate can be issued.")
{
    /// <inheritdoc />
    public override string ErrorCode => "certificate_name_missing";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}

/// <summary>
/// The candidate's name or the exam's name uses characters the certificate's built-in font cannot show. Refused rather than printed as
/// substitutes, so a certificate never misspells a name.
/// </summary>
public sealed class CertificateTextUnsupportedError() : DomainException(
    "This certificate cannot show every character in your name or the exam's name yet. Latin letters, digits and common punctuation are supported.")
{
    /// <inheritdoc />
    public override string ErrorCode => "certificate_text_unsupported";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
