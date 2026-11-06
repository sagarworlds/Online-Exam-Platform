using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>What the exam catalog tells other modules about an exam, beyond what a setting's own tests already cover.</summary>
public class ExamCatalogTests
{
    private static readonly DateTime Created = new(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TheSnapshot_ReportsWhenTheExamWasCreated_NotWhenItWasLastChanged()
    {
        var exam = new Exam(null, "Maths", null, Exam.NotScheduledAt, Exam.NotScheduledAt, Guid.NewGuid(), Created);
        exam.AddQuestion(exam.AddSection("S", null, Created).Id, Guid.NewGuid(), QuestionPlacement.Unfiled, Created);
        exam.Schedule(Created.AddDays(10), Created.AddDays(11), null, null, null, Created.AddDays(2));
        exam.Publish(Created.AddDays(3));
        var repository = Substitute.For<IExamRepository>();
        repository.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([exam]);

        var published = await new ExamCatalog(repository).FindPublishedAsync([exam.Id], CancellationToken.None);

        // The candidate's page lists the newest exam first: editing an old exam must not move it to the top.
        Assert.Equal(Created.AddDays(3), exam.UpdatedAt);
        Assert.Equal(Created, Assert.Single(published).CreatedAtUtc);
    }
}
