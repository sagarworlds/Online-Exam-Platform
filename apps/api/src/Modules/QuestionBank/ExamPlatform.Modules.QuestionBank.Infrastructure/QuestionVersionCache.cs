using System.Diagnostics.CodeAnalysis;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.QuestionBank.Infrastructure;

/// <summary>
/// An in-memory <see cref="IQuestionVersionCache"/> limited by size. When it is full, a version that does not fit is simply not kept and
/// is read from the database again next time; nothing is ever wrong, only slower.
/// </summary>
public sealed class MemoryQuestionVersionCache : IQuestionVersionCache, IDisposable
{
    private readonly MemoryCache _cache;
    private readonly long _maxBytes;
    private readonly TimeSpan _sliding;
    private readonly TimeSpan _absolute;

    /// <summary>Makes a cache of the size the settings say.</summary>
    /// <param name="options">The settings.</param>
    public MemoryQuestionVersionCache(IOptions<QuestionVersionCacheOptions> options)
    {
        _maxBytes = options.Value.MaxBytes;
        _sliding = TimeSpan.FromMinutes(options.Value.SlidingMinutes);
        _absolute = TimeSpan.FromMinutes(options.Value.AbsoluteMinutes);
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = _maxBytes });
    }

    /// <inheritdoc />
    public bool TryGet(Guid questionId, int versionNumber, [NotNullWhen(true)] out QuestionVersionContent? content) =>
        _cache.TryGetValue((questionId, versionNumber), out content);

    /// <inheritdoc />
    public void Set(Guid questionId, int versionNumber, QuestionVersionContent content)
    {
        var size = content.ApproximateBytes;
        if (size > _maxBytes)
            return; // Larger than the whole cache: keeping it would push everything else out.

        _cache.Set(
            (questionId, versionNumber),
            content,
            new MemoryCacheEntryOptions { Size = size, SlidingExpiration = _sliding, AbsoluteExpirationRelativeToNow = _absolute });
    }

    /// <inheritdoc />
    public void Dispose() => _cache.Dispose();
}
