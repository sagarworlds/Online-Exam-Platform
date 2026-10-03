using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    /// <summary>
    /// Registers the <see cref="IMailSender"/> every module's notifier uses. The caller binds <see cref="SmtpOptions"/> to the
    /// <c>Smtp</c> configuration section, which needs the configuration system this assembly does not reference.
    /// </summary>
    /// <param name="services">The application's service collection.</param>
    public static IServiceCollection AddSmtpMailer(this IServiceCollection services)
    {
        services.TryAddScoped<IMailSender, SmtpMailSender>();
        return services;
    }
}
