using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Finding an exam's candidates by phone number from the staff page, over real HTTP and a real database: the numbers are matched by their
/// digits whatever way they were written, only candidates of that exam can be found, and no number is ever in an answer.
/// </summary>
public sealed class CandidatePhoneSearchFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    // Each test registers numbers of its own: one account holds a number, and the tests of a class share a database.

    private async Task<(Guid ExamId, HttpClient Admin)> ExamAsync()
    {
        var admin = await factory.AdminClientAsync();
        var question = await CreateQuestionAsync(admin, "Phone search question", "Right", "Wrong");
        var examId = await CreateExamAsync(admin, "Phone search exam", [question], TimeSpan.FromMinutes(-5));
        return (examId, admin);
    }

    /// <summary>Invites a new candidate who registered with the given number, has them accept, and returns their account id.</summary>
    private async Task<Guid> EnrollWithNumberAsync(HttpClient admin, Guid examId, string phoneNumber)
    {
        var email = UniqueEmail();
        var invite = await InviteAsync(admin, examId, email);
        var (candidate, user) = await factory.CandidateClientAsync(email, phoneNumber);
        using var _c = candidate;
        (await candidate.PostAsJsonAsync("/v1/invites/accept", new { code = CodeFromLink(invite.GetProperty("inviteLink").GetString()!) })).EnsureSuccessStatusCode();
        return user.UserId;
    }

    private static async Task<HttpResponseMessage> SearchAsync(HttpClient client, Guid examId, string? phone) =>
        await client.PostAsJsonAsync($"/v1/exams/{examId}/candidates/phone-search", new { phone });

    private static async Task<Guid[]> FoundAsync(HttpClient admin, Guid examId, string phone)
    {
        var response = await SearchAsync(admin, examId, phone);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("candidateIds").EnumerateArray().Select(id => id.GetGuid()).ToArray();
    }

    [Fact]
    public async Task ANumber_FindsTheCandidate_WhateverWayItWasWritten()
    {
        var (examId, admin) = await ExamAsync();
        using var _a = admin;
        const string number = "9111122222";
        const string spaced = "+91 91111-22222";
        var candidate = await EnrollWithNumberAsync(admin, examId, spaced);
        await EnrollWithNumberAsync(admin, examId, "9333344444");

        Assert.Equal([candidate], await FoundAsync(admin, examId, number)); // digits only
        Assert.Equal([candidate], await FoundAsync(admin, examId, spaced)); // the way it was entered
        Assert.Equal([candidate], await FoundAsync(admin, examId, "091111 22222")); // with a trunk 0
        Assert.Equal([candidate], await FoundAsync(admin, examId, "11122")); // a part of it
    }

    [Fact]
    public async Task ASearchThatIsNotANumber_OrMatchesNoOne_FindsNoOne()
    {
        var (examId, admin) = await ExamAsync();
        using var _a = admin;
        await EnrollWithNumberAsync(admin, examId, "9000000001");

        Assert.Empty(await FoundAsync(admin, examId, "9000000002"));
        Assert.Empty(await FoundAsync(admin, examId, "90")); // too few digits
        Assert.Empty(await FoundAsync(admin, examId, "someone@example.com"));
        Assert.Empty(await FoundAsync(admin, examId, "   "));
    }

    [Fact]
    public async Task OnlyTheCandidatesOfThatExam_CanBeFound()
    {
        var (examId, admin) = await ExamAsync();
        using var _a = admin;
        var (otherExamId, _) = await ExamAsync();
        await EnrollWithNumberAsync(admin, otherExamId, "9500000005");

        Assert.Empty(await FoundAsync(admin, examId, "9500000005"));
    }

    [Fact]
    public async Task TheAnswer_HoldsIdsAndNoNumber()
    {
        var (examId, admin) = await ExamAsync();
        using var _a = admin;
        const string number = "9400000004";
        await EnrollWithNumberAsync(admin, examId, number);

        var response = await SearchAsync(admin, examId, number);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("candidateIds", body);
        Assert.DoesNotContain(number, body);
        Assert.Equal(["candidateIds"], JsonDocument.Parse(body).RootElement.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task OnlyStaffWhoManageAttempts_MaySearch_AndTheExamMustExist()
    {
        var (examId, admin) = await ExamAsync();
        using var _a = admin;
        var (candidate, _) = await factory.CandidateClientAsync();
        using var _c = candidate;
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SearchAsync(anonymous, examId, "9000000001")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SearchAsync(candidate, examId, "9000000001")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SearchAsync(admin, Guid.NewGuid(), "9000000001")).StatusCode);
    }
}
