using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Invite.Domain.Exceptions;

/// <summary>The caller asked for a direct sign-in link without holding the permission to make one.</summary>
public sealed class DirectLinkNotPermittedError()
    : DomainException("You are not allowed to make a direct sign-in link.")
{
    /// <inheritdoc />
    public override string ErrorCode => "direct_link_not_permitted";

    /// <inheritdoc />
    public override int HttpStatusCode => 403;
}
