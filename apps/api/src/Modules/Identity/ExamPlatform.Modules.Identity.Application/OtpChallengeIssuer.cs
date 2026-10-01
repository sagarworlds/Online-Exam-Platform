using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Identity.Application;

/// <summary>
/// Shared "generate, store, send" logic for issuing an OTP challenge, used by
/// every login-adjacent flow (candidate login, registration confirmation, staff
/// 2FA step) so the generation/hashing/sending steps can never drift between them.
/// Does not commit the unit of work — the calling handler decides when to save,
/// so a challenge can be persisted atomically alongside other changes (e.g. a
/// newly created <see cref="User"/> during registration, or the earlier challenges
/// a new one supersedes).
/// </summary>
public sealed class OtpChallengeIssuer(
    IOtpChallengeRepository challengeRepository,
    IOtpCodeGenerator codeGenerator,
    IOtpSender otpSender,
    Clock clock)
{
    private static readonly TimeSpan Validity = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Supersedes every still-verifiable challenge for the same destination and purpose,
    /// generates a code, stores its hash as a new challenge, and sends the plaintext code
    /// to the destination.
    /// </summary>
    /// <param name="userId">The user this challenge is for, if one already exists.</param>
    /// <param name="channel">How to deliver the code.</param>
    /// <param name="destination">The email address or phone number to deliver to.</param>
    /// <param name="purpose">What the challenge authorizes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The id of the newly issued challenge.</returns>
    public async Task<Guid> IssueAsync(
        Guid? userId,
        OtpChannel channel,
        string destination,
        OtpPurpose purpose,
        CancellationToken cancellationToken)
    {
        var nowUtc = clock.UtcNow;

        // Only the newest code for a destination and purpose stays verifiable, so the
        // five-attempt budget applies to one code at a time. Without this, requesting N
        // codes would leave N live challenges and allow 5N guesses at them (NFR-5). The
        // superseded rows are saved with the caller's commit, and their xmin row version
        // turns a race with a parallel verify of one of them into a 409, not a lost update.
        var outstanding = await challengeRepository.GetOutstandingAsync(destination, purpose, nowUtc, cancellationToken);
        foreach (var earlier in outstanding)
        {
            earlier.Supersede(nowUtc);
        }

        var code = codeGenerator.GenerateCode();
        var codeHash = codeGenerator.Hash(code);

        var challenge = OtpChallenge.Issue(userId, channel, destination, codeHash, purpose, nowUtc, Validity);
        await challengeRepository.AddAsync(challenge, cancellationToken);

        await otpSender.SendAsync(channel, destination, code, cancellationToken);

        return challenge.Id;
    }
}
