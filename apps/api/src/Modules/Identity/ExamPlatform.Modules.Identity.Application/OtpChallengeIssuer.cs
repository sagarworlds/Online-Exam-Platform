using System.Security.Cryptography;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Identity.Application;

/// <summary>
/// Shared "generate, store, send" logic for issuing an OTP challenge, used by
/// every login-adjacent flow (candidate login, registration confirmation, staff
/// 2FA step) so the generation/hashing/sending steps can never drift between them,
/// plus the never-sent decoy that candidate login issues instead when a destination
/// must not get a usable code (<see cref="IssueDecoyAsync"/>).
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
        await SupersedeOutstandingAsync(destination, purpose, nowUtc, cancellationToken);

        var code = codeGenerator.GenerateCode();
        var codeHash = codeGenerator.Hash(code);

        var challenge = OtpChallenge.Issue(
            userId, channel, destination, codeHash, purpose, nowUtc, Validity, revealableCode: code);
        await challengeRepository.AddAsync(challenge, cancellationToken);

        await otpSender.SendAsync(channel, destination, code, cancellationToken);

        return challenge.Id;
    }

    /// <summary>
    /// Issues a challenge that looks exactly like a real one to the caller but can never be
    /// verified and is never sent, for a destination that must not receive a usable code
    /// (no account, a locked account, or a staff account that must use password + 2FA).
    /// It supersedes outstanding challenges for the destination and purpose just as
    /// <see cref="IssueAsync"/> does, and is stored without a user.
    /// </summary>
    /// <param name="channel">The channel the caller asked for.</param>
    /// <param name="destination">The email address or phone number the caller asked for.</param>
    /// <param name="purpose">The purpose a real challenge would have had.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The id of the decoy challenge.</returns>
    public async Task<Guid> IssueDecoyAsync(
        OtpChannel channel,
        string destination,
        OtpPurpose purpose,
        CancellationToken cancellationToken)
    {
        var nowUtc = clock.UtcNow;
        await SupersedeOutstandingAsync(destination, purpose, nowUtc, cancellationToken);

        // The decoy is persisted, not just given a random id, so verifying it behaves like a
        // real challenge: wrong codes count, and after five the caller gets the same 429.
        // An unknown id would answer 404 and hand the account enumeration back one step later.
        // Its hash is of 64 random hex characters, which no 6-digit code can ever match.
        var unguessable = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var challenge = OtpChallenge.Issue(
            null, channel, destination, codeGenerator.Hash(unguessable), purpose, nowUtc, Validity);
        await challengeRepository.AddAsync(challenge, cancellationToken);

        return challenge.Id;
    }

    private async Task SupersedeOutstandingAsync(
        string destination,
        OtpPurpose purpose,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
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
    }
}
