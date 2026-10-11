using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Proctoring.Domain.Exceptions;

/// <summary>A risk assessment with this id does not exist (404).</summary>
/// <param name="assessmentId">The id asked for.</param>
public sealed class RiskAssessmentNotFoundError(Guid assessmentId) : DomainException($"Risk assessment '{assessmentId}' not found.")
{
    /// <inheritdoc />
    public override string ErrorCode => "risk_assessment_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}

/// <summary>The attempt was not flagged, so it is not in the review queue and there is nothing to decide on (409).</summary>
public sealed class RiskFlagNotRaisedError() : DomainException("This attempt was not flagged, so there is nothing to review.")
{
    /// <inheritdoc />
    public override string ErrorCode => "risk_flag_not_raised";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}

/// <summary>A reviewer has already marked this flag reviewed or dismissed it (409). The decision stands and a later scan does not overwrite it.</summary>
public sealed class RiskFlagAlreadyDecidedError() : DomainException("A reviewer has already decided on this flag.")
{
    /// <inheritdoc />
    public override string ErrorCode => "risk_flag_already_decided";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}

/// <summary>A reviewer's note is missing where one is required, or too long (400).</summary>
/// <param name="problem">What is wrong with the note, in words a reviewer can act on.</param>
public sealed class InvalidRiskDecisionError(string problem) : DomainException(problem)
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_risk_decision";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}

/// <summary>The exam does not exist, so there is nothing to scan or to list flags for (404).</summary>
/// <param name="examId">The exam asked for.</param>
public sealed class ExamNotFoundError(Guid examId) : DomainException($"Exam '{examId}' not found.")
{
    /// <inheritdoc />
    public override string ErrorCode => "exam_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
