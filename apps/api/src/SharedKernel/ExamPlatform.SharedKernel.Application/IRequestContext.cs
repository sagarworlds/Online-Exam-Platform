namespace ExamPlatform.SharedKernel.Application;

/// <summary>
/// Who is acting, and in which request. Lets code that reacts to a domain event (which only knows what
/// happened, not who did it) name the actor in the audit trail without every command and event having to
/// carry the caller through (FR-40). Outside a request (a seeder, a background job) there is no actor.
/// </summary>
public interface IRequestContext
{
    /// <summary>The signed-in user making the request, or null when there is none.</summary>
    Guid? UserId { get; }

    /// <summary>The user's primary role name, a snapshot for the audit trail; null when there is no signed-in user.</summary>
    string? Role { get; }

    /// <summary>An identifier shared by everything done for this request, so audit entries can be tied to its logs; null outside a request.</summary>
    string? CorrelationId { get; }
}
