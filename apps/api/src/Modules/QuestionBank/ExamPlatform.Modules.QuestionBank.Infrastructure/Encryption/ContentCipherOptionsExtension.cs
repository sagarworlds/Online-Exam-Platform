using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Encryption;

/// <summary>
/// Ties a context's internal service provider to the cipher it was given. EF Core caches models and compiled queries per service provider,
/// and the content converters are built from the cipher, so a provider shared by two cipher instances would decrypt one host's content with
/// another host's keys. Two cipher instances therefore never share a provider, while contexts that share one cipher still do.
/// </summary>
public sealed class ContentCipherOptionsExtension : IDbContextOptionsExtension
{
    /// <summary>The extension for a context built with <paramref name="cipher"/>.</summary>
    /// <param name="cipher">The cipher of the host that owns the context.</param>
    public ContentCipherOptionsExtension(QuestionContentCipher cipher)
    {
        Cipher = cipher;
        Info = new ExtensionInfo(this);
    }

    /// <summary>The cipher this extension was given.</summary>
    public QuestionContentCipher Cipher { get; }

    /// <inheritdoc />
    public DbContextOptionsExtensionInfo Info { get; }

    /// <inheritdoc />
    public void ApplyServices(IServiceCollection services)
    {
        // Nothing to register: the cipher reaches the context through its constructor, and this extension only tells EF which provider to use.
    }

    /// <inheritdoc />
    public void Validate(IDbContextOptions options)
    {
        // Nothing to check: the extension holds no setting that could be invalid.
    }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        private readonly ContentCipherOptionsExtension _extension;

        public ExtensionInfo(ContentCipherOptionsExtension extension)
            : base(extension)
        {
            _extension = extension;
        }

        /// <inheritdoc />
        public override bool IsDatabaseProvider => false;

        /// <inheritdoc />
        public override string LogFragment => "ContentCipher ";

        /// <inheritdoc />
        public override int GetServiceProviderHashCode() => RuntimeHelpers.GetHashCode(_extension.Cipher);

        /// <inheritdoc />
        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo info && ReferenceEquals(info._extension.Cipher, _extension.Cipher);

        /// <inheritdoc />
        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["QuestionBank:ContentCipher"] = RuntimeHelpers.GetHashCode(_extension.Cipher).ToString(CultureInfo.InvariantCulture);
    }
}
