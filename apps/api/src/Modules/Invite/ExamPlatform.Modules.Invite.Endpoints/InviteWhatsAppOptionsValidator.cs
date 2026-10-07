using ExamPlatform.Modules.Invite.Infrastructure.WhatsApp;
using ExamPlatform.SharedKernel.Infrastructure.WhatsApp;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.Invite.Endpoints;

/// <summary>
/// Refuses, at startup, an <see cref="InviteWhatsAppOptions.TemplateName"/> without the WhatsApp settings needed to send it, so a host
/// told to send invitations on WhatsApp cannot start and then skip WhatsApp on every invitation without a word. A host that names no
/// template is not asked for anything.
/// </summary>
/// <param name="whatsApp">The platform's WhatsApp settings.</param>
internal sealed class InviteWhatsAppOptionsValidator(IOptions<WhatsAppOptions> whatsApp) : IValidateOptions<InviteWhatsAppOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, InviteWhatsAppOptions options)
    {
        if (!options.HasTemplate)
        {
            return ValidateOptionsResult.Success;
        }

        var missing = whatsApp.Value.MissingForSending();
        return missing.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{InviteWhatsAppOptions.SectionName}:TemplateName is set, but "
                + $"{string.Join(", ", missing.Select(setting => $"{WhatsAppOptions.SectionName}:{setting}"))} "
                + (missing.Count == 1 ? "is" : "are") + " not set.");
    }
}
