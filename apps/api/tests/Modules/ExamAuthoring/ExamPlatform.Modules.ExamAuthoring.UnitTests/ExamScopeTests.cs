using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

public class ExamScopeTests
{
    private static readonly Guid BookA = Guid.NewGuid();
    private static readonly Guid BookB = Guid.NewGuid();
    private static readonly Guid Algebra = Guid.NewGuid();
    private static readonly Guid Geometry = Guid.NewGuid();
    private static readonly Guid Optics = Guid.NewGuid();

    private static QuestionPlacement In(Guid book, Guid chapter) => new(book, chapter);

    [Fact]
    public void Independent_AllowsAnyQuestion_FiledOrNot()
    {
        var scope = ExamScope.Independent();

        Assert.Equal(ExamScopeType.Independent, scope.Type);
        Assert.True(scope.Allows(QuestionPlacement.Unfiled));
        Assert.True(scope.Allows(In(BookA, Algebra)));
        Assert.True(scope.Allows(In(BookB, Optics)));
    }

    [Fact]
    public void Independent_IsANewInstanceEachTime_SoNoTwoExamsShareOne() =>
        Assert.NotSame(ExamScope.Independent(), ExamScope.Independent());

    [Fact]
    public void Book_AllowsEveryChapterOfThatBook_AndNothingElse()
    {
        var scope = ExamScope.ForBook(BookA);

        Assert.Equal(ExamScopeType.Book, scope.Type);
        Assert.Equal(BookA, scope.BookId);
        Assert.True(scope.Allows(In(BookA, Algebra)));
        Assert.True(scope.Allows(In(BookA, Geometry)));
        Assert.False(scope.Allows(In(BookB, Optics)));
        Assert.False(scope.Allows(QuestionPlacement.Unfiled));
    }

    [Fact]
    public void Chapters_AllowOnlyTheChosenChapters()
    {
        var scope = ExamScope.ForChapters(BookA, [Algebra]);

        Assert.Equal(ExamScopeType.Chapters, scope.Type);
        Assert.Equal(BookA, scope.BookId);
        Assert.True(scope.Allows(In(BookA, Algebra)));
        Assert.False(scope.Allows(In(BookA, Geometry)));
        Assert.False(scope.Allows(In(BookB, Optics)));
        Assert.False(scope.Allows(QuestionPlacement.Unfiled));
    }

    [Fact]
    public void Chapters_IgnoreRepeats() =>
        Assert.Equal([Algebra, Geometry], ExamScope.ForChapters(BookA, [Algebra, Geometry, Algebra]).ChapterIds);

    [Fact]
    public void ForBook_WithoutABook_Throws()
    {
        var error = Assert.Throws<InvalidExamConfigError>(() => ExamScope.ForBook(Guid.Empty));
        Assert.Equal(400, error.HttpStatusCode);
    }

    [Fact]
    public void ForChapters_NeedsABookAndAtLeastOneRealChapter()
    {
        Assert.Throws<InvalidExamConfigError>(() => ExamScope.ForChapters(Guid.Empty, [Algebra]));
        Assert.Throws<InvalidExamConfigError>(() => ExamScope.ForChapters(BookA, []));
        Assert.Throws<InvalidExamConfigError>(() => ExamScope.ForChapters(BookA, null));
        Assert.Throws<InvalidExamConfigError>(() => ExamScope.ForChapters(BookA, [Algebra, Guid.Empty]));
    }
}
