using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure.Email;
using ExamPlatform.SharedKernel.Infrastructure.Sms;
using ExamPlatform.SharedKernel.Infrastructure.WhatsApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    /// Registers every <see cref="IDomainEventHandler{TEvent}"/> in an assembly, once per event type it handles, so a
    /// module adds a reaction by writing the class and nothing else (Open/Closed).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assembly">The assembly to scan, normally the module's Application assembly.</param>
    public static IServiceCollection AddDomainEventHandlers(this IServiceCollection services, System.Reflection.Assembly assembly)
    {
        foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
        {
            foreach (var handled in type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>)))
            {
                services.AddScoped(handled, type);
            }
        }

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

    /// <summary>
    /// Registers the <see cref="IMailSender"/> that sends through Brevo's HTTPS API, for a host that blocks outbound
    /// SMTP. The caller binds <see cref="BrevoOptions"/> to the <c>Brevo</c> configuration section, which needs the
    /// configuration system this assembly does not reference.
    /// </summary>
    /// <param name="services">The application's service collection.</param>
    public static IServiceCollection AddBrevoApiMailer(this IServiceCollection services)
    {
        // A single shared HttpClient, not IHttpClientFactory: this call is infrequent (one OTP or notification at a
        // time), so pooled handler rotation and the extra package it needs would add nothing here.
        services.TryAddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(15) });
        services.TryAddScoped<IMailSender, BrevoApiMailSender>();
        return services;
    }

    /// <summary>
    /// Registers the <see cref="IWhatsAppSender"/> that sends through Meta's WhatsApp Cloud API. The caller binds
    /// <see cref="WhatsAppOptions"/> to the <c>WhatsApp</c> configuration section, which needs the configuration system
    /// this assembly does not reference. With nothing configured it sends nothing and says so.
    /// </summary>
    /// <param name="services">The application's service collection.</param>
    public static IServiceCollection AddWhatsAppCloudApi(this IServiceCollection services)
    {
        // Its own HttpClient (not the one the Brevo mailer shares), so the two keep their own timeouts.
        services.TryAddSingleton<IWhatsAppSender>(sp => new WhatsAppCloudApiSender(
            new HttpClient { Timeout = TimeSpan.FromSeconds(15) },
            sp.GetRequiredService<IOptions<WhatsAppOptions>>(),
            sp.GetRequiredService<ILogger<WhatsAppCloudApiSender>>()));

        // What the webhook reports about recent messages, for the administrator's WhatsApp test (a diagnostic aid, kept in memory).
        services.TryAddSingleton<IWhatsAppDeliveryTracker>(sp => new InMemoryWhatsAppDeliveryTracker(sp.GetRequiredService<Clock>()));
        return services;
    }

    /// <summary>
    /// Registers the <see cref="ISmsSender"/> every module uses, behind the <c>Sms:Enabled</c> master switch. The caller binds
    /// <see cref="SmsOptions"/> to the <c>Sms</c> configuration section, which needs the configuration system this assembly does not
    /// reference. No SMS provider is built in yet, so even with the switch on nothing is sent, and each attempt is logged.
    /// </summary>
    /// <param name="services">The application's service collection.</param>
    public static IServiceCollection AddSmsSender(this IServiceCollection services)
    {
        services.TryAddSingleton<ISmsProvider, UnconfiguredSmsProvider>();
        services.TryAddSingleton<ISmsSender, SwitchedSmsSender>();
        return services;
    }
}
