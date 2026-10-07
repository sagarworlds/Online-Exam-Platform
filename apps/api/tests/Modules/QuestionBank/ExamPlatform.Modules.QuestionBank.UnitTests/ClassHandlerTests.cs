using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Application.Commands;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Application.Queries;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

public class ClassHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Author = Guid.NewGuid();

    private readonly IClassRepository _classes = Substitute.For<IClassRepository>();
    private readonly IQuestionBankUnitOfWork _unitOfWork = Substitute.For<IQuestionBankUnitOfWork>();
    private readonly Clock _clock = Substitute.For<Clock>();
    private readonly Dictionary<Guid, int> _bookCounts = [];

    public ClassHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _classes.NameIsTakenAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(false);
        _classes.CountBooksAsync(Arg.Any<CancellationToken>()).Returns(_bookCounts);
    }

    private SchoolClass Existing(string name = "4th")
    {
        var schoolClass = SchoolClass.Create(name, Author, Now);
        _classes.GetByIdAsync(schoolClass.Id, Arg.Any<CancellationToken>()).Returns(schoolClass);
        return schoolClass;
    }

    private ChangeClassHandler Change => new(_classes, _unitOfWork);

    // ---- creating ---------------------------------------------------------------------------------

    [Fact]
    public async Task Create_StoresTheClass_AndReportsItWithNoBooks()
    {
        SchoolClass? stored = null;
        _classes.When(c => c.Add(Arg.Any<SchoolClass>())).Do(call => stored = call.Arg<SchoolClass>());

        var dto = await new CreateClassHandler(_classes, _unitOfWork, _clock)
            .HandleAsync(new CreateClassCommand("  4th ", Author), CancellationToken.None);

        Assert.Equal("4th", dto.Name);
        Assert.Equal(0, dto.BookCount);
        Assert.False(dto.IsArchived);
        Assert.Equal(Now, dto.CreatedAtUtc);
        Assert.Equal(stored!.Id, dto.Id);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_WhenTheNameIsTaken_Throws409_AndStoresNothing()
    {
        // The name that is checked is the trimmed one, so " 4th " clashes with "4th".
        _classes.NameIsTakenAsync("4th", null, Arg.Any<CancellationToken>()).Returns(true);

        var error = await Assert.ThrowsAsync<DuplicateClassError>(
            () => new CreateClassHandler(_classes, _unitOfWork, _clock).HandleAsync(new CreateClassCommand(" 4th ", Author), CancellationToken.None));

        Assert.Equal(409, error.HttpStatusCode);
        Assert.Equal("duplicate_class", error.ErrorCode);
        _classes.DidNotReceive().Add(Arg.Any<SchoolClass>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_WithABlankName_Throws400_WithoutAskingTheDatabase()
    {
        await Assert.ThrowsAsync<InvalidClassError>(
            () => new CreateClassHandler(_classes, _unitOfWork, _clock).HandleAsync(new CreateClassCommand("  ", Author), CancellationToken.None));

        await _classes.DidNotReceiveWithAnyArgs().NameIsTakenAsync(default!, default, default);
    }

    // ---- renaming ---------------------------------------------------------------------------------

    [Fact]
    public async Task Rename_ChangesTheName_AndReportsTheBooksUnderIt()
    {
        var schoolClass = Existing();
        _bookCounts[schoolClass.Id] = 3;

        var dto = await Change.RenameAsync(new RenameClassCommand(schoolClass.Id, " Class 4 "), CancellationToken.None);

        Assert.Equal("Class 4", dto.Name);
        Assert.Equal(3, dto.BookCount);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rename_ChecksTheNameAgainstTheOtherClassesOnly_SoKeepingItsOwnNameIsNotAClash()
    {
        var schoolClass = Existing();

        await Change.RenameAsync(new RenameClassCommand(schoolClass.Id, "4th"), CancellationToken.None);

        await _classes.Received(1).NameIsTakenAsync("4th", schoolClass.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rename_ToANameAnotherClassHas_Throws409_AndKeepsTheOldName()
    {
        var schoolClass = Existing();
        _classes.NameIsTakenAsync("5th", schoolClass.Id, Arg.Any<CancellationToken>()).Returns(true);

        await Assert.ThrowsAsync<DuplicateClassError>(
            () => Change.RenameAsync(new RenameClassCommand(schoolClass.Id, "5th"), CancellationToken.None));

        Assert.Equal("4th", schoolClass.Name);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rename_ToABlankName_Throws400()
    {
        var schoolClass = Existing();

        await Assert.ThrowsAsync<InvalidClassError>(() => Change.RenameAsync(new RenameClassCommand(schoolClass.Id, ""), CancellationToken.None));

        Assert.Equal("4th", schoolClass.Name);
    }

    // ---- archiving --------------------------------------------------------------------------------

    [Fact]
    public async Task ArchiveThenRestore_FlipsTheFlag_AndSaves()
    {
        var schoolClass = Existing();

        var archived = await Change.ArchiveAsync(schoolClass.Id, CancellationToken.None);
        Assert.True(archived.IsArchived);

        var restored = await Change.RestoreAsync(schoolClass.Id, CancellationToken.None);
        Assert.False(restored.IsArchived);
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EveryChange_ToAnUnknownClass_Throws404_AndSavesNothing()
    {
        var unknown = Guid.NewGuid();
        _classes.GetByIdAsync(unknown, Arg.Any<CancellationToken>()).Returns((SchoolClass?)null);

        var rename = await Assert.ThrowsAsync<ClassNotFoundError>(() => Change.RenameAsync(new RenameClassCommand(unknown, "x"), CancellationToken.None));
        await Assert.ThrowsAsync<ClassNotFoundError>(() => Change.ArchiveAsync(unknown, CancellationToken.None));
        await Assert.ThrowsAsync<ClassNotFoundError>(() => Change.RestoreAsync(unknown, CancellationToken.None));

        Assert.Equal(404, rename.HttpStatusCode);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- listing ----------------------------------------------------------------------------------

    [Fact]
    public async Task List_ReportsEachClassWithItsBookCount_AndZeroForOneWithNone()
    {
        var fourth = SchoolClass.Create("4th", Author, Now);
        var fifth = SchoolClass.Create("5th", Author, Now);
        _bookCounts[fourth.Id] = 2;
        _classes.ListAsync(false, Arg.Any<CancellationToken>()).Returns([fourth, fifth]);

        var list = await new ListClassesHandler(_classes).HandleAsync(includeArchived: false, CancellationToken.None);

        Assert.Equal([("4th", 2), ("5th", 0)], list.Select(c => (c.Name, c.BookCount)));
    }

    [Fact]
    public async Task List_PassesTheArchivedChoiceOn()
    {
        _classes.ListAsync(true, Arg.Any<CancellationToken>()).Returns([]);

        await new ListClassesHandler(_classes).HandleAsync(includeArchived: true, CancellationToken.None);

        await _classes.Received(1).ListAsync(true, Arg.Any<CancellationToken>());
    }
}
