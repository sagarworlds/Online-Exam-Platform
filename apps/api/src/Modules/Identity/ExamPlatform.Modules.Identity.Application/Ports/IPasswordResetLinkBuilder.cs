namespace ExamPlatform.Modules.Identity.Application.Ports;

/// <summary>
/// Builds the address that opens the web app's reset page with a reset code already filled in. The address of the web app is
/// deployment configuration, so the Identity module only sees this port (DIP).
/// </summary>
public interface IPasswordResetLinkBuilder
{
    /// <summary>The full link for a reset code.</summary>
    /// <param name="code">The reset code, <c>{tokenId}:{secret}</c>.</param>
    /// <remarks>
    /// The code goes in the link's fragment (<c>#code=…</c>), not its query string. A browser never sends a fragment to a server, so the
    /// code stays out of access logs and out of the <c>Referer</c> header, which matters because the code is a credential.
    /// </remarks>
    string Build(string code);
}
