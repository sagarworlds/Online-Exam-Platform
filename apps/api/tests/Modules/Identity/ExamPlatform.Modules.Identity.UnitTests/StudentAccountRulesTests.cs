using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Exceptions;
using ExamPlatform.Modules.Identity.Domain.Rbac;

namespace ExamPlatform.Modules.Identity.UnitTests;

/// <summary>What makes an account a student's, and the rules for staff correcting its contact details and date of birth.</summary>
public class StudentAccountRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);

    private static User Register(string? email = "asha@example.com", string? phone = null) =>
        User.Register(email, phone, new DateOnly(2000, 1, 1), "Asha Rao", Now);

    private static Role RoleNamed(string name) => Role.Create(name, requiresTwoFactor: false);

    [Fact]
    public void IsStudent_WithOnlyTheCandidateRole_IsTrue()
    {
        var user = Register();
        user.AssignRole(RoleNamed(RbacCatalog.RoleNames.Candidate));

        Assert.True(user.IsStudent);
    }

    [Fact]
    public void IsStudent_WithCandidateAndGuardianRoles_IsTrue()
    {
        // A guardian who is also a student is still a student: neither role carries staff powers.
        var user = Register();
        user.AssignRole(RoleNamed(RbacCatalog.RoleNames.Candidate));
        user.AssignRole(RoleNamed(RbacCatalog.RoleNames.Guardian));

        Assert.True(user.IsStudent);
    }

    [Fact]
    public void IsStudent_WithNoRole_IsFalse()
    {
        Assert.False(Register().IsStudent);
    }

    [Theory]
    [InlineData(RbacCatalog.RoleNames.SuperAdmin)]
    [InlineData(RbacCatalog.RoleNames.ExamAdmin)]
    [InlineData(RbacCatalog.RoleNames.InstituteTeacher)]
    public void IsStudent_WhenTheAccountAlsoHoldsAStaffRole_IsFalse(string staffRole)
    {
        // A colleague who once registered as a candidate must not become editable through the student routes.
        var user = Register();
        user.AssignRole(RoleNamed(RbacCatalog.RoleNames.Candidate));
        user.AssignRole(RoleNamed(staffRole));

        Assert.False(user.IsStudent);
    }

    [Fact]
    public void ChangeContact_ReplacesBothAndTrimsThem()
    {
        var user = Register(phone: "+919800000000");

        user.ChangeContact("  new@example.com ", " +919811111111 ");

        Assert.Equal("new@example.com", user.Email);
        Assert.Equal("+919811111111", user.PhoneNumber);
    }

    [Fact]
    public void ChangeContact_WithABlankEmailAndAPhone_LeavesTheAccountWithNoEmail()
    {
        var user = Register(phone: "+919800000000");

        user.ChangeContact("   ", "+919800000000");

        // Null, not blank: the unique index skips nulls but would treat two stored "" as the same address.
        Assert.Null(user.Email);
        Assert.Equal("+919800000000", user.PhoneNumber);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "  ")]
    public void ChangeContact_WithNeitherContact_ThrowsContactRequiredErrorAndChangesNothing(string? email, string? phone)
    {
        var user = Register(phone: "+919800000000");

        Assert.Throws<ContactRequiredError>(() => user.ChangeContact(email, phone));

        Assert.Equal("asha@example.com", user.Email);
        Assert.Equal("+919800000000", user.PhoneNumber);
    }

    [Theory]
    [InlineData(User.MaxEmailLength + 1, 0)]
    [InlineData(0, User.MaxPhoneNumberLength + 1)]
    public void ChangeContact_WithContactLongerThanStored_ThrowsInvalidContactErrorAndChangesNothing(int emailLength, int phoneLength)
    {
        var user = Register(phone: "+919800000000");
        var email = emailLength == 0 ? "asha@example.com" : new string('a', emailLength - "@x.in".Length) + "@x.in";
        var phone = phoneLength == 0 ? "+919800000000" : new string('9', phoneLength);

        Assert.Throws<InvalidContactError>(() => user.ChangeContact(email, phone));

        Assert.Equal("asha@example.com", user.Email);
        Assert.Equal("+919800000000", user.PhoneNumber);
    }

    [Fact]
    public void ChangeDateOfBirth_ReplacesIt_AndMovesTheAgeBand()
    {
        var user = Register();
        Assert.Equal(AgeBand.Adult, user.GetAgeBand(Now));

        user.ChangeDateOfBirth(new DateOnly(2012, 5, 1), Now);

        Assert.Equal(new DateOnly(2012, 5, 1), user.DateOfBirth);
        Assert.Equal(AgeBand.Minor, user.GetAgeBand(Now));
    }

    [Fact]
    public void ChangeDateOfBirth_WithNoDate_ThrowsAndKeepsTheOldOne()
    {
        var user = Register();

        var error = Assert.Throws<InvalidDateOfBirthError>(() => user.ChangeDateOfBirth(default, Now));

        Assert.Equal(InvalidDateOfBirthError.Missing().Message, error.Message);
        Assert.Equal(new DateOnly(2000, 1, 1), user.DateOfBirth);
    }

    [Fact]
    public void ChangeDateOfBirth_InTheFuture_ThrowsAndKeepsTheOldOne()
    {
        var user = Register();

        var error = Assert.Throws<InvalidDateOfBirthError>(() => user.ChangeDateOfBirth(new DateOnly(2026, 12, 1), Now));

        Assert.Equal(InvalidDateOfBirthError.InTheFuture().Message, error.Message);
        Assert.Equal(new DateOnly(2000, 1, 1), user.DateOfBirth);
    }

    [Fact]
    public void ChangeDateOfBirth_ImplausiblyLongAgo_ThrowsAndKeepsTheOldOne()
    {
        var user = Register();

        var error = Assert.Throws<InvalidDateOfBirthError>(() => user.ChangeDateOfBirth(new DateOnly(1850, 1, 1), Now));

        Assert.Equal(InvalidDateOfBirthError.TooLongAgo().Message, error.Message);
        Assert.Equal(new DateOnly(2000, 1, 1), user.DateOfBirth);
    }
}
