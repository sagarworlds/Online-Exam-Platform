namespace ExamPlatform.Modules.ExamRuntime.Application.Dtos;

/// <summary>One enrolled candidate of an exam, with their attempts and whether another can be granted, as staff see them.</summary>
/// <param name="CandidateId">The candidate's account id.</param>
/// <param name="Email">The address they were invited at.</param>
/// <param name="AttemptsAllowed">How many attempts they may make in all: the exam's limit per candidate, plus each extra attempt granted.</param>
/// <param name="AttemptsUsed">How many they have started.</param>
/// <param name="CanGrant">Whether another extra attempt may be granted now: they have used every attempt they hold, and the exam can still be started.</param>
/// <param name="Attempts">Every attempt they have made, oldest first.</param>
public sealed record ExamCandidateDto(
    Guid CandidateId,
    string Email,
    int AttemptsAllowed,
    int AttemptsUsed,
    bool CanGrant,
    IReadOnlyList<AttemptSummaryDto> Attempts);

/// <summary>An exam's enrolled candidates and how each has got on, for the staff who may grant extra attempts.</summary>
/// <param name="ExamId">The exam.</param>
/// <param name="ExamName">The exam's name.</param>
/// <param name="WindowClosed">Whether nobody can start an attempt any more, so no extra attempt can be granted.</param>
/// <param name="AttemptsPerCandidate">How many attempts every candidate has before any extra is granted, as the exam's author set it.</param>
/// <param name="Candidates">The enrolled candidates, by e-mail address.</param>
public sealed record ExamAttemptsDto(Guid ExamId, string ExamName, bool WindowClosed, int AttemptsPerCandidate, IReadOnlyList<ExamCandidateDto> Candidates);
