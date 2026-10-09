using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.SharedKernel.Application;

/// <summary>
/// A validated request for one page of a list. Pages count from 1 and the size is capped, so a caller can
/// neither ask for a page before the first (a negative offset the database rejects as a 500) nor pull a whole
/// table in one request.
/// </summary>
public sealed record PageRequest
{
    /// <summary>The page size used when the caller names none.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>The largest page size a caller may ask for.</summary>
    public const int MaxPageSize = 200;

    /// <summary>
    /// The highest page number served. Capped so that <c>(page - 1) * pageSize</c> always fits an int; a
    /// larger number would wrap to a negative offset the database rejects as a 500.
    /// </summary>
    public const int MaxPage = 1_000_000;

    private PageRequest(int page, int pageSize)
    {
        Page = page;
        PageSize = pageSize;
    }

    /// <summary>The page to read, from 1.</summary>
    public int Page { get; }

    /// <summary>How many rows a page holds, from 1 to <see cref="MaxPageSize"/>.</summary>
    public int PageSize { get; }

    /// <summary>How many rows come before this page.</summary>
    public int Skip => (Page - 1) * PageSize;

    /// <summary>Validates what a caller asked for, filling in the defaults for what they left out.</summary>
    /// <param name="page">The page, or null for the first.</param>
    /// <param name="pageSize">The page size, or null for <see cref="DefaultPageSize"/>.</param>
    /// <exception cref="InvalidPageRequestError">The page is outside 1 to <see cref="MaxPage"/>, or the size is outside 1 to <see cref="MaxPageSize"/>.</exception>
    public static PageRequest Create(int? page, int? pageSize)
    {
        var p = page ?? 1;
        var size = pageSize ?? DefaultPageSize;

        if (p is < 1 or > MaxPage)
            throw new InvalidPageRequestError($"page must be between 1 and {MaxPage}.");
        if (size is < 1 or > MaxPageSize)
            throw new InvalidPageRequestError($"pageSize must be between 1 and {MaxPageSize}.");

        return new PageRequest(p, size);
    }
}
