using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Identity.Application;

/// <summary>
/// The Identity module's unit of work. A distinct interface per module (rather
/// than every module binding the shared <see cref="IUnitOfWork"/> directly) so
/// each module's DI registration cannot accidentally resolve another module's
/// <c>DbContext</c>-backed implementation.
/// </summary>
public interface IIdentityUnitOfWork : IUnitOfWork;
