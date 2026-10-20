using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Staff looking after students' accounts over real HTTP and a real database: finding a student, and correcting the name, e-mail
/// address, mobile number and date of birth they signed up with. A new address or number takes effect at once, signs the student out,
/// and carries their invitations with it.
/// </summary>
public sealed class StudentFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly BornOn = new(1995, 3, 14);

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string errorCode)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(errorCode, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    private static string UniquePhone() => $"+9198{System.Security.Cryptography.RandomNumberGenerator.GetInt32(10_000_000, 99_999_999)}";

    private async Task<HttpClient> StaffClientAsync(string role) => (await StaffAsync(role)).Client;

    private async Task<(HttpClient Client, SignedInTestUser User)> StaffAsync(string role)
    {
        var client = factory.CreateClient();
        var staff = await factory.SignInAsAsync(role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staff.AccessToken);
        return (client, staff);
    }

    /// <summary>A student who signed up with a name, an e-mail address, a mobile number and a date of birth, and is signed in now.</summary>
    private async Task<(HttpClient Client, SignedInTestUser User, string Phone)> StudentAsync(string? email = null)
    {
        var phone = UniquePhone();
        var client = factory.CreateClient();
        var user = await factory.SignInAsAsync("Candidate", email ?? UniqueEmail(), BornOn, phoneNumber: phone);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        return (client, user, phone);
    }

    /// <summary>Signs in the way a student does after a change of address: asks for a code at the address and enters it.</summary>
    private async Task<HttpClient> SignInWithCodeAsync(string email)
    {
        var client = factory.CreateClient();
        var request = await client.PostAsJsonAsync("/v1/auth/otp/request", new { channel = "Email", destination = email });
        request.EnsureSuccessStatusCode();
        var challengeId = (await request.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("otpChallengeId").GetGuid();
        var verified = await client.PostAsJsonAsync(
            "/v1/auth/otp/verify", new { otpChallengeId = challengeId, code = factory.OtpSender.GetLastCode(email) });
        verified.EnsureSuccessStatusCode();
        var accessToken = (await verified.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static object Profile(string name, string? email, string? phone, string dateOfBirth = "1995-03-14") =>
        new { displayName = name, email, phoneNumber = phone, dateOfBirth };

    private static Task<HttpResponseMessage> UpdateAsync(HttpClient staff, Guid studentId, object profile) =>
        staff.PutAsJsonAsync($"/v1/admin/students/{studentId}", profile);

    private static Task<JsonElement> StudentOfAsync(HttpClient staff, Guid studentId) =>
        staff.GetFromJsonAsync<JsonElement>($"/v1/admin/students/{studentId}");

    // ---- finding students -------------------------------------------------------------------------

    private static async Task<List<JsonElement>> SearchAsync(HttpClient staff, string text) =>
        (await staff.GetFromJsonAsync<JsonElement>($"/v1/admin/students?search={Uri.EscapeDataString(text)}")).EnumerateArray().ToList();

    // A listing without a search holds the first 200 students by name, so a test that must find its own student searches for it.

    [Fact]
    public async Task List_ShowsStudents_WithoutTheirDateOfBirth_AndNoStaffAccount()
    {
        using var admin = await factory.AdminClientAsync();
        var token = Guid.NewGuid().ToString("N");
        var (_, student, _) = await StudentAsync($"listed.{token}@tests.local");
        await factory.SignInAsAsync("ExamAdmin", $"listed.{token}.staff@tests.local");

        var rows = await SearchAsync(admin, token);

        // The staff account's address matches the search too, and is still not a student.
        var row = Assert.Single(rows);
        Assert.Equal(student.UserId, row.GetProperty("id").GetGuid());
        Assert.Equal(student.Email, row.GetProperty("email").GetString());
        Assert.Equal("Active", row.GetProperty("status").GetString());
        Assert.False(row.TryGetProperty("dateOfBirth", out _));
    }

    [Fact]
    public async Task List_Search_FindsByEmailNameOrNumber_WithoutRegardToCase()
    {
        using var admin = await factory.AdminClientAsync();
        var token = Guid.NewGuid().ToString("N");
        var (_, student, phone) = await StudentAsync($"Find.{token}@Tests.local");
        await StudentAsync();
        // Students all sign up as "Test Candidate"; a name of their own is what a search by name can find.
        (await UpdateAsync(admin, student.UserId, Profile($"Zed {token}", student.Email, phone))).EnsureSuccessStatusCode();

        async Task<List<Guid>> IdsAsync(string text) => (await SearchAsync(admin, text)).Select(row => row.GetProperty("id").GetGuid()).ToList();

        Assert.Equal([student.UserId], await IdsAsync($"find.{token}"));
        Assert.Equal([student.UserId], await IdsAsync($"FIND.{token.ToUpperInvariant()}"));
        Assert.Equal([student.UserId], await IdsAsync(phone));
        Assert.Equal([student.UserId], await IdsAsync($"zed {token.ToUpperInvariant()}"));
    }

    [Fact]
    public async Task List_Search_TreatsUnderscoreAndPercentAsPlainCharacters()
    {
        using var admin = await factory.AdminClientAsync();
        var token = Guid.NewGuid().ToString("N");
        var (_, literal, _) = await StudentAsync($"a_{token}@tests.local");
        var (_, lookalike, _) = await StudentAsync($"aX{token}@tests.local");

        var found = (await admin.GetFromJsonAsync<JsonElement>($"/v1/admin/students?search={Uri.EscapeDataString($"a_{token}")}"))
            .EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).ToList();

        Assert.Contains(literal.UserId, found);
        Assert.DoesNotContain(lookalike.UserId, found);

        var everything = (await admin.GetFromJsonAsync<JsonElement>("/v1/admin/students?search=%25")).EnumerateArray().ToList();
        Assert.Empty(everything);
    }

    [Fact]
    public async Task Get_ReturnsTheProfile_WithTheDateOfBirthAndAgeBand()
    {
        using var admin = await factory.AdminClientAsync();
        var (_, student, phone) = await StudentAsync();

        var profile = await StudentOfAsync(admin, student.UserId);

        Assert.Equal(student.Email, profile.GetProperty("email").GetString());
        Assert.Equal(phone, profile.GetProperty("phoneNumber").GetString());
        Assert.Equal("1995-03-14", profile.GetProperty("dateOfBirth").GetString());
        Assert.Equal("Adult", profile.GetProperty("ageBand").GetString());
        Assert.Equal("Active", profile.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Get_ForAStaffAccountOrAnUnknownId_Returns404()
    {
        using var admin = await factory.AdminClientAsync();
        var colleague = await factory.SignInAsAsync("ExamAdmin");

        await AssertProblemAsync(await admin.GetAsync($"/v1/admin/students/{colleague.UserId}"), HttpStatusCode.NotFound, "user_not_found");
        await AssertProblemAsync(await admin.GetAsync($"/v1/admin/students/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "user_not_found");
    }

    [Fact]
    public async Task Lookup_FindsTheStudentsHoldingTheAddresses_WithoutRegardToCase_AndNoStaffAccount()
    {
        using var admin = await factory.AdminClientAsync();
        var (_, student, _) = await StudentAsync();
        var staff = await factory.SignInAsAsync("InstituteTeacher");
        var nobody = UniqueEmail();

        var response = await admin.PostAsJsonAsync(
            "/v1/admin/students/lookup", new { emails = new[] { student.Email.ToUpperInvariant(), staff.Email, nobody, "" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var found = Assert.Single((await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
        Assert.Equal(student.UserId, found.GetProperty("id").GetGuid());
        Assert.Equal(student.Email, found.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Lookup_OfMoreThan200Addresses_Returns400()
    {
        using var admin = await factory.AdminClientAsync();
        var emails = Enumerable.Range(0, 201).Select(n => $"lookup-{n}-{Guid.NewGuid():N}@tests.local").ToList();

        await AssertProblemAsync(await admin.PostAsJsonAsync("/v1/admin/students/lookup", new { emails }), HttpStatusCode.BadRequest, "too_many_addresses");
    }

    [Fact]
    public async Task Lookup_ByACandidate_Returns403_AndWithoutSigningIn401()
    {
        var (candidate, _, _) = await StudentAsync();
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await candidate.PostAsJsonAsync("/v1/admin/students/lookup", new { emails = new[] { "a@example.com" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/v1/admin/students/lookup", new { emails = new[] { "a@example.com" } })).StatusCode);
    }

    // ---- correcting a profile ---------------------------------------------------------------------

    [Fact]
    public async Task Update_ChangesEveryField_AndSaysWhatItDid()
    {
        using var admin = await factory.AdminClientAsync();
        var (_, student, _) = await StudentAsync();
        var email = UniqueEmail();
        var phone = UniquePhone();

        var response = await UpdateAsync(admin, student.UserId, Profile("Asha Kumar", email, phone, "2012-05-01"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["name", "email", "phoneNumber", "dateOfBirth"], result.GetProperty("changed").EnumerateArray().Select(f => f.GetString()));
        Assert.True(result.GetProperty("signedOut").GetBoolean());

        var profile = await StudentOfAsync(admin, student.UserId);
        Assert.Equal("Asha Kumar", profile.GetProperty("displayName").GetString());
        Assert.Equal(email, profile.GetProperty("email").GetString());
        Assert.Equal(phone, profile.GetProperty("phoneNumber").GetString());
        Assert.Equal("2012-05-01", profile.GetProperty("dateOfBirth").GetString());
        Assert.Equal("Minor", profile.GetProperty("ageBand").GetString());
    }

    [Fact]
    public async Task Update_ChangingOnlyTheName_KeepsTheStudentSignedIn()
    {
        using var admin = await factory.AdminClientAsync();
        var (client, student, phone) = await StudentAsync();

        var response = await UpdateAsync(admin, student.UserId, Profile("Asha R. Rao", student.Email, phone));

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["name"], result.GetProperty("changed").EnumerateArray().Select(f => f.GetString()));
        Assert.False(result.GetProperty("signedOut").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/me/profile")).StatusCode);
    }

    [Fact]
    public async Task Update_WithNothingDifferent_ChangesNothingAndLeavesNoAuditEntry()
    {
        var (admin, staff) = await StaffAsync("SuperAdmin");
        using var adminClient = admin;
        var (client, student, phone) = await StudentAsync();
        var current = await StudentOfAsync(admin, student.UserId);

        var response = await UpdateAsync(admin, student.UserId, Profile(current.GetProperty("displayName").GetString()!, student.Email, phone));

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(result.GetProperty("changed").EnumerateArray());
        Assert.False(result.GetProperty("signedOut").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/me/profile")).StatusCode);
        Assert.Empty(await StudentAuditEntriesAsync(admin, staff.UserId, student.UserId));
    }

    [Fact]
    public async Task Update_ChangingTheEmail_SignsTheStudentOut_AndTheyCanSignInAtTheNewAddress()
    {
        using var admin = await factory.AdminClientAsync();
        var (oldSession, student, phone) = await StudentAsync();
        var newEmail = UniqueEmail();

        (await UpdateAsync(admin, student.UserId, Profile("Test Candidate", newEmail, phone))).EnsureSuccessStatusCode();

        // The session that was open under the old address ends at once.
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldSession.GetAsync("/v1/me/profile")).StatusCode);

        // The student signs in again with a code sent to the new address, and it is the same account.
        using var signedInAgain = await SignInWithCodeAsync(newEmail);
        var profile = await signedInAgain.GetFromJsonAsync<JsonElement>("/v1/me/profile");
        Assert.Equal(student.UserId, profile.GetProperty("userId").GetGuid());
        Assert.Equal(newEmail, profile.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Update_ChangingTheEmail_EndsACodeAlreadySentToTheOldAddress()
    {
        // Whoever holds the old address, which may be why it is being changed, asks for a sign-in code just before staff change it.
        using var admin = await factory.AdminClientAsync();
        var (_, student, phone) = await StudentAsync();
        using var anonymous = factory.CreateClient();
        var request = await anonymous.PostAsJsonAsync("/v1/auth/otp/request", new { channel = "Email", destination = student.Email });
        request.EnsureSuccessStatusCode();
        var challengeId = (await request.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("otpChallengeId").GetGuid();
        var code = factory.OtpSender.GetLastCode(student.Email);

        (await UpdateAsync(admin, student.UserId, Profile("Test Candidate", UniqueEmail(), phone))).EnsureSuccessStatusCode();

        // The code is right, and it signs nobody in: the account is no longer the one at that address.
        var verify = await anonymous.PostAsJsonAsync("/v1/auth/otp/verify", new { otpChallengeId = challengeId, code });
        await AssertProblemAsync(verify, HttpStatusCode.BadRequest, "otp_superseded");
    }

    [Fact]
    public async Task Update_ChangingOnlyTheName_LeavesACodeAlreadySentStillWorking()
    {
        using var admin = await factory.AdminClientAsync();
        var (_, student, phone) = await StudentAsync();
        using var anonymous = factory.CreateClient();
        var request = await anonymous.PostAsJsonAsync("/v1/auth/otp/request", new { channel = "Email", destination = student.Email });
        request.EnsureSuccessStatusCode();
        var challengeId = (await request.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("otpChallengeId").GetGuid();

        (await UpdateAsync(admin, student.UserId, Profile("Renamed", student.Email, phone))).EnsureSuccessStatusCode();

        var verify = await anonymous.PostAsJsonAsync(
            "/v1/auth/otp/verify", new { otpChallengeId = challengeId, code = factory.OtpSender.GetLastCode(student.Email) });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
    }

    [Fact]
    public async Task Update_ChangingTheMobileNumber_SignsTheStudentOut_ButMovesNoInvitation()
    {
        using var admin = await factory.AdminClientAsync();
        var (client, student, _) = await StudentAsync();

        var response = await UpdateAsync(admin, student.UserId, Profile("Test Candidate", student.Email, UniquePhone()));

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["phoneNumber"], result.GetProperty("changed").EnumerateArray().Select(f => f.GetString()));
        Assert.True(result.GetProperty("signedOut").GetBoolean());
        Assert.Equal(0, result.GetProperty("invitationsMoved").GetInt32());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/me/profile")).StatusCode);
    }

    [Fact]
    public async Task Update_ChangingTheEmail_MovesTheStudentsInvitations_SoTheNewAddressCanAcceptThem()
    {
        using var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "What is 2 + 2?", "4", "5");
        var examId = await CreateExamAsync(admin, "Moved Invitation Exam", [question], startsIn: TimeSpan.FromHours(-1));
        var (_, student, phone) = await StudentAsync();
        var invite = await InviteAsync(admin, examId, student.Email);
        var bystander = UniqueEmail();
        await InviteAsync(admin, examId, bystander);
        var newEmail = UniqueEmail();

        var response = await UpdateAsync(admin, student.UserId, Profile("Test Candidate", newEmail, phone));

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, result.GetProperty("invitationsMoved").GetInt32());
        var addresses = (await admin.GetFromJsonAsync<JsonElement>("/v1/invites"))
            .EnumerateArray().Select(i => i.GetProperty("email").GetString()!).ToList();
        Assert.Contains(newEmail, addresses);
        Assert.DoesNotContain(addresses, address => string.Equals(address, student.Email, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(bystander, addresses);

        // The invitation was sent with a code before the change; the student, signed in again under the new address, can still use it.
        var code = CodeFromLink(invite.GetProperty("inviteLink").GetString()!);
        using var signedInAgain = await SignInWithCodeAsync(newEmail);
        Assert.Equal(HttpStatusCode.OK, (await signedInAgain.PostAsJsonAsync("/v1/invites/accept", new { code })).StatusCode);
    }

    [Fact]
    public async Task Update_ChangingTheDateOfBirth_IsAuditedByAgeBandAndNeverByValue()
    {
        var (admin, superAdmin) = await StaffAsync("SuperAdmin");
        using var adminClient = admin;
        var (_, student, phone) = await StudentAsync();
        var newEmail = UniqueEmail();

        (await UpdateAsync(admin, student.UserId, Profile("Test Candidate", newEmail, phone, "2012-05-01"))).EnsureSuccessStatusCode();

        var entry = Assert.Single(await StudentAuditEntriesAsync(admin, superAdmin.UserId, student.UserId));
        Assert.Equal(superAdmin.UserId, entry.GetProperty("actorUserId").GetGuid());
        var metadata = entry.GetProperty("metadata");
        Assert.Equal("email,dateOfBirth", metadata.GetProperty("fields").GetString());
        Assert.Equal("Adult", metadata.GetProperty("ageBandBefore").GetString());
        Assert.Equal("Minor", metadata.GetProperty("ageBandAfter").GetString());
        Assert.Equal("true", metadata.GetProperty("signedOut").GetString());

        // NFR-6: no address, number, name or date anywhere in the entry.
        var recorded = entry.GetRawText();
        Assert.DoesNotContain(newEmail, recorded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(student.Email, recorded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(phone, recorded, StringComparison.Ordinal);
        Assert.DoesNotContain("2012-05-01", recorded, StringComparison.Ordinal);
        Assert.DoesNotContain("1995-03-14", recorded, StringComparison.Ordinal);
    }

    // ---- what is refused --------------------------------------------------------------------------

    [Fact]
    public async Task Update_ToAnAddressAnotherAccountHolds_Returns409_AndChangesNothing()
    {
        using var admin = await factory.AdminClientAsync();
        var (client, student, phone) = await StudentAsync();
        var (_, other, _) = await StudentAsync();

        // The other student's address in capitals is still theirs: two accounts must not differ only by case.
        var response = await UpdateAsync(admin, student.UserId, Profile("Changed Name", other.Email.ToUpperInvariant(), phone));

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "contact_in_use");
        var profile = await StudentOfAsync(admin, student.UserId);
        Assert.Equal("Test Candidate", profile.GetProperty("displayName").GetString());
        Assert.Equal(student.Email, profile.GetProperty("email").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/me/profile")).StatusCode);
    }

    [Fact]
    public async Task Update_ToANumberAnotherAccountHolds_Returns409_AndChangesNothing()
    {
        using var admin = await factory.AdminClientAsync();
        var (client, student, phone) = await StudentAsync();
        var (_, _, otherPhone) = await StudentAsync();

        var response = await UpdateAsync(admin, student.UserId, Profile("Test Candidate", student.Email, otherPhone));

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "contact_in_use");
        Assert.Equal(phone, (await StudentOfAsync(admin, student.UserId)).GetProperty("phoneNumber").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/me/profile")).StatusCode);
    }

    [Fact]
    public async Task Update_ToAnAddressAnotherAccountHolds_LeavesTheStudentsInvitationsWhereTheyWere()
    {
        using var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "What is 2 + 2?", "4", "5");
        var examId = await CreateExamAsync(admin, "Unmoved Invitation Exam", [question], startsIn: TimeSpan.FromHours(-1));
        var (_, student, phone) = await StudentAsync();
        var (_, other, _) = await StudentAsync();
        await InviteAsync(admin, examId, student.Email);

        await UpdateAsync(admin, student.UserId, Profile("Test Candidate", other.Email, phone));

        var addresses = (await admin.GetFromJsonAsync<JsonElement>("/v1/invites"))
            .EnumerateArray().Select(i => i.GetProperty("email").GetString()!).ToList();
        Assert.Contains(student.Email, addresses);
    }

    [Theory]
    [InlineData("not-an-email", "invalid_email_address")]
    [InlineData("spaces in@example.com", "invalid_email_address")]
    public async Task Update_WithAnInvalidEmail_Returns400(string email, string errorCode)
    {
        using var admin = await factory.AdminClientAsync();
        var (_, student, phone) = await StudentAsync();

        await AssertProblemAsync(await UpdateAsync(admin, student.UserId, Profile("Test Candidate", email, phone)), HttpStatusCode.BadRequest, errorCode);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12345")]
    public async Task Update_WithAnInvalidMobileNumber_Returns400(string phone)
    {
        using var admin = await factory.AdminClientAsync();
        var (_, student, _) = await StudentAsync();

        await AssertProblemAsync(
            await UpdateAsync(admin, student.UserId, Profile("Test Candidate", student.Email, phone)), HttpStatusCode.BadRequest, "invalid_phone_number");
    }

    [Fact]
    public async Task Update_WithNeitherEmailNorNumber_Returns400()
    {
        using var admin = await factory.AdminClientAsync();
        var (_, student, _) = await StudentAsync();

        var response = await UpdateAsync(admin, student.UserId, Profile("Test Candidate", "", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithABlankName_Returns400()
    {
        using var admin = await factory.AdminClientAsync();
        var (_, student, phone) = await StudentAsync();

        await AssertProblemAsync(
            await UpdateAsync(admin, student.UserId, Profile("   ", student.Email, phone)), HttpStatusCode.BadRequest, "invalid_display_name");
    }

    [Theory]
    [InlineData("2999-01-01")]
    [InlineData("1800-01-01")]
    public async Task Update_WithAnImplausibleDateOfBirth_Returns400(string dateOfBirth)
    {
        using var admin = await factory.AdminClientAsync();
        var (_, student, phone) = await StudentAsync();

        await AssertProblemAsync(
            await UpdateAsync(admin, student.UserId, Profile("Test Candidate", student.Email, phone, dateOfBirth)),
            HttpStatusCode.BadRequest,
            "invalid_date_of_birth");
    }

    [Fact]
    public async Task Update_WithoutADateOfBirth_Returns400()
    {
        using var admin = await factory.AdminClientAsync();
        var (_, student, phone) = await StudentAsync();

        var response = await admin.PutAsJsonAsync(
            $"/v1/admin/students/{student.UserId}", new { displayName = "Test Candidate", email = student.Email, phoneNumber = phone });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_date_of_birth");
    }

    [Fact]
    public async Task Update_OfAStaffAccount_Returns404_AndLeavesItAlone()
    {
        using var admin = await factory.AdminClientAsync();
        var colleague = await factory.SignInAsAsync("InstituteTeacher");

        var response = await UpdateAsync(admin, colleague.UserId, Profile("Taken Over", UniqueEmail(), UniquePhone()));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "user_not_found");
        using var colleagueClient = factory.CreateClient();
        colleagueClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", colleague.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await colleagueClient.GetAsync("/v1/me/profile")).StatusCode);
    }

    // ---- deactivating -----------------------------------------------------------------------------

    private static Task<HttpResponseMessage> DeactivateAsync(HttpClient staff, Guid studentId) =>
        staff.PostAsync($"/v1/admin/students/{studentId}/deactivate", content: null);

    [Fact]
    public async Task Deactivate_SignsTheStudentOut_AndTheyCannotSignInAgain()
    {
        using var admin = await StaffClientAsync("ExamAdmin");
        var (student, user, _) = await StudentAsync();
        using var studentClient = student;
        Assert.Equal(HttpStatusCode.OK, (await studentClient.GetAsync("/v1/me/profile")).StatusCode);
        factory.OtpSender.TryGetLastCode(user.Email, out var codeBefore);

        var response = await DeactivateAsync(admin, user.UserId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Deactivated", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await studentClient.GetAsync("/v1/me/profile")).StatusCode);

        // Asking for a code is answered like any other request, but no code is sent, so the student cannot turn it into a session.
        using var anonymous = factory.CreateClient();
        var request = await anonymous.PostAsJsonAsync("/v1/auth/otp/request", new { channel = "Email", destination = user.Email });
        request.EnsureSuccessStatusCode();
        factory.OtpSender.TryGetLastCode(user.Email, out var codeAfter);
        Assert.Equal(codeBefore, codeAfter);
    }

    [Fact]
    public async Task Deactivate_KeepsTheAccount_AndItsDetails()
    {
        using var admin = await StaffClientAsync("ExamAdmin");
        var (_, user, phone) = await StudentAsync();

        (await DeactivateAsync(admin, user.UserId)).EnsureSuccessStatusCode();

        var kept = await StudentOfAsync(admin, user.UserId);
        Assert.Equal("Deactivated", kept.GetProperty("status").GetString());
        Assert.Equal(user.Email, kept.GetProperty("email").GetString());
        Assert.Equal(phone, kept.GetProperty("phoneNumber").GetString());
        Assert.Equal("1995-03-14", kept.GetProperty("dateOfBirth").GetString());
        // The page lists it still, marked, so staff can find the account they closed.
        var listed = Assert.Single(await SearchAsync(admin, user.Email));
        Assert.Equal("Deactivated", listed.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Deactivate_IsAuditedWithWhoDidIt_AndNoPersonalData()
    {
        var (admin, superAdmin) = await StaffAsync("SuperAdmin");
        using var adminClient = admin;
        var (_, user, phone) = await StudentAsync();

        (await DeactivateAsync(admin, user.UserId)).EnsureSuccessStatusCode();

        var entry = Assert.Single(await StudentAuditEntriesAsync(admin, superAdmin.UserId, user.UserId, "Identity.StudentDeactivated"));
        Assert.Equal(superAdmin.UserId, entry.GetProperty("actorUserId").GetGuid());
        // NFR-6: no address, number or name anywhere in the entry.
        var recorded = entry.GetRawText();
        Assert.DoesNotContain(user.Email, recorded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(phone, recorded, StringComparison.Ordinal);
        Assert.DoesNotContain("Test Candidate", recorded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deactivate_Twice_ChangesNothingTheSecondTime_AndIsAuditedOnce()
    {
        var (admin, superAdmin) = await StaffAsync("SuperAdmin");
        using var adminClient = admin;
        var (_, user, _) = await StudentAsync();

        (await DeactivateAsync(admin, user.UserId)).EnsureSuccessStatusCode();
        var again = await DeactivateAsync(admin, user.UserId);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("Deactivated", (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Single(await StudentAuditEntriesAsync(admin, superAdmin.UserId, user.UserId, "Identity.StudentDeactivated"));
    }

    [Fact]
    public async Task Deactivate_OfAStaffAccountOrAnUnknownId_Returns404_AndLeavesTheStaffAccountAlone()
    {
        using var admin = await factory.AdminClientAsync();
        var colleague = await factory.SignInAsAsync("InstituteTeacher");

        await AssertProblemAsync(await DeactivateAsync(admin, colleague.UserId), HttpStatusCode.NotFound, "user_not_found");
        await AssertProblemAsync(await DeactivateAsync(admin, Guid.NewGuid()), HttpStatusCode.NotFound, "user_not_found");

        using var colleagueClient = factory.CreateClient();
        colleagueClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", colleague.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await colleagueClient.GetAsync("/v1/me/profile")).StatusCode);
    }

    [Fact]
    public async Task Deactivate_ByATeacherOrAStudent_Returns403_AndTheStudentStaysSignedIn()
    {
        using var teacher = await StaffClientAsync("InstituteTeacher");
        var (student, user, _) = await StudentAsync();
        using var studentClient = student;

        Assert.Equal(HttpStatusCode.Forbidden, (await DeactivateAsync(teacher, user.UserId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DeactivateAsync(studentClient, user.UserId)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await studentClient.GetAsync("/v1/me/profile")).StatusCode);
        using var admin = await StaffClientAsync("ExamAdmin");
        Assert.Equal("Active", (await StudentOfAsync(admin, user.UserId)).GetProperty("status").GetString());
    }

    private static Task<HttpResponseMessage> ReactivateAsync(HttpClient staff, Guid studentId) =>
        staff.PostAsync($"/v1/admin/students/{studentId}/reactivate", content: null);

    [Fact]
    public async Task Reactivate_LetsTheStudentSignInAgain_WithACode_ButBringsNoOldSessionBack()
    {
        using var admin = await StaffClientAsync("ExamAdmin");
        var (student, user, _) = await StudentAsync();
        using var oldSession = student;
        (await DeactivateAsync(admin, user.UserId)).EnsureSuccessStatusCode();

        var response = await ReactivateAsync(admin, user.UserId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Active", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        // What the deactivation ended stays ended: the student has to sign in again.
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldSession.GetAsync("/v1/me/profile")).StatusCode);
        using var signedInAgain = await SignInWithCodeAsync(user.Email);
        Assert.Equal(HttpStatusCode.OK, (await signedInAgain.GetAsync("/v1/me/profile")).StatusCode);
        Assert.Equal("Active", (await StudentOfAsync(admin, user.UserId)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Reactivate_IsAuditedWithWhoDidIt_AndNoPersonalData_AndOnlyOnce()
    {
        var (admin, superAdmin) = await StaffAsync("SuperAdmin");
        using var adminClient = admin;
        var (_, user, phone) = await StudentAsync();
        (await DeactivateAsync(admin, user.UserId)).EnsureSuccessStatusCode();

        (await ReactivateAsync(admin, user.UserId)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await ReactivateAsync(admin, user.UserId)).StatusCode);

        var entry = Assert.Single(await StudentAuditEntriesAsync(admin, superAdmin.UserId, user.UserId, "Identity.StudentReactivated"));
        Assert.Equal(superAdmin.UserId, entry.GetProperty("actorUserId").GetGuid());
        var recorded = entry.GetRawText();
        Assert.DoesNotContain(user.Email, recorded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(phone, recorded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reactivate_OfAStudentWhoIsNotDeactivated_ChangesNothing()
    {
        using var admin = await StaffClientAsync("ExamAdmin");
        var (student, user, _) = await StudentAsync();
        using var studentClient = student;

        var response = await ReactivateAsync(admin, user.UserId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Active", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await studentClient.GetAsync("/v1/me/profile")).StatusCode);
    }

    [Fact]
    public async Task Reactivate_OfAStaffAccountOrAnUnknownId_Returns404()
    {
        using var admin = await factory.AdminClientAsync();
        var colleague = await factory.SignInAsAsync("InstituteTeacher");

        await AssertProblemAsync(await ReactivateAsync(admin, colleague.UserId), HttpStatusCode.NotFound, "user_not_found");
        await AssertProblemAsync(await ReactivateAsync(admin, Guid.NewGuid()), HttpStatusCode.NotFound, "user_not_found");
    }

    [Fact]
    public async Task Reactivate_ByATeacherOrAStudent_Returns403_AndTheAccountStaysDeactivated()
    {
        using var admin = await StaffClientAsync("ExamAdmin");
        using var teacher = await StaffClientAsync("InstituteTeacher");
        var (_, user, _) = await StudentAsync();
        // Another student, still signed in: the deactivated one's own session has ended, so it would be refused as 401, not 403.
        var (other, _, _) = await StudentAsync();
        using var otherStudent = other;
        (await DeactivateAsync(admin, user.UserId)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await ReactivateAsync(teacher, user.UserId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ReactivateAsync(otherStudent, user.UserId)).StatusCode);

        Assert.Equal("Deactivated", (await StudentOfAsync(admin, user.UserId)).GetProperty("status").GetString());
    }

    // ---- who may ----------------------------------------------------------------------------------

    [Fact]
    public async Task ATeacher_CanFindAndReadStudents_ButNotChangeThem()
    {
        using var teacher = await StaffClientAsync("InstituteTeacher");
        var (_, student, phone) = await StudentAsync();

        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync("/v1/admin/students")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync($"/v1/admin/students/{student.UserId}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await UpdateAsync(teacher, student.UserId, Profile("Renamed", student.Email, phone))).StatusCode);
    }

    [Fact]
    public async Task AnExamAdmin_CanChangeAStudent()
    {
        using var examAdmin = await StaffClientAsync("ExamAdmin");
        var (_, student, phone) = await StudentAsync();

        var response = await UpdateAsync(examAdmin, student.UserId, Profile("Renamed By Exam Admin", student.Email, phone));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AStudent_CannotListReadOrChangeStudents()
    {
        var (candidate, student, phone) = await StudentAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await candidate.GetAsync("/v1/admin/students")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await candidate.GetAsync($"/v1/admin/students/{student.UserId}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await UpdateAsync(candidate, student.UserId, Profile("Self Service", student.Email, phone))).StatusCode);
    }

    [Fact]
    public async Task WithoutSigningIn_EveryRouteReturns401()
    {
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/admin/students")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/v1/admin/students/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await UpdateAsync(client, id, Profile("x", "x@example.com", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await DeactivateAsync(client, id)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ReactivateAsync(client, id)).StatusCode);
    }

    private static async Task<List<JsonElement>> StudentAuditEntriesAsync(
        HttpClient admin, Guid actorId, Guid studentId, string action = "Identity.StudentProfileChanged")
    {
        var audit = await admin.GetFromJsonAsync<JsonElement>($"/v1/admin/audit-logs?entityType=User&actorUserId={actorId}&pageSize=100");
        return audit.EnumerateArray()
            .Where(e => e.GetProperty("action").GetString() == action
                && e.GetProperty("entityId").GetString() == studentId.ToString())
            .ToList();
    }
}
