namespace ExamPlatform.SharedKernel.Infrastructure.WhatsApp;

/// <summary>
/// The shape of the messages the platform sends, in one place so the administrator's test sends exactly what sign-in sends: a test that
/// proves a different message works proves nothing about the real one.
/// </summary>
public static class WhatsAppMessages
{
    /// <summary>
    /// A one-time code through an approved Authentication template: the code fills the body and the "copy code" button, which takes it
    /// again.
    /// </summary>
    /// <param name="to">The recipient's number as <see cref="WhatsAppPhoneNumber.Normalize"/> gives it.</param>
    /// <param name="templateName">The approved Authentication template's name.</param>
    /// <param name="languageCode">The language the template was approved in.</param>
    /// <param name="code">The code.</param>
    public static WhatsAppTemplateMessage AuthenticationCode(string to, string templateName, string languageCode, string code) =>
        new(to, templateName, languageCode, [code], code);
}
