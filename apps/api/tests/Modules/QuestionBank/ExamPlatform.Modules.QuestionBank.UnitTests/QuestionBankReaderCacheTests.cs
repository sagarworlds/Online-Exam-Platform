using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Infrastructure;
using ExamPlatform.SharedKernel.Domain;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>
/// What the reader keeps of a stored question version, and what it never does: a stored version cannot change, so its content is kept;
/// where a question is filed, whether it can be used, and whether it is still there are read every time.
/// </summary>
public class QuestionBankReaderCacheTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc);

    private readonly IQuestionRepository _questions = Substitute.For<IQuestionRepository>();
    private readonly IBookRepository _books = Substitute.For<IBookRepository>();
    private readonly MemoryQuestionVersionCache _cache = new(Options.Create(new QuestionVersionCacheOptions()));
    private readonly Question _question;

    public QuestionBankReaderCacheTests()
    {
        _question = Question.Create("<p>Old wording</p>", [new NewQuestionOption("A", true), new NewQuestionOption("B", false)], Guid.NewGuid(), Now);
        var options = _question.Options.OrderBy(o => o.Order).Select(o => new QuestionOptionEdit(o.Id, o.Text, o.IsCorrect)).ToList();
        _question.Revise("<p>New wording</p>", options, answered: false, allowsMultiple: false, Now.AddMinutes(1));

        Facts(chapterId: null, status: QuestionStatus.Approved, current: 2);
        StoreVersionsOf(_question);
        _books.GetChapterRefsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, ChapterRef>());
    }

    private QuestionBankReader Reader(bool requireApproval = false) =>
        new(_questions, _books, new QuestionApprovalPolicy(requireApproval), _cache);

    private void Facts(Guid? chapterId, QuestionStatus status, int? current) =>
        _questions.GetFactsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new QuestionFacts(_question.Id, chapterId, status, current)]);

    private void StoreVersionsOf(Question question) =>
        _questions.GetVersionsAsync(Arg.Any<IReadOnlyCollection<(Guid, int)>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<(Guid QuestionId, int VersionNumber)>>()
                .SelectMany(w => question.Versions.Where(v => v.QuestionId == w.QuestionId && v.VersionNumber == w.VersionNumber)).ToList());

    [Fact]
    public async Task ASecondRead_OfTheSameVersion_IsServedFromTheCache_WithTheSameContent()
    {
        var reader = Reader();
        var first = Assert.Single(await reader.GetVersionsAsync([new QuestionVersionRef(_question.Id, 2)], CancellationToken.None));
        var second = Assert.Single(await reader.GetVersionsAsync([new QuestionVersionRef(_question.Id, 2)], CancellationToken.None));

        await _questions.ReceivedWithAnyArgs(1).GetVersionsAsync(default!, default);
        Assert.Equal(first.Text, second.Text);
        Assert.Equal(first.Options, second.Options);
        Assert.Equal(first.VersionNumber, second.VersionNumber);
        Assert.Contains("New wording", second.Text);
        Assert.Equal([true, false], second.Options.Select(o => o.IsCorrect));
    }

    [Fact]
    public async Task ANewerVersion_IsAnotherEntry_SoAnEditIsNeverServedStale()
    {
        var reader = Reader();
        var before = Assert.Single(await reader.GetVersionsAsync([new QuestionVersionRef(_question.Id, null)], CancellationToken.None));
        Assert.Contains("New wording", before.Text);

        // The question is revised again: a third version, and the version in force moves on.
        var options = _question.Options.OrderBy(o => o.Order).Select(o => new QuestionOptionEdit(o.Id, o.Text, o.IsCorrect)).ToList();
        _question.Revise("<p>Newest wording</p>", options, answered: false, allowsMultiple: false, Now.AddMinutes(2));
        Facts(chapterId: null, status: QuestionStatus.Approved, current: 3);

        var after = Assert.Single(await reader.GetVersionsAsync([new QuestionVersionRef(_question.Id, null)], CancellationToken.None));
        var attemptStillOnTwo = Assert.Single(await reader.GetVersionsAsync([new QuestionVersionRef(_question.Id, 2)], CancellationToken.None));

        Assert.Contains("Newest wording", after.Text);
        Assert.Equal(3, after.VersionNumber);
        Assert.Contains("New wording", attemptStillOnTwo.Text);
        Assert.DoesNotContain("Newest", attemptStillOnTwo.Text);
        await _questions.ReceivedWithAnyArgs(2).GetVersionsAsync(default!, default); // versions 2 and 3, each read once
    }

    [Fact]
    public async Task WhereAQuestionIsFiled_IsReadEveryTime_EvenWhenItsContentIsKept()
    {
        var firstChapter = Guid.NewGuid();
        var secondChapter = Guid.NewGuid();
        var firstBook = Guid.NewGuid();
        var secondBook = Guid.NewGuid();
        _books.GetChapterRefsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, ChapterRef>
        {
            [firstChapter] = new(firstChapter, "One", firstBook, "Book one"),
            [secondChapter] = new(secondChapter, "Two", secondBook, "Book two"),
        });
        var reader = Reader();

        Facts(firstChapter, QuestionStatus.Approved, current: 2);
        var first = Assert.Single(await reader.GetVersionsAsync([new QuestionVersionRef(_question.Id, 2)], CancellationToken.None));
        Facts(secondChapter, QuestionStatus.Approved, current: 2);
        var second = Assert.Single(await reader.GetVersionsAsync([new QuestionVersionRef(_question.Id, 2)], CancellationToken.None));

        Assert.Equal((firstChapter, firstBook), (first.ChapterId, first.BookId));
        Assert.Equal((secondChapter, secondBook), (second.ChapterId, second.BookId));
        await _questions.ReceivedWithAnyArgs(1).GetVersionsAsync(default!, default);
    }

    [Fact]
    public async Task AQuestionThatIsGone_IsNotServedFromTheCache()
    {
        var reader = Reader();
        Assert.Single(await reader.GetVersionsAsync([new QuestionVersionRef(_question.Id, 2)], CancellationToken.None));

        _questions.GetFactsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);

        Assert.Empty(await reader.GetVersionsAsync([new QuestionVersionRef(_question.Id, 2)], CancellationToken.None));
    }

    [Fact]
    public async Task WhetherAQuestionCanBeUsed_IsAboutTheQuestionAsItIsNow_AndOnlySaidOfTheVersionInForce()
    {
        var reader = Reader(requireApproval: true);
        Facts(chapterId: null, QuestionStatus.Draft, current: 2);

        var inForce = Assert.Single(await reader.GetVersionsAsync([new QuestionVersionRef(_question.Id, 2)], CancellationToken.None));
        var older = Assert.Single(await reader.GetVersionsAsync([new QuestionVersionRef(_question.Id, 1)], CancellationToken.None));

        Assert.NotNull(inForce.UnusableReason);
        Assert.Null(older.UnusableReason);

        // Approved a moment later: the version that was cached is now usable, with no new read of its content.
        Facts(chapterId: null, QuestionStatus.Approved, current: 2);
        var approved = Assert.Single(await reader.GetVersionsAsync([new QuestionVersionRef(_question.Id, 2)], CancellationToken.None));
        Assert.Null(approved.UnusableReason);
    }

    [Fact]
    public async Task AVersionWithNoStoredRow_ReadsAsTheQuestionIsNow_AndIsNotKept()
    {
        _questions.GetVersionsAsync(Arg.Any<IReadOnlyCollection<(Guid, int)>>(), Arg.Any<CancellationToken>()).Returns([]);
        _questions.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([_question]);
        var reader = Reader();

        var first = Assert.Single(await reader.GetVersionsAsync([new QuestionVersionRef(_question.Id, 2)], CancellationToken.None));
        var second = Assert.Single(await reader.GetVersionsAsync([new QuestionVersionRef(_question.Id, 2)], CancellationToken.None));

        Assert.Contains("New wording", first.Text);
        Assert.Contains("New wording", second.Text);
        // Nothing was stored to keep, so each read asks again rather than caching what the question says now under a version number.
        await _questions.ReceivedWithAnyArgs(2).GetVersionsAsync(default!, default);
    }

    [Fact]
    public async Task ATextQuestion_KeepsItsAcceptedAnswers_Rule_AndExplanation()
    {
        var text = Question.Create(
            "<p>Capital of France?</p>", [], Guid.NewGuid(), Now, isTextAnswer: true, acceptedAnswers: ["Paris", "City of Paris"],
            explanation: "It has been the capital for centuries.", matchRule: TextMatchRule.Lenient);
        _questions.GetFactsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new QuestionFacts(text.Id, null, text.Status, 1)]);
        StoreVersionsOf(text);
        var reader = Reader();

        await reader.GetVersionsAsync([new QuestionVersionRef(text.Id, 1)], CancellationToken.None);
        var kept = Assert.Single(await reader.GetVersionsAsync([new QuestionVersionRef(text.Id, 1)], CancellationToken.None));

        await _questions.ReceivedWithAnyArgs(1).GetVersionsAsync(default!, default);
        Assert.True(kept.IsTextAnswer);
        Assert.Equal(["Paris", "City of Paris"], kept.AcceptedAnswers);
        Assert.Equal(TextMatchRule.Lenient, kept.MatchRule);
        Assert.Equal("It has been the capital for centuries.", kept.Explanation);
        Assert.Empty(kept.Options);
    }

    [Fact]
    public async Task WhatTheCacheHandsOut_CannotBeEditedByAReader_SoOneCallerCannotChangeItForTheNext()
    {
        var reader = Reader();
        var chosen = Assert.Single(await reader.GetVersionsAsync([new QuestionVersionRef(_question.Id, 2)], CancellationToken.None));

        var text = Question.Create(
            "<p>Capital of France?</p>", [], Guid.NewGuid(), Now, isTextAnswer: true, acceptedAnswers: ["Paris"]);
        _questions.GetFactsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new QuestionFacts(text.Id, null, text.Status, 1)]);
        StoreVersionsOf(text);
        var typed = Assert.Single(await reader.GetVersionsAsync([new QuestionVersionRef(text.Id, 1)], CancellationToken.None));

        // A reader that cast the answers or the options back to a list or an array and edited them would be editing the cached copy.
        Assert.Throws<NotSupportedException>(() => ((IList<string>)typed.AcceptedAnswers!)[0] = "Lyon");
        Assert.Throws<NotSupportedException>(() => ((IList<QuestionOptionSnapshot>)chosen.Options).Clear());
        Assert.IsNotType<List<QuestionOptionSnapshot>>(chosen.Options);
        Assert.IsNotType<string[]>(typed.AcceptedAnswers);
    }

    [Fact]
    public async Task SeveralQuestions_AreReadTogether_AndOnlyTheOnesNotKeptAreLookedUp()
    {
        var other = Question.Create("<p>Other</p>", [new NewQuestionOption("X", true), new NewQuestionOption("Y", false)], Guid.NewGuid(), Now);
        _questions.GetFactsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new QuestionFacts(_question.Id, null, _question.Status, 2), new QuestionFacts(other.Id, null, other.Status, 1)]);
        _questions.GetVersionsAsync(Arg.Any<IReadOnlyCollection<(Guid, int)>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<(Guid QuestionId, int VersionNumber)>>()
                .SelectMany(w => new[] { _question, other }.SelectMany(q => q.Versions).Where(v => v.QuestionId == w.QuestionId && v.VersionNumber == w.VersionNumber)).ToList());
        var reader = Reader();

        await reader.GetVersionsAsync([new QuestionVersionRef(_question.Id, 2)], CancellationToken.None);
        var both = await reader.GetVersionsAsync([new QuestionVersionRef(_question.Id, 2), new QuestionVersionRef(other.Id, 1)], CancellationToken.None);

        Assert.Equal(2, both.Count);
        var lastLookup = _questions.ReceivedCalls().Last(c => c.GetMethodInfo().Name == nameof(IQuestionRepository.GetVersionsAsync));
        var asked = Assert.IsAssignableFrom<IReadOnlyCollection<(Guid QuestionId, int VersionNumber)>>(lastLookup.GetArguments()[0]);
        Assert.Equal([(other.Id, 1)], asked);
    }
}

/// <summary>The in-memory cache of question versions, and the settings that bound it.</summary>
public class MemoryQuestionVersionCacheTests
{
    private static QuestionVersionContent Content(string text = "<p>Question</p>") =>
        new(text, [new QuestionOptionSnapshot(Guid.NewGuid(), "A", true), new QuestionOptionSnapshot(Guid.NewGuid(), "B", false)],
            AllowsMultiple: false, IsTextAnswer: false, AcceptedAnswers: [], Explanation: null, MatchRule: TextMatchRule.Exact);

    private static MemoryQuestionVersionCache Cache(long maxBytes = 1024 * 1024) =>
        new(Options.Create(new QuestionVersionCacheOptions { MaxBytes = maxBytes }));

    [Fact]
    public void WhatIsSet_IsFound_UnderItsQuestionAndVersionOnly()
    {
        using var cache = Cache();
        var id = Guid.NewGuid();
        var content = Content();

        cache.Set(id, 2, content);

        Assert.True(cache.TryGet(id, 2, out var found));
        Assert.Same(content, found);
        Assert.False(cache.TryGet(id, 3, out _));
        Assert.False(cache.TryGet(Guid.NewGuid(), 2, out _));
    }

    [Fact]
    public void ContentLargerThanTheWholeCache_IsNotKept()
    {
        using var cache = Cache(maxBytes: 1000);
        var id = Guid.NewGuid();

        cache.Set(id, 1, Content(new string('x', 5000)));

        Assert.False(cache.TryGet(id, 1, out _));
    }

    [Fact]
    public void ACacheThatIsFull_NeverHoldsMoreThanItsSize_AndStillWorks()
    {
        using var cache = Cache(maxBytes: 4000);
        var ids = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToList();

        foreach (var id in ids)
            cache.Set(id, 1, Content(new string('y', 300)));

        var kept = ids.Count(id => cache.TryGet(id, 1, out _));
        Assert.InRange(kept, 0, 8); // 4000 bytes at 700 or so each
    }

    [Fact]
    public void AnImageHeavyQuestion_CountsForFarMoreThanAPlainOne()
    {
        var plain = Content().ApproximateBytes;
        var heavy = Content("<p><img src=\"data:image/png;base64," + new string('A', 400_000) + "\"></p>").ApproximateBytes;

        Assert.True(heavy > 100 * plain);
    }

    [Theory]
    [InlineData(0, 30, 360, true)]
    [InlineData(64L * 1024 * 1024, 30, 360, true)]
    [InlineData(-1, 30, 360, false)]
    [InlineData(1024, 0, 360, false)]
    [InlineData(1024, 30, 0, false)]
    public void TheSettings_AreValidWhenSizeIsNotNegative_AndBothTimesArePositive(long maxBytes, int sliding, int absolute, bool valid) =>
        Assert.Equal(valid, new QuestionVersionCacheOptions { MaxBytes = maxBytes, SlidingMinutes = sliding, AbsoluteMinutes = absolute }.IsValid);

    [Fact]
    public void TheDefaults_AreOneThirtyMinuteIdleLimit_AndASixHourCeiling()
    {
        var defaults = new QuestionVersionCacheOptions();

        Assert.True(defaults.IsValid);
        Assert.Equal(30, defaults.SlidingMinutes);
        Assert.Equal(360, defaults.AbsoluteMinutes);
    }

    [Fact]
    public void ACacheThatHoldsNothing_NeverFindsAnything()
    {
        var cache = NoQuestionVersionCache.Instance;
        var id = Guid.NewGuid();

        cache.Set(id, 1, Content());

        Assert.False(cache.TryGet(id, 1, out _));
    }
}
