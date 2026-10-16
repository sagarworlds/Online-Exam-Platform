using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.SharedKernel.Application;

/// <summary>Says which command a command-line flag selects, without building the command.</summary>
/// <param name="Flag">The flag, such as <c>--create-first-administrator</c>.</param>
/// <param name="CommandType">The registered <see cref="IOperatorCommand"/> to resolve when the flag is present.</param>
public sealed record OperatorCommandRegistration(string Flag, Type CommandType);

/// <summary>Registers operator commands (<see cref="IOperatorCommand"/>).</summary>
public static class OperatorCommandServiceCollectionExtensions
{
    /// <summary>
    /// Registers a command and the flag that selects it. The flag is kept apart from the command so the Host can pick a command by
    /// its flag without building any command: building one resolves its dependencies, and a deployment job has only the settings the
    /// command it runs needs (a database job has no mail settings, for example).
    /// </summary>
    /// <typeparam name="TCommand">The command's type.</typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="flag">The command-line flag that selects the command, beginning with <c>--</c>.</param>
    /// <exception cref="ArgumentException">The flag does not begin with <c>--</c>, or another command already uses it.</exception>
    public static IServiceCollection AddOperatorCommand<TCommand>(this IServiceCollection services, string flag)
        where TCommand : class, IOperatorCommand
    {
        if (string.IsNullOrWhiteSpace(flag) || !flag.StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException("An operator command's flag begins with \"--\".", nameof(flag));
        }

        if (services.Any(d => d.ServiceType == typeof(OperatorCommandRegistration)
                              && d.ImplementationInstance is OperatorCommandRegistration existing
                              && string.Equals(existing.Flag, flag, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"Another operator command already uses the flag {flag}.", nameof(flag));
        }

        services.AddScoped<TCommand>();
        services.AddSingleton(new OperatorCommandRegistration(flag, typeof(TCommand)));
        return services;
    }
}
