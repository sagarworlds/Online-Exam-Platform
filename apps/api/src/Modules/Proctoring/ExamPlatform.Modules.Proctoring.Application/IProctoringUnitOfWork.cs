using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Proctoring.Application;

/// <summary>
/// The Proctoring module's unit of work. Its own interface, as in every module (see <c>IGuardianUnitOfWork</c>), so DI cannot resolve
/// another module's <c>DbContext</c>-backed implementation.
/// </summary>
public interface IProctoringUnitOfWork : IUnitOfWork;
