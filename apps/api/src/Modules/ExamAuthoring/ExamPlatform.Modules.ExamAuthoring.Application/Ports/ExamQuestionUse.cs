using ExamPlatform.Modules.ExamAuthoring.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Application.Ports;

/// <summary>One question sitting in one exam, as far as another module needs to know.</summary>
/// <param name="QuestionId">The question-bank id of the question.</param>
/// <param name="ExamId">The exam that holds it.</param>
/// <param name="ExamName">That exam's name.</param>
/// <param name="Status">That exam's status.</param>
public sealed record ExamQuestionUse(Guid QuestionId, Guid ExamId, string ExamName, ExamStatus Status);
