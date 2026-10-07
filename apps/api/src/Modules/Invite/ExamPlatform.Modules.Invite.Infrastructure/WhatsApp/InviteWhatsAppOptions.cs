namespace ExamPlatform.Modules.Invite.Infrastructure.WhatsApp;

/// <summary>
/// Whether, and with which approved template, an invitation's exam code is also sent on WhatsApp, bound from
/// <c>Invite:WhatsApp</c>. Off until <see cref="TemplateName"/> is set: sending an organisation's invitations through the
/// operator's WhatsApp number is the operator's decision, not a default.
/// </summary>
public sealed class InviteWhatsAppOptions
{
    /// <summary>The configuration section the options are read from.</summary>
    public const string SectionName = "Invite:WhatsApp";

    /// <summary>
    /// The name of the approved template that carries the invitation. Its body takes four values, in this order: the exam's name,
    /// the exam code, the link, and when the code expires. Blank turns invitations on WhatsApp off.
    /// </summary>
    public string? TemplateName { get; set; }

    /// <summary>The language that template was approved in (for example <c>en</c>); it must match that template.</summary>
    public string TemplateLanguage { get; set; } = "en";

    /// <summary>Whether a template is named.</summary>
    public bool HasTemplate => !string.IsNullOrWhiteSpace(TemplateName);
}
