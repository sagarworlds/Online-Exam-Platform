using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Application.LoadTest;

namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>Creates the load test's pool of candidate accounts.</summary>
/// <param name="Pattern">The addresses and how many: the pool's accounts are numbered 1 to <see cref="LoadTestAccountPattern.Count"/>.</param>
/// <param name="Password">The password every account in the pool shares.</param>
public sealed record ProvisionLoadTestCandidatesCommand(LoadTestAccountPattern Pattern, string? Password);

/// <summary>What <see cref="ProvisionLoadTestCandidatesHandler"/> did.</summary>
/// <param name="Created">The accounts that were created.</param>
/// <param name="AlreadyExisted">The accounts that already existed and were left exactly as they were.</param>
public sealed record ProvisionLoadTestCandidatesResult(int Created, int AlreadyExisted);

/// <summary>
/// Handles <see cref="ProvisionLoadTestCandidatesCommand"/> (ADR 0006). The load test (NFR-1, NFR-2) needs one candidate account for
/// each virtual user, all with the same password and none needing a second factor, and registration e-mails a code to each address, so
/// the web form cannot make thousands of them. These accounts are made the way a registered, verified candidate is, so the load
/// test signs in and starts attempts through the code real candidates use.
/// </summary>
/// <remarks>
/// The addresses are on the reserved <c>.invalid</c> domain (<see cref="LoadTestAccountPattern"/>), so no pool account is a real
/// person. Running it again creates only what is missing and never changes an account that exists.
/// </remarks>
public sealed class ProvisionLoadTestCandidatesHandler(
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IPasswordHasher passwordHasher,
    IPasswordPolicy passwordPolicy,
    IAuditLogger auditLogger,
    IIdentityUnitOfWork unitOfWork,
    Clock clock)
{
    /// <summary>How many accounts are looked up and stored together. It keeps one save small and its round trips few.</summary>
    public const int BatchSize = 500;

    /// <summary>The name every pool account shows.</summary>
    public const string DisplayName = "Load test candidate";

    /// <summary>An adult, so no age rule treats a pool account as a minor.</summary>
    public static readonly DateOnly AdultDateOfBirth = new(1990, 1, 1);

    /// <summary>Creates the accounts that do not exist yet, in batches, and records the run in the audit trail.</summary>
    /// <param name="command">The pool and its shared password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many accounts were created and how many already existed.</returns>
    /// <exception cref="InvalidLoadTestAccountsError">There is no shared password.</exception>
    /// <exception cref="WeakPasswordError">The shared password does not meet the password policy.</exception>
    /// <exception cref="RoleNotFoundError">The Candidate role has not been seeded; the database needs <c>--migrate-and-seed</c> first.</exception>
    public async Task<ProvisionLoadTestCandidatesResult> HandleAsync(ProvisionLoadTestCandidatesCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Password))
        {
            throw new InvalidLoadTestAccountsError("The shared password is missing. Set LoadTest__Password for this run.");
        }

        passwordPolicy.EnsureAcceptable(command.Password, command.Pattern.AddressOf(1));

        var candidateRole = await roleRepository.GetByNameAsync(RegisterCandidateHandler.CandidateRoleName, cancellationToken)
            ?? throw new RoleNotFoundError();

        // Hashed once: the accounts share one password, so they share its hash. Hashing deliberately costs tens of milliseconds, and
        // doing it for every account would take longer than the rest of the run put together.
        var passwordHash = passwordHasher.Hash(command.Password);

        var nowUtc = clock.UtcNow;
        var created = 0;
        var existed = 0;
        foreach (var batch in command.Pattern.Addresses().Chunk(BatchSize))
        {
            var existing = await userRepository.ListExistingEmailsAsync(batch, cancellationToken);
            foreach (var email in batch)
            {
                if (existing.Contains(email))
                {
                    existed++;
                    continue;
                }

                var candidate = User.Register(email, phoneNumber: null, AdultDateOfBirth, DisplayName, nowUtc);
                candidate.AssignRole(candidateRole);
                candidate.SetPasswordHash(passwordHash);
                candidate.Activate();

                // Registration raises an event. Nothing handles it today, but anything added later (an audit entry, a welcome
                // notice) would then run for thousands of synthetic accounts, and a notice would go to addresses that cannot exist.
                // The run is recorded once, below.
                candidate.ClearDomainEvents();

                await userRepository.AddAsync(candidate, cancellationToken);
                created++;
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        if (created > 0)
        {
            await auditLogger.RecordAsync(
                new AuditEntry(
                    ActorUserId: null,
                    ActorRole: null,
                    Action: "Identity.LoadTestCandidatesCreated",
                    EntityType: "LoadTestPool",
                    EntityId: "load-test-pool",
                    Metadata: new Dictionary<string, string>
                    {
                        ["created"] = created.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["alreadyExisted"] = existed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    },
                    CorrelationId: null),
                cancellationToken);
        }

        return new ProvisionLoadTestCandidatesResult(created, existed);
    }
}
