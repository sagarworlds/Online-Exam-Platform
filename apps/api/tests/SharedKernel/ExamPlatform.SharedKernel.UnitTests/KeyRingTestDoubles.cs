using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>
/// An <see cref="IXmlRepository"/> that keeps its entries in memory and counts the writes, standing in for the database table of the key
/// ring. A write count that does not move proves Data Protection did not make a new key.
/// </summary>
internal sealed class MemoryKeyRepository : IXmlRepository
{
    private readonly List<XElement> _elements = [];

    /// <summary>How many times an entry was stored.</summary>
    public int StoreCount { get; private set; }

    /// <summary>The entries as they are stored: what a copy of the database would hold.</summary>
    public IReadOnlyList<XElement> Stored => _elements;

    /// <inheritdoc />
    public IReadOnlyCollection<XElement> GetAllElements() => _elements.Select(element => new XElement(element)).ToArray();

    /// <inheritdoc />
    public void StoreElement(XElement element, string friendlyName)
    {
        StoreCount++;
        _elements.Add(new XElement(element));
    }

    /// <summary>Replaces the stored entries, as the re-protection step does when it rewrites rows in place. It is not a store: the count is unchanged.</summary>
    /// <param name="elements">The entries to hold from now on.</param>
    public void ReplaceAll(IEnumerable<XElement> elements)
    {
        var replacement = elements.ToList();
        _elements.Clear();
        _elements.AddRange(replacement);
    }
}

/// <summary>An <see cref="IHostEnvironment"/> with a chosen name, for the startup check that treats Development differently.</summary>
/// <param name="name">The environment name.</param>
internal sealed class FakeHostEnvironment(string name) : IHostEnvironment
{
    /// <inheritdoc />
    public string EnvironmentName { get; set; } = name;

    /// <inheritdoc />
    public string ApplicationName { get; set; } = "tests";

    /// <inheritdoc />
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    /// <inheritdoc />
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
