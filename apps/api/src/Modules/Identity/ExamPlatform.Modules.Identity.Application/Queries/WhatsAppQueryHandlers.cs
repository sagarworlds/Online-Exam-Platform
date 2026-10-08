using ExamPlatform.Modules.Identity.Application.Dtos;
using ExamPlatform.Modules.Identity.Application.Ports;

namespace ExamPlatform.Modules.Identity.Application.Queries;

/// <summary>Reports where the WhatsApp configuration stands, for the administrator's test page. It calls nobody and shows no secret.</summary>
public sealed class GetWhatsAppStatusHandler(IWhatsAppDiagnostics diagnostics)
{
    /// <summary>Reviews the configuration.</summary>
    public WhatsAppStatusDto Handle() => diagnostics.GetStatus();
}

/// <summary>Reports what Meta has said about a test message since it was accepted.</summary>
public sealed class GetWhatsAppDeliveryHandler(IWhatsAppDiagnostics diagnostics)
{
    /// <summary>Returns the latest report, or <c>NotReported</c> when there is none.</summary>
    /// <param name="messageId">WhatsApp's id for the message, from the send's answer.</param>
    public WhatsAppDeliveryDto Handle(string messageId) => diagnostics.FindDelivery(messageId);
}
