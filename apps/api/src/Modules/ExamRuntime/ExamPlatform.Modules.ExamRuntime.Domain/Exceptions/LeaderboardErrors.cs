using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>
/// The batch asked for is not one the candidate belongs to on this exam. Answered as not found, so a candidate cannot probe for the
/// rankings of batches they are not in.
/// </summary>
public sealed class LeaderboardBatchNotFoundError() : DomainException("You are not a member of a batch with that id on this exam.")
{
    /// <inheritdoc />
    public override string ErrorCode => "leaderboard_batch_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}

/// <summary>The subject asked for is not one of the subjects of this exam's questions.</summary>
public sealed class LeaderboardSubjectNotFoundError() : DomainException("No question of this exam is filed under that subject.")
{
    /// <inheritdoc />
    public override string ErrorCode => "leaderboard_subject_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}

/// <summary>The leaderboard asked for is not one the platform has. The boards are overall, batch and subject.</summary>
public sealed class InvalidLeaderboardBoardError() : DomainException("The leaderboard must be overall, batch or subject.")
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_leaderboard_board";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
