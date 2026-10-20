using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Queries;
using ExamPlatform.Modules.Identity.Domain.Rbac;
using ExamPlatform.SharedKernel.Application.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.Modules.Identity.Endpoints;

/// <summary>Maps the staff routes for students' accounts: who they are, how to reach them, and correcting their profile.</summary>
public static class StudentEndpoints
{
    /// <summary>Maps <c>/v1/admin/students</c>: reading behind <c>student.read</c>, changing behind <c>student.manage</c>.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    public static void MapStudentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Each route names the permission it needs: being signed in says nothing about being staff (a candidate is signed in too).
        var students = endpoints.MapGroup("/v1/admin/students")
            .WithTags("Identity")
            .RequireAuthorization();

        students.MapGet("/", async (string? search, ListStudentsHandler handler, CancellationToken ct) =>
                Results.Ok(await handler.HandleAsync(search, ct)))
            .RequireAuthorization($"permission:{RbacCatalog.PermissionCodes.StudentRead}")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("ListStudents")
            .WithDescription("List the student accounts, first 200 matches, by name, of an optional search by name, e-mail address or phone number");

        // A POST because the addresses would not fit in a URL, though it only reads.
        students.MapPost("/lookup", async (FindStudentsRequest request, FindStudentsByEmailHandler handler, CancellationToken ct) =>
                Results.Ok(await handler.HandleAsync(request.Emails, ct)))
            .RequireAuthorization($"permission:{RbacCatalog.PermissionCodes.StudentRead}")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("FindStudentsByEmail")
            .WithDescription("Which of up to 200 e-mail addresses belong to student accounts; reads only");

        students.MapGet("/{studentId:guid}", async (Guid studentId, GetStudentHandler handler, CancellationToken ct) =>
                Results.Ok(await handler.HandleAsync(studentId, ct)))
            .RequireAuthorization($"permission:{RbacCatalog.PermissionCodes.StudentRead}")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("GetStudent")
            .WithDescription("One student's profile, with the date of birth");

        // The staff member is the authenticated caller, never a value from the body, so the audit trail names who really did it.
        students.MapPut("/{studentId:guid}", async (
                Guid studentId, UpdateStudentRequest request, HttpContext http, UpdateStudentHandler handler, CancellationToken ct) =>
            {
                var command = new UpdateStudentCommand(
                    studentId, request.DisplayName, request.Email, request.PhoneNumber, request.DateOfBirth,
                    http.User.GetUserId(), http.User.GetPrimaryRole());
                return Results.Ok(await handler.HandleAsync(command, ct));
            })
            .RequireAuthorization($"permission:{RbacCatalog.PermissionCodes.StudentManage}")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("UpdateStudent")
            .WithDescription("Correct a student's name, e-mail address, mobile number and date of birth; a new e-mail address or number ends their sessions; audited");

        // A POST and not a DELETE: nothing is removed. The account stays, closed to sign-in, with its results and history.
        students.MapPost("/{studentId:guid}/deactivate", async (
                Guid studentId, HttpContext http, DeactivateStudentHandler handler, CancellationToken ct) =>
            {
                var command = new DeactivateStudentCommand(studentId, http.User.GetUserId(), http.User.GetPrimaryRole());
                return Results.Ok(await handler.HandleAsync(command, ct));
            })
            .RequireAuthorization($"permission:{RbacCatalog.PermissionCodes.StudentManage}")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("DeactivateStudent")
            .WithDescription("Deactivate a student's account: they can no longer sign in and are signed out; their results and records are kept; audited");
    }
}
