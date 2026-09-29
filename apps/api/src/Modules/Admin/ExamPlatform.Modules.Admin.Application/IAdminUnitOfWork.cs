using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Admin.Application;

/// <summary>
/// The Admin module's unit of work. A distinct interface per module (see
/// <c>Identity.Application.IIdentityUnitOfWork</c> for the rationale) so DI
/// cannot resolve another module's <c>DbContext</c>-backed implementation.
/// </summary>
public interface IAdminUnitOfWork : IUnitOfWork;
