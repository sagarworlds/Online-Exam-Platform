namespace ExamPlatform.Modules.Guardian.Application.Ports;

/// <summary>Builds the page a guardian opens to confirm a candidate link.</summary>
public interface IGuardianConsentLinkBuilder
{
    /// <summary>The web link that carries a confirmation code.</summary>
    /// <param name="rawToken">The code, as it goes into the link.</param>
    string Build(string rawToken);
}
