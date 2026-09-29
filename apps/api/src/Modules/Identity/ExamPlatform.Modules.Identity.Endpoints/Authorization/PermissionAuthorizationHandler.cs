using Microsoft.AspNetCore.Authorization;

namespace ExamPlatform.Modules.Identity.Endpoints.Authorization;

/// <summary>Succeeds a <see cref="PermissionRequirement"/> when the caller's token carries a matching "perm" claim.</summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Claims.Any(c => c.Type == "perm" && c.Value == requirement.PermissionCode))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
