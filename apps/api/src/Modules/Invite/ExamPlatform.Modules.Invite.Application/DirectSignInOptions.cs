namespace ExamPlatform.Modules.Invite.Application;

/// <summary>
/// Settings for direct sign-in links, bound to <c>Invite:DirectSignIn</c>. A link that signs someone in is a security choice an operator
/// may want to refuse outright, so it can be switched off without a release.
/// </summary>
public sealed class DirectSignInOptions
{
    /// <summary>The configuration section these options are bound to.</summary>
    public const string SectionName = "Invite:DirectSignIn";

    /// <summary>
    /// Whether direct sign-in links can be made and used. On unless turned off. Turning it off refuses new links and the use of every
    /// link already sent; the invitations themselves still work the ordinary way.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
