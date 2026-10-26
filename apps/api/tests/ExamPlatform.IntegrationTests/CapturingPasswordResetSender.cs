using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using ExamPlatform.Modules.Identity.Application.Ports;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Test double replacing the real <see cref="IPasswordResetSender"/> so tests can read the reset code that would have been e-mailed,
/// instead of scraping log output. It keeps the code and the lifetime the message would have stated, per address.
/// </summary>
public sealed class CapturingPasswordResetSender : IPasswordResetSender
{
    private readonly ConcurrentDictionary<string, (string Code, TimeSpan Validity)> _byAddress = new();

    /// <inheritdoc />
    public Task SendAsync(string email, string code, TimeSpan validity, CancellationToken cancellationToken)
    {
        _byAddress[email] = (code, validity);
        return Task.CompletedTask;
    }

    /// <summary>The most recent reset code sent to an address.</summary>
    /// <param name="email">The e-mail address.</param>
    public string GetLastCode(string email) => _byAddress[email].Code;

    /// <summary>The lifetime the most recent message to an address stated.</summary>
    /// <param name="email">The e-mail address.</param>
    public TimeSpan GetLastValidity(string email) => _byAddress[email].Validity;

    /// <summary>
    /// Whether any reset message has been sent to an address, so a test can assert that a request deliberately sent nothing, which
    /// <see cref="GetLastCode"/> can only express as a <see cref="KeyNotFoundException"/>.
    /// </summary>
    /// <param name="email">The e-mail address.</param>
    public bool HasSentTo(string email) => _byAddress.ContainsKey(email);

    /// <summary>Reads the most recent reset code sent to an address, if one was sent.</summary>
    /// <param name="email">The e-mail address.</param>
    /// <param name="code">The most recent code, when this returns true.</param>
    public bool TryGetLastCode(string email, [MaybeNullWhen(false)] out string code)
    {
        var found = _byAddress.TryGetValue(email, out var sent);
        code = found ? sent.Code : null;
        return found;
    }
}
