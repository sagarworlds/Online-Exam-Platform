using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

public class SchoolClassTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Author = Guid.NewGuid();

    [Fact]
    public void Create_StoresTheTrimmedName_AndWhoMadeItWhen()
    {
        var schoolClass = SchoolClass.Create("  4th  ", Author, Now);

        Assert.Equal("4th", schoolClass.Name);
        Assert.Equal(Author, schoolClass.CreatedBy);
        Assert.Equal(Now, schoolClass.CreatedAtUtc);
        Assert.False(schoolClass.IsArchived);
        Assert.NotEqual(Guid.Empty, schoolClass.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutAName_Throws400(string? name)
    {
        var error = Assert.Throws<InvalidClassError>(() => SchoolClass.Create(name, Author, Now));

        Assert.Equal("invalid_class", error.ErrorCode);
        Assert.Equal(400, error.HttpStatusCode);
    }

    [Fact]
    public void Create_AcceptsTheLongestName_AndRefusesOneCharacterMore()
    {
        Assert.Equal(SchoolClass.MaxNameLength, SchoolClass.Create(new string('x', SchoolClass.MaxNameLength), Author, Now).Name.Length);

        Assert.Throws<InvalidClassError>(() => SchoolClass.Create(new string('x', SchoolClass.MaxNameLength + 1), Author, Now));
    }

    [Fact]
    public void Rename_ChangesTheName_AndTrimsIt()
    {
        var schoolClass = SchoolClass.Create("4th", Author, Now);

        schoolClass.Rename("  Class 4 ");

        Assert.Equal("Class 4", schoolClass.Name);
    }

    [Fact]
    public void Rename_ToABlankName_Throws_AndKeepsTheOldOne()
    {
        var schoolClass = SchoolClass.Create("4th", Author, Now);

        Assert.Throws<InvalidClassError>(() => schoolClass.Rename("   "));

        Assert.Equal("4th", schoolClass.Name);
    }

    [Fact]
    public void ArchiveAndRestore_TurnTheFlagOffAndOn_AndNothingElseChanges()
    {
        var schoolClass = SchoolClass.Create("4th", Author, Now);

        schoolClass.Archive();
        Assert.True(schoolClass.IsArchived);
        Assert.Equal("4th", schoolClass.Name);

        schoolClass.Restore();
        Assert.False(schoolClass.IsArchived);
    }

    [Fact]
    public void ValidateName_ReturnsTheTrimmedName_SoACallerCanLookForAClashBeforeAnythingIsMade()
    {
        Assert.Equal("4th", SchoolClass.ValidateName("  4th "));
    }
}
