using System.Diagnostics;

namespace ExamPlatform.IntegrationTests;

/// <summary>What a Host job printed and the exit code it ended with.</summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="Output">Standard output and standard error together.</param>
public sealed record JobRun(int ExitCode, string Output);

/// <summary>
/// Runs the real Host binary as a deployment job would (ADR 0002, ADR 0005), as a separate process, so a test covers the
/// command-line flag, the return before the web host starts, and the exit code. Only the connection string is set; the rest
/// is the Production default.
/// </summary>
public static class HostJob
{
    /// <summary>Runs the Host with the given arguments against a database and waits for it to end.</summary>
    /// <param name="arguments">The command-line arguments, such as <c>--migrate-and-seed</c>.</param>
    /// <param name="connectionString">The Npgsql connection string of the database to run against.</param>
    /// <param name="extraEnvironment">Environment variables to add, as a deployment would set them for the job.</param>
    /// <exception cref="TimeoutException">The process did not end within two minutes: it must return before <c>app.Run()</c>.</exception>
    public static async Task<JobRun> RunAsync(
        IReadOnlyList<string> arguments, string connectionString, IReadOnlyDictionary<string, string>? extraEnvironment = null)
    {
        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "ExamPlatform.Api.dll"));
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Production";
        startInfo.Environment["ConnectionStrings__Postgres"] = connectionString;
        foreach (var (key, value) in extraEnvironment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[key] = value;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The Host process could not be started.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"The Host did not exit after {string.Join(' ', arguments)}; it must return before app.Run().");
        }

        return new JobRun(process.ExitCode, await stdout + await stderr);
    }
}
