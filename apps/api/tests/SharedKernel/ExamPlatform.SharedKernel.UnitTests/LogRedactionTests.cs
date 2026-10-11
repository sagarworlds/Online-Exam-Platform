using ExamPlatform.SharedKernel.Infrastructure.Observability;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>
/// The redaction rule (NFR-6): e-mail addresses, phone numbers, tokens, passwords, OTP codes and answers never reach a log line,
/// and the values the platform writes on purpose (masked contacts, durations, dates, ids) stay readable.
/// </summary>
public class LogRedactionTests
{
    [Theory]
    [InlineData("Mail to student@example.com failed", "Mail to [redacted-email] failed")]
    [InlineData("Contact first.last+tag@school.edu.in now", "Contact [redacted-email] now")]
    [InlineData("SMTP refused rcpt <parent@example.org>", "SMTP refused rcpt <[redacted-email]>")]
    public void AnEmailAddress_IsMasked(string input, string expected) =>
        Assert.Equal(expected, LogRedaction.RedactText(input));

    [Theory]
    [InlineData("Call +91 98765 43210 today", "Call [redacted-phone] today")]
    [InlineData("Call +919876543210 today", "Call [redacted-phone] today")]
    [InlineData("Call 98765-43210 today", "Call [redacted-phone] today")]
    [InlineData("Call 9876543210 today", "Call [redacted-number] today")]
    public void APhoneNumber_IsMasked(string input, string expected) =>
        Assert.Equal(expected, LogRedaction.RedactText(input));

    [Theory]
    [InlineData("password=hunter2", "password=[redacted]")]
    [InlineData("login failed, Password: correct horse", "login failed, Password: [redacted] horse")]
    [InlineData("refresh_token=abc.def", "refresh_token=[redacted]")]
    [InlineData("api-key: example-value", "api-key: [redacted]")]
    [InlineData("OTP: 123456", "OTP: [redacted]")]
    [InlineData("answer=B", "answer=[redacted]")]
    [InlineData("GET /v1/whatsapp/webhook?hub.verify_token=s3cr3t", "GET /v1/whatsapp/webhook?hub.verify_token=[redacted]")]
    public void ALabelledSecret_IsMasked_AndItsLabelIsKept(string input, string expected) =>
        Assert.Equal(expected, LogRedaction.RedactText(input));

    [Fact]
    public void ABearerCredential_IsMasked()
    {
        var result = LogRedaction.RedactText("Authorization: Bearer abcdefghijklmnop");

        Assert.DoesNotContain("abcdefghijklmnop", result, StringComparison.Ordinal);
        Assert.Contains("[redacted-token]", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AJsonWebToken_IsMasked()
    {
        const string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl";

        Assert.Equal("token [redacted-token]", LogRedaction.RedactText($"token {jwt}"));
    }

    [Theory]
    [InlineData("Masked a***@example.com stays readable", "Masked a***@example.com stays readable")]
    [InlineData("Phone **********10 stays readable", "Phone **********10 stays readable")]
    [InlineData("Migrated 2026-10-10 in 1234 ms", "Migrated 2026-10-10 in 1234 ms")]
    [InlineData("Attempt 42 of 3, page 7", "Attempt 42 of 3, page 7")]
    [InlineData("Batch 3f2504e0-4f89-11d3-9a0c-0305e82c3301 activated", "Batch 3f2504e0-4f89-11d3-9a0c-0305e82c3301 activated")]
    public void WhatThePlatformWritesOnPurpose_IsLeftReadable(string input, string expected) =>
        Assert.Equal(expected, LogRedaction.RedactText(input));

    [Fact]
    public void NullOrEmptyText_IsAnEmptyString()
    {
        Assert.Equal(string.Empty, LogRedaction.RedactText(null));
        Assert.Equal(string.Empty, LogRedaction.RedactText(string.Empty));
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("passwd")]
    [InlineData("AccessToken")]
    [InlineData("refresh_token")]
    [InlineData("OtpCode")]
    [InlineData("otp-code")]
    [InlineData("Answer")]
    [InlineData("QuestionText")]
    [InlineData("RequestBody")]
    [InlineData("Email")]
    [InlineData("PhoneNumber")]
    public void A_PropertyNamedForSecretOrPersonalData_IsSensitive(string name) =>
        Assert.True(LogRedaction.IsSensitiveName(name));

    [Theory]
    [InlineData("MaskedDestination")]
    [InlineData("MaskedNumber")]
    [InlineData("ErrorCode")]
    [InlineData("StatusCode")]
    [InlineData("ElapsedMs")]
    [InlineData("InviteId")]
    public void A_PropertyThatHoldsNoSecret_IsNotSensitive(string name) =>
        Assert.False(LogRedaction.IsSensitiveName(name));

    [Fact]
    public void ALogLine_WithSeveralKindsOfPersonalData_LeavesNoneOfThemReadable()
    {
        const string line = "Invite to student@example.com on +91 98765 43210 failed with password=hunter2 and token eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl";

        var result = LogRedaction.RedactText(line);

        Assert.DoesNotContain("student@example.com", result, StringComparison.Ordinal);
        Assert.DoesNotContain("98765", result, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", result, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", result, StringComparison.Ordinal);
    }
}
