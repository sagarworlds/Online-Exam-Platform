namespace ExamPlatform.Modules.ExamRuntime.Application.Dtos;

/// <summary>What a bulk "recheck with AI" queued.</summary>
/// <param name="AttemptsQueued">How many attempts had at least one typed answer sent to the AI evaluator.</param>
/// <param name="AnswersQueued">How many typed answers were sent. Zero when every typed answer already has a verdict or is waiting for one.</param>
public sealed record AiRecheckResultDto(int AttemptsQueued, int AnswersQueued);
