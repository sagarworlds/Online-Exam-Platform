using ExamPlatform.Modules.Invite.Infrastructure.WhatsApp;
using ExamPlatform.SharedKernel.Infrastructure.WhatsApp;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.Invite.Endpoints;

/// <summary>
/// Refuses, at startup, an <see cref="InviteWhatsAppOptions.TemplateName"/> without the WhatsApp settings needed to send it, once
/// WhatsApp is switched on (<c>WhatsApp:Enabled</c>), so a host told to send invitations on WhatsApp cannot start and then skip WhatsApp
/// on every invitation without a word. A host that names no template, or has WhatsApp switched off, is not asked for anything: with the
/// switch off nothing is sent, and it can be turned off again in an emergency without the host refusing to start.
/// </summary>
/// <param name="whatsApp">The platform's WhatsApp settings.</param>
internal sealed class InviteWhatsAppOptionsValidator(IOptions<WhatsAppOptions> whatsApp) : IValidateOptions<InviteWhatsAppOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, InviteWhatsAppOptions options)
    {
        if (!options.HasTemplate || !whatsApp.Value.IsEnabled)
        {
            return ValidateOptionsResult.Success;
        }

        var missing = whatsApp.Value.MissingForSending();
        return missing.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{InviteWhatsAppOptions.SectionName}:TemplateName is set and WhatsApp is switched on, but "
                + $"{string.Join(", ", missing.Select(setting => $"{WhatsAppOptions.SectionName}:{setting}"))} "
                + (missing.Count == 1 ? "is" : "are") + " not set.");
    }
}
