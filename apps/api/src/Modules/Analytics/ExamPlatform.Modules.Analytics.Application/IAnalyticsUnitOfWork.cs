using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Analytics.Application;

/// <summary>
/// The Analytics module's unit of work. A distinct interface per module (see <c>IAdminUnitOfWork</c> for the rationale) so DI cannot
/// resolve another module's <c>DbContext</c>-backed implementation.
/// </summary>
public interface IAnalyticsUnitOfWork : IUnitOfWork;
