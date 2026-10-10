using System.ComponentModel.DataAnnotations;

namespace ExamPlatform.Modules.Identity.Application.DataRequests;

/// <summary>
/// How long staff have to answer a data-principal request (FR-48), read from the <c>Identity:DataRequests</c> section.
/// </summary>
/// <remarks>
/// Provisional. The owner has not set the service period, and the grievance contact that goes with it is not in the repository (data map
/// OP2). Thirty days is an engineering default until the owner decides; configuration can change it without a code change.
/// </remarks>
public sealed class DataRequestOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "Identity:DataRequests";

    /// <summary>Days from receipt to the answer's due time.</summary>
    [Range(1, 365)]
    public int ServiceDays { get; set; } = 30;
}
