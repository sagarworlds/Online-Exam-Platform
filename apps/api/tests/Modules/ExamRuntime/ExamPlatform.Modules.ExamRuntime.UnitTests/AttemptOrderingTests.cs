using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Application.Queries;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>The order a candidate sees questions and options in: as authored the first time, shuffled from the second attempt on.</summary>
public class AttemptOrderingTests
{
    private static Guid Id(int n) => new($"00000000-0000-0000-0000-{n:x12}");
    private static List<Guid> Ids(int count) => Enumerable.Range(1, count).Select(Id).ToList();
    private static IReadOnlyList<Guid> Arrange(IReadOnlyList<Guid> authored, Guid attempt, int number, Guid? scope = null) =>
        AttemptOrdering.Arrange(authored, id => id, attempt, number, scope ?? Id(0xb1));

    [Fact]
    public void TheFirstAttempt_ShowsThingsAsTheAuthorWroteThem()
    {
        var authored = Ids(8);

        Assert.Same(authored, Arrange(authored, Guid.NewGuid(), 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void FewerThanTwoItems_HaveNothingToShuffle_OnAnyAttempt(int count)
    {
        var authored = Ids(count);

        Assert.Equal(authored, Arrange(authored, Guid.NewGuid(), 2));
        Assert.Equal(authored, Arrange(authored, Guid.NewGuid(), 5));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public void ALaterAttempt_ShowsTheSameItems_InAnOrderThatIsNeverTheAuthoredOne(int count)
    {
        var authored = Ids(count);

        // Many attempts, so a shuffle that sometimes came out unchanged (a coin flip with two items) would be caught.
        for (var i = 0; i < 300; i++)
        {
            var arranged = Arrange(authored, Guid.NewGuid(), 2 + i % 4);

            Assert.Equal(authored.Order(), arranged.Order());
            Assert.NotEqual(authored, arranged);
        }
    }

    [Fact]
    public void WithTwoItems_ALaterAttemptSwapsThem()
    {
        var authored = Ids(2);

        Assert.Equal([Id(2), Id(1)], Arrange(authored, Guid.NewGuid(), 2));
    }

    [Fact]
    public void TheOrder_IsRepeatable_SoAReloadOrAResumeShowsWhatTheCandidateSawBefore()
    {
        var authored = Ids(8);
        var attempt = Guid.NewGuid();

        Assert.Equal(Arrange(authored, attempt, 2), Arrange(authored, attempt, 2));
        Assert.Equal(Arrange(authored, attempt, 3), Arrange([.. authored], attempt, 3));
    }

    [Fact]
    public void EachAttempt_GetsItsOwnOrder()
    {
        var authored = Ids(8);

        var distinct = Enumerable.Range(0, 20).Select(_ => string.Join(',', Arrange(authored, Guid.NewGuid(), 2))).Distinct().Count();

        // 20 attempts over 8! possible orders: all alike would mean the attempt does not matter.
        Assert.True(distinct >= 15, $"only {distinct} different orders in 20 attempts");
    }

    [Fact]
    public void EachList_IsShuffledIndependently_OfTheOthersInTheSameAttempt()
    {
        var authored = Ids(8);
        var attempt = Id(0xa1);

        Assert.NotEqual(Arrange(authored, attempt, 2, scope: Id(0xb1)), Arrange(authored, attempt, 2, scope: Id(0xb2)));
    }

    [Fact]
    public void TheOrder_IsPinned_SoAFrameworkUpgradeCannotChangeWhatCandidatesAlreadySaw()
    {
        // Worked out separately (SHA-256 over attempt, list and item ids, ordered by the first eight bytes). If this fails, a
        // change has altered the order every candidate on a later attempt sees, including on attempts already made.
        var arranged = Arrange(Ids(6), Id(0xa1), 2, scope: Id(0xb1));

        Assert.Equal(new[] { 2, 4, 3, 5, 6, 1 }.Select(Id), arranged);
    }

    [Fact]
    public void OnlyTheSecondAttemptOnward_IsShuffled()
    {
        Assert.False(AttemptOrdering.IsShuffled(1));
        Assert.True(AttemptOrdering.IsShuffled(2));
        Assert.True(AttemptOrdering.IsShuffled(7));
    }

    [Fact]
    public void TheFirstAttempt_IsShuffledWhenTheAuthorAsks()
    {
        Assert.True(AttemptOrdering.IsShuffled(1, authorShuffles: true));
        Assert.False(AttemptOrdering.IsShuffled(1, authorShuffles: false));
    }

    [Fact]
    public void WhenTheAuthorAsks_TheFirstAttemptIsShuffled_AndDiffersFromTheAuthoredOrder()
    {
        var authored = Ids(8);

        var arranged = AttemptOrdering.Arrange(authored, id => id, Id(0xa1), 1, Id(0xb1), authorShuffles: true);

        Assert.NotEqual(authored, arranged);
        Assert.Equal(authored.OrderBy(id => id), arranged.OrderBy(id => id));
    }

    [Fact]
    public void WhenTheAuthorAsks_TheOrderIsTheSameEveryTimeTheAttemptIsRead()
    {
        var authored = Ids(8);

        Assert.Equal(
            AttemptOrdering.Arrange(authored, id => id, Id(0xa1), 1, Id(0xb1), authorShuffles: true),
            AttemptOrdering.Arrange(authored, id => id, Id(0xa1), 1, Id(0xb1), authorShuffles: true));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void APinnedOption_KeepsItsPlace_WhateverTheAttempt(int number)
    {
        var authored = Ids(5);
        var last = authored[^1];

        for (var attempt = 1; attempt <= 12; attempt++)
        {
            var arranged = AttemptOrdering.Arrange(authored, id => id, Guid.NewGuid(), number, Id(0xb1), authorShuffles: true, isPinned: id => id == last);

            Assert.Equal(last, arranged[^1]);
            Assert.Equal(authored.OrderBy(id => id), arranged.OrderBy(id => id));
        }
    }

    [Fact]
    public void APinnedOptionInTheMiddle_StaysThere_AndTheOthersStillMove()
    {
        var authored = Ids(6);
        var pinned = authored[2];

        var arranged = AttemptOrdering.Arrange(authored, id => id, Id(0xa1), 2, Id(0xb1), isPinned: id => id == pinned);

        Assert.Equal(pinned, arranged[2]);
        Assert.NotEqual(authored, arranged);
    }

    [Fact]
    public void TheUnpinnedOptions_AreNeverLeftInTheirAuthoredOrder_WhenThereAreTwoOrMore()
    {
        var authored = Ids(4);
        var pinned = authored[3];

        var arranged = AttemptOrdering.Arrange(authored, id => id, Id(0xa1), 2, Id(0xb1), isPinned: id => id == pinned);

        Assert.NotEqual(authored.Take(3), arranged.Take(3));
    }

    [Fact]
    public void WithFewerThanTwoMovableOptions_NothingMoves()
    {
        var authored = Ids(3);

        var arranged = AttemptOrdering.Arrange(authored, id => id, Id(0xa1), 2, Id(0xb1), isPinned: id => id != authored[0]);

        Assert.Equal(authored, arranged);
    }

    [Fact]
    public void WithNothingPinned_TheOrderIsTheSameAsWithoutThePinRule()
    {
        var authored = Ids(8);

        Assert.Equal(
            Arrange(authored, Id(0xa1), 2),
            AttemptOrdering.Arrange(authored, id => id, Id(0xa1), 2, Id(0xb1), isPinned: _ => false));
    }

    [Fact]
    public void WhenTheAuthorDoesNotAsk_TheFirstAttemptKeepsTheAuthoredOrder()
    {
        var authored = Ids(8);

        Assert.Same(authored, AttemptOrdering.Arrange(authored, id => id, Id(0xa1), 1, Id(0xb1), authorShuffles: false));
    }
}

/// <summary>What a candidate is shown, and what their review shows, once an attempt is a repeat.</summary>
public class AttemptShuffleHandlerTests
{
    private readonly Guid _candidate = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();
    private readonly List<QuestionSnapshot> _questions = Enumerable.Range(1, 5).Select(i => Fixtures.Question($"Question {i}")).ToList();
    private readonly ExamSnapshot _exam;

    public AttemptShuffleHandlerTests()
    {
        _exam = Fixtures.Exam(_questions);
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<QuestionSnapshot>>(_questions.Where(q => call.Arg<IReadOnlyCollection<Guid>>().Contains(q.Id)).ToList()));
    }

    private AttemptViewBuilder Views => new(_bank, _clock);
    private GetAttemptReviewHandler Review => new(new AttemptAccess(_attempts, _catalog, new AttemptCloser(_bank, _unitOfWork, _clock), _clock), new AttemptReviewBuilder(_bank, _clock));

    private Attempt Open(int number)
    {
        var attempt = Attempt.Start(_exam.Id, _candidate, number, Fixtures.Now, Fixtures.Now.AddMinutes(30));
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        return attempt;
    }

    private static IEnumerable<Guid> QuestionOrder(AttemptDto dto) => dto.Sections.SelectMany(s => s.Questions).Select(q => q.Id);

    [Fact]
    public async Task TheFirstAttempt_ShowsQuestionsAndOptionsExactlyAsAuthored()
    {
        var dto = await Views.BuildAsync(Open(1), _exam, CancellationToken.None);

        Assert.Equal(_questions.Select(q => q.Id), QuestionOrder(dto));
        foreach (var shown in dto.Sections.Single().Questions)
            Assert.Equal(_questions.Single(q => q.Id == shown.Id).Options.Select(o => o.Id), shown.Options.Select(o => o.Id));
    }

    [Fact]
    public async Task ASecondAttempt_ShowsTheSameQuestionsAndOptions_ShuffledAndNeverInTheAuthoredOrder()
    {
        var dto = await Views.BuildAsync(Open(2), _exam, CancellationToken.None);

        Assert.Equal(_questions.Select(q => q.Id).Order(), QuestionOrder(dto).Order());
        Assert.NotEqual(_questions.Select(q => q.Id), QuestionOrder(dto));
        foreach (var shown in dto.Sections.Single().Questions)
        {
            var authored = _questions.Single(q => q.Id == shown.Id).Options.Select(o => o.Id).ToList();
            Assert.Equal(authored.Order(), shown.Options.Select(o => o.Id).Order());
            Assert.NotEqual(authored, shown.Options.Select(o => o.Id));
        }
    }

    [Fact]
    public async Task TheShuffledOrder_IsTheSameEveryTimeTheAttemptIsRead_AndTheSavedAnswerFollowsItsQuestion()
    {
        var attempt = Open(2);
        var answered = _questions[3];
        attempt.RecordAnswer(answered.Id, answered.Correct(), Fixtures.Now);

        var first = await Views.BuildAsync(attempt, _exam, CancellationToken.None);
        var again = await Views.BuildAsync(attempt, _exam, CancellationToken.None);

        Assert.Equal(QuestionOrder(first), QuestionOrder(again));
        Assert.Equal(
            first.Sections.Single().Questions.Select(q => string.Join(',', q.Options.Select(o => o.Id))),
            again.Sections.Single().Questions.Select(q => string.Join(',', q.Options.Select(o => o.Id))));
        Assert.Equal(answered.Correct(), first.Sections.Single().Questions.Single(q => q.Id == answered.Id).SelectedOptionId);
    }

    [Fact]
    public async Task TwoAttempts_AreShuffledDifferently_SoTheThirdIsNotJustTheSecondAgain()
    {
        var second = await Views.BuildAsync(Open(2), _exam, CancellationToken.None);
        var third = await Views.BuildAsync(Open(3), _exam, CancellationToken.None);
        var other = await Views.BuildAsync(Open(2), _exam, CancellationToken.None);

        // Different attempt ids give different shuffles (five questions: 120 orders, so a clash is a 1-in-120 chance per pair, and
        // with all three alike it would take two such clashes).
        Assert.False(QuestionOrder(second).SequenceEqual(QuestionOrder(third)) && QuestionOrder(second).SequenceEqual(QuestionOrder(other)));
    }

    [Fact]
    public async Task TheReview_ListsQuestionsAndOptionsInTheOrderTheCandidateSat_ForARepeatAttempt()
    {
        var attempt = Open(2);
        var sat = await Views.BuildAsync(attempt, _exam, CancellationToken.None);
        attempt.Submit(Fixtures.Now.AddMinutes(10), 0m, 5m);

        var review = await Review.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        var reviewed = review.Sections.Single().Questions;
        Assert.Equal(QuestionOrder(sat), reviewed.Select(q => q.Id));
        foreach (var shown in sat.Sections.Single().Questions)
            Assert.Equal(shown.Options.Select(o => o.Id), reviewed.Single(q => q.Id == shown.Id).Options.Select(o => o.Id));
    }

    [Fact]
    public async Task TheReview_OfTheFirstAttempt_IsAsAuthored()
    {
        var attempt = Open(1);
        attempt.Submit(Fixtures.Now.AddMinutes(10), 0m, 5m);

        var review = await Review.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.Equal(_questions.Select(q => q.Id), review.Sections.Single().Questions.Select(q => q.Id));
    }

    [Fact]
    public async Task Sections_StayInTheAuthorsOrder_OnARepeatAttempt()
    {
        var second = Fixtures.Question("In section two");
        _questions.Add(second);
        var exam = _exam with
        {
            Sections =
            [
                new ExamSectionSnapshot(Guid.NewGuid(), "Section A", 1, _questions.Take(5).Select(q => q.Id).ToList()),
                new ExamSectionSnapshot(Guid.NewGuid(), "Section B", 2, [second.Id]),
            ],
        };

        var dto = await Views.BuildAsync(Open(2), exam, CancellationToken.None);

        Assert.Equal(["Section A", "Section B"], dto.Sections.Select(s => s.Name));
        Assert.Equal(5, dto.Sections[0].Questions.Count);
        Assert.Equal(second.Id, Assert.Single(dto.Sections[1].Questions).Id);
    }
}
