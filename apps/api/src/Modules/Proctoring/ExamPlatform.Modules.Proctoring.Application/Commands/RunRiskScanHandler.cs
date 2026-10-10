using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Contracts;
using ExamPlatform.Modules.Proctoring.Application.Dtos;
using ExamPlatform.Modules.Proctoring.Application.Ports;
using ExamPlatform.Modules.Proctoring.Domain;
using ExamPlatform.Modules.Proctoring.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Proctoring.Application.Commands;

/// <summary>
/// Scores every finished attempt at an exam and puts the flagged ones in the review queue (FR-27). A person starts it; nothing scores
/// attempts on its own, and a scan changes nothing about a candidate.
/// </summary>
public sealed class RunRiskScanHandler(
    IExamCatalog catalog,
    IAttemptSignalSource signals,
    IRiskAssessmentRepository assessments,
    IProctoringUnitOfWork unitOfWork,
    RiskPolicy policy,
    Clock clock,
    ProctoringAuditTrail audit)
{
    /// <summary>Scores the exam's finished attempts, rescoring open assessments and leaving decided ones as they are.</summary>
    /// <param name="examId">The exam to scan.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many attempts were scored, how many were flagged, and how many were left alone because a reviewer had decided.</returns>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <remarks>
    /// Propagates the exam runtime's error when an attempt holds an answer that cannot be marked (its exam content must be fixed first),
    /// rather than scoring that attempt on a guess.
    /// </remarks>
    public async Task<RiskScanResultDto> HandleAsync(Guid examId, CancellationToken cancellationToken)
    {
        if (await catalog.FindAsync(examId, cancellationToken) is null)
            throw new ExamNotFoundError(examId);

        var inputs = (await signals.ListFinishedAttemptsAsync(examId, cancellationToken))
            .Select(RiskFlagMapper.ToRiskInputs)
            .ToList();

        // Sharing is worked out across the whole exam at once, so every attempt is compared with all the others, not just with the ones
        // scanned before it.
        var sharedCounts = SharedAnswerAnalyzer.MaxSharedWithOneAttempt(inputs, policy.MaxSharersPerAnswer);
        var existing = (await assessments.ListForExamAsync(examId, cancellationToken)).ToDictionary(a => a.AttemptId);
        var nowUtc = clock.UtcNow;

        var scored = 0;
        var flagged = 0;
        var kept = 0;
        foreach (var input in inputs)
        {
            var outcome = RiskScorer.Score(input, sharedCounts[input.AttemptId], policy);
            if (existing.TryGetValue(input.AttemptId, out var assessment))
            {
                // A reviewer's decision is the record of what they saw. A rescan must not change it under them.
                if (assessment.Status != RiskFlagStatus.Open)
                {
                    kept++;
                    continue;
                }

                assessment.Rescore(input, outcome, nowUtc);
            }
            else
            {
                assessments.Add(RiskAssessment.Create(examId, input, outcome, nowUtc));
            }

            scored++;
            if (outcome.Flagged)
                flagged++;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await audit.RecordScanAsync(examId, scored, flagged, cancellationToken);
        return new RiskScanResultDto(examId, scored, flagged, kept);
    }
}
