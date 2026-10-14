using ExamPlatform.Modules.ExamAuthoring.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.Application.Ports;

/// <summary>
/// Resolves the proctoring rules that apply to one candidate (requirements sections 8 and 11, FR-26). Every runtime handler reads an exam's
/// rules through this, so a rule that depends on the candidate is added here and nowhere else, and the runtime never checks a profile's name.
/// </summary>
public interface IProctorPolicy
{
    /// <summary>The exam as this candidate sits it: the rules the platform enforces, and the rules the candidate is told about.</summary>
    /// <param name="exam">The exam as authored.</param>
    /// <param name="alternateFormats">The alternate formats the candidate has (FR-49).</param>
    /// <returns>The exam with this candidate's effective proctoring rules.</returns>
    ExamSnapshot Resolve(ExamSnapshot exam, IEnumerable<string> alternateFormats);
}
