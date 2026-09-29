using ExamPlatform.SharedKernel.Application;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.SharedKernel.Infrastructure;

/// <summary>Registers the cross-cutting services every module relies on.</summary>
public static class SharedKernelServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="Clock"/>, <see cref="IDomainEventDispatcher"/>, and
    /// <see cref="DomainEventsSaveChangesInterceptor"/>. Called once by the Host,
    /// before any module's <c>AddModule</c>, so every module resolves the same
    /// singleton clock and dispatcher instead of each registering its own.
    /// </summary>
    /// <param name="services">The application's service collection.</param>
    public static IServiceCollection AddSharedKernel(this IServiceCollection services)
    {
        services.AddSingleton<Clock, SystemClock>();
        services.AddScoped<IDomainEventDispatcher, InProcessDomainEventDispatcher>();
        services.AddScoped<DomainEventsSaveChangesInterceptor>();
        return services;
    }
}
