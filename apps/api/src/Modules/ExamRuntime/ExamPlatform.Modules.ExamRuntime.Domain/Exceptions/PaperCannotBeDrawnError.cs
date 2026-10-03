using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The questions a candidate's paper should be drawn from are fewer than the exam's rules ask for, so the attempt cannot start.</summary>
/// <param name="problem">What is short, for the administrator who has to fix the exam or the bank.</param>
public sealed class PaperCannotBeDrawnError(string problem) : DomainException(problem)
{
    /// <inheritdoc />
    public override string ErrorCode => "paper_cannot_be_drawn";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
