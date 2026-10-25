using ExamPlatform.Modules.ExamRuntime.Application.Ports;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.AiEvaluation;

/// <summary>
/// An evaluator for one AI provider, which owns the <see cref="EvaluatorGate"/> that paces it and records where it stands. The chain of providers
/// reads the gate to skip a provider that has said "not now" and to tell staff how each one is doing.
/// </summary>
public interface IGatedTextAnswerEvaluator : ITextAnswerEvaluator
{
    /// <summary>The pacing and pause shared by every worker that asks this provider.</summary>
    EvaluatorGate Gate { get; }
}
