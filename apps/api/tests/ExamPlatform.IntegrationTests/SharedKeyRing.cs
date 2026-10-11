using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// One Data Protection provider for every test host in the process, and it is never disposed. Production keeps its keys in the database
/// (<c>PersistKeysToDbContext</c>); a test host keeps them in memory, so a test host needs no database for its keys.
/// </summary>
/// <remarks>
/// Why one provider, and why it outlives the hosts: EF Core caches the QuestionBank model for the whole process, and that model's value
/// converters keep the content cipher of the host that built it first. When a Data Protection key ring is refreshed, it reads its keys
/// through the services of the host that created it. A cipher from a disposed host therefore fails with a disposed
/// <c>IServiceProvider</c> the next time a later test writes a question. A provider that lives for the whole process has nothing to
/// dispose, so every cipher keeps working. Every test host must use this provider, including any host that migrates the database, since
/// the first host to build the model is the one whose cipher is kept.
/// </remarks>
internal static class SharedKeyRing
{
    // Declared before the services, which read it while they are built.
    private static readonly MemoryKeyRepository Repository = new();

    // Held for the life of the process and never disposed; that is the point of this class.
    private static readonly ServiceProvider Services = BuildServices();

    /// <summary>The Data Protection provider every test host uses.</summary>
    public static IDataProtectionProvider Provider { get; } = Services.GetRequiredService<IDataProtectionProvider>();

    /// <summary>Gives a test host the shared provider in place of its own. Call it from the host's <c>ConfigureTestServices</c>.</summary>
    /// <param name="services">The host's services. The shared provider is registered last, so it replaces the host's own.</param>
    public static void Apply(IServiceCollection services) => services.AddSingleton(Provider);

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddDataProtection().SetApplicationName("ExamPlatform");
        services.PostConfigure<KeyManagementOptions>(options => options.XmlRepository = Repository);
        var provider = services.BuildServiceProvider();

        // The first key is created here, before any host runs: two hosts that each created one at the same moment would hold
        // different default keys.
        provider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("ExamPlatform.IntegrationTests.KeyRingWarmUp")
            .Protect("warm-up");
        return provider;
    }

    private sealed class MemoryKeyRepository : IXmlRepository
    {
        private readonly object _gate = new();
        private readonly List<XElement> _elements = [];

        public IReadOnlyCollection<XElement> GetAllElements()
        {
            lock (_gate)
            {
                return _elements.Select(element => new XElement(element)).ToArray();
            }
        }

        public void StoreElement(XElement element, string friendlyName)
        {
            lock (_gate)
            {
                _elements.Add(new XElement(element));
            }
        }
    }
}
