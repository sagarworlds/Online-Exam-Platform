using ExamPlatform.Modules.Identity.Application.Dtos;
using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.Application;

/// <summary>Maps a student account to its DTO, so every handler reports one the same way.</summary>
internal static class StudentMapping
{
    /// <summary>Maps an account.</summary>
    /// <param name="user">The student's account.</param>
    /// <param name="nowUtc">The current instant, for the age band.</param>
    public static StudentDto ToDto(User user, DateTime nowUtc) =>
        new(user.Id, user.DisplayName, user.Email, user.PhoneNumber, user.DateOfBirth, user.GetAgeBand(nowUtc).ToString(), user.Status.ToString());
}
