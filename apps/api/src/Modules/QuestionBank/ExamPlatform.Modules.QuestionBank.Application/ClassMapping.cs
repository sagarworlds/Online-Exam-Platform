using ExamPlatform.Modules.QuestionBank.Application.Dtos;
using ExamPlatform.Modules.QuestionBank.Domain;

namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>Maps <see cref="SchoolClass"/> to its DTO, so every handler reports a class the same way.</summary>
internal static class ClassMapping
{
    /// <summary>Maps a class with the number of books under it.</summary>
    /// <param name="schoolClass">The class to map.</param>
    /// <param name="bookCount">How many books belong to it.</param>
    public static ClassDto ToDto(this SchoolClass schoolClass, int bookCount) =>
        new(schoolClass.Id, schoolClass.Name, schoolClass.IsArchived, bookCount, schoolClass.CreatedAtUtc);
}
