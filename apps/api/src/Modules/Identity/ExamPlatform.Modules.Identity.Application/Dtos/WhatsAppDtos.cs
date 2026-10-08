namespace ExamPlatform.Modules.Identity.Application.Dtos;

/// <summary>One setting WhatsApp depends on, and whether it is in place.</summary>
/// <param name="Setting">The environment variable that sets it, for example <c>WhatsApp__AccessToken</c>.</param>
/// <param name="IsSet">Whether it has a value (for the master switch, whether it is on).</param>
/// <param name="Required">Whether nothing can be sent without it.</param>
/// <param name="Purpose">What it is for, in plain words.</param>
/// <param name="Value">The value, for settings that are not secret; null for a secret or one that is not set.</param>
public sealed record WhatsAppSettingDto(string Setting, bool IsSet, bool Required, string Purpose, string? Value);

/// <summary>Where the WhatsApp configuration stands, worked out from the settings alone. It never contains a secret.</summary>
/// <param name="Enabled">Whether the master switch is on.</param>
/// <param name="CanSendMessages">Whether the switch is on and the access token and phone number id are set.</param>
/// <param name="CanSendTemplate">Whether <paramref name="CanSendMessages"/> and the sign-in template is named too.</param>
/// <param name="CanTrackDelivery">Whether delivery reports from Meta are accepted (the webhook is configured).</param>
/// <param name="SignInCodesUseWhatsApp">Whether phone sign-in codes are routed to WhatsApp.</param>
/// <param name="InviteCodesUseWhatsApp">Whether invitations also send the exam code on WhatsApp.</param>
/// <param name="Settings">Every setting and its state.</param>
/// <param name="Problems">What stops WhatsApp working, as sentences.</param>
/// <param name="Notes">Optional things worth knowing, as sentences.</param>
public sealed record WhatsAppStatusDto(
    bool Enabled,
    bool CanSendMessages,
    bool CanSendTemplate,
    bool CanTrackDelivery,
    bool SignInCodesUseWhatsApp,
    bool InviteCodesUseWhatsApp,
    IReadOnlyList<WhatsAppSettingDto> Settings,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Notes);

/// <summary>Why a WhatsApp message did not go, for the administrator running the test.</summary>
/// <param name="Kind">The kind of failure, a stable name such as <c>RecipientNotAllowed</c>.</param>
/// <param name="Explanation">What happened and what to do about it, in plain words.</param>
/// <param name="MetaCode">Meta's error code, when the failure came from Meta.</param>
/// <param name="MetaMessage">Meta's own message, when the failure came from Meta.</param>
/// <param name="HttpStatus">The HTTP status Meta answered with, when it answered.</param>
public sealed record WhatsAppFailureDto(string Kind, string Explanation, int? MetaCode, string? MetaMessage, int? HttpStatus);

/// <summary>What happened to a test message.</summary>
/// <param name="Sent">Whether WhatsApp accepted it. Acceptance is not delivery: see <see cref="WhatsAppDeliveryDto"/>.</param>
/// <param name="MessageId">WhatsApp's id for the message, for following its delivery; null when it was not accepted.</param>
/// <param name="To">The recipient, masked to its last two digits; null when the number was not usable.</param>
/// <param name="Mode">What was sent: <c>Text</c> or <c>SignInTemplate</c>.</param>
/// <param name="DeliveryTracking">Whether delivery reports from Meta are accepted, so the message's fate can be followed.</param>
/// <param name="Note">Something worth telling the administrator about what was sent; null when there is nothing.</param>
/// <param name="Failure">Why it was not accepted; null when it was.</param>
public sealed record WhatsAppSendResultDto(
    bool Sent, string? MessageId, string? To, string Mode, bool DeliveryTracking, string? Note, WhatsAppFailureDto? Failure);

/// <summary>What Meta has reported about a message since it was accepted.</summary>
/// <param name="MessageId">WhatsApp's id for the message.</param>
/// <param name="Status"><c>NotReported</c> until Meta says anything, then <c>sent</c>, <c>delivered</c>, <c>read</c> or <c>failed</c>.</param>
/// <param name="Recipient">The recipient, masked to its last two digits, once reported.</param>
/// <param name="UpdatedAtUtc">When the latest report arrived.</param>
/// <param name="Failure">Why it failed, when <paramref name="Status"/> is <c>failed</c>.</param>
public sealed record WhatsAppDeliveryDto(string MessageId, string Status, string? Recipient, DateTime? UpdatedAtUtc, WhatsAppFailureDto? Failure);
