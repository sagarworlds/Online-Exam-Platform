using ExamPlatform.Modules.Identity.Application.Dtos;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Identity.Application.Queries;

/// <summary>Lists the student accounts for staff, optionally narrowed by a search.</summary>
public sealed class ListStudentsHandler(IUserRepository userRepository)
{
    /// <summary>The most students one listing returns; a search narrows it instead of paging.</summary>
    public const int MaxTake = 200;

    /// <summary>Returns the students whose name, e-mail address or phone number contains the search.</summary>
    /// <param name="search">Part of a name, e-mail address or phone number, without regard to case; null or blank lists everyone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<StudentSummaryDto>> HandleAsync(string? search, CancellationToken cancellationToken)
    {
        var students = await userRepository.ListStudentsAsync(search?.Trim(), MaxTake, cancellationToken);

        return students
            .Select(student => new StudentSummaryDto(student.Id, student.DisplayName, student.Email, student.PhoneNumber, student.Status.ToString()))
            .ToList();
    }
}

/// <summary>
/// Finds which of some e-mail addresses belong to student accounts. The staff list of invitations needs it: an address that was invited
/// and has an account is that student, and one that has none is still waiting to register, and neither can be told from a listing that stops
/// at <see cref="ListStudentsHandler.MaxTake"/> students.
/// </summary>
public sealed class FindStudentsByEmailHandler(IUserRepository userRepository)
{
    /// <summary>The most addresses one lookup takes, as many as a listing of invitations holds.</summary>
    public const int MaxEmails = 200;

    /// <summary>Returns the students holding any of the addresses.</summary>
    /// <param name="emails">The addresses, in any case; blank ones are ignored.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="TooManyAddressesError">More than <see cref="MaxEmails"/> distinct addresses were given.</exception>
    public async Task<IReadOnlyList<StudentSummaryDto>> HandleAsync(IReadOnlyCollection<string?>? emails, CancellationToken cancellationToken)
    {
        var wanted = (emails ?? [])
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Select(email => email!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (wanted.Count == 0)
            return [];

        if (wanted.Count > MaxEmails)
            throw new TooManyAddressesError(MaxEmails);

        var students = await userRepository.ListStudentsByEmailAsync(wanted, cancellationToken);

        return students
            .Select(student => new StudentSummaryDto(student.Id, student.DisplayName, student.Email, student.PhoneNumber, student.Status.ToString()))
            .ToList();
    }
}

/// <summary>Reads one student's profile for staff.</summary>
public sealed class GetStudentHandler(IUserRepository userRepository, Clock clock)
{
    /// <summary>Returns the profile of a student account.</summary>
    /// <param name="studentId">The account's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="UserNotFoundError">There is no such account, or it is not a student's: a staff account is not shown here.</exception>
    public async Task<StudentDto> HandleAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(studentId, cancellationToken);
        if (user is null || !user.IsStudent)
            throw new UserNotFoundError();

        return StudentMapping.ToDto(user, clock.UtcNow);
    }
}
