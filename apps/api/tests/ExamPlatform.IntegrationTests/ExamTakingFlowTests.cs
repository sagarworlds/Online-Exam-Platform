using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The candidate's side of the loop (FR-16 to FR-21), driven through the real API: an invited candidate starts an
/// exam, answers, submits and reads a score, and nobody else can get at their attempt.
/// </summary>
public class ExamTakingFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Question(Guid Id, Guid CorrectOptionId, Guid WrongOptionId);

    private static async Task<Question> CreateTwoOptionQuestionAsync(HttpClient admin, string text)
    {
        var response = await admin.PostAsJsonAsync("/v1/questions", new
        {
            text,
            options = new[] { new { text = "Right", isCorrect = true }, new { text = "Wrong", isCorrect = false } },
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var options = body.GetProperty("options").EnumerateArray().ToList();
        return new Question(
            body.GetProperty("id").GetGuid(),
            options.Single(o => o.GetProperty("isCorrect").GetBoolean()).GetProperty("id").GetGuid(),
            options.Single(o => !o.GetProperty("isCorrect").GetBoolean()).GetProperty("id").GetGuid());
    }

    /// <summary>An administrator, a published exam with the given questions, and a candidate who has accepted their invitation to it.</summary>
    private async Task<(HttpClient Admin, HttpClient Candidate, Guid ExamId, IReadOnlyList<Question> Questions)> EnrolledCandidateAsync(
        int questionCount = 2, TimeSpan? startsIn = null, int? durationMinutes = 60, string? firstQuestionText = null)
    {
        var admin = await factory.AdminClientAsync();
        var questions = new List<Question>();
        for (var i = 1; i <= questionCount; i++)
            questions.Add(await CreateTwoOptionQuestionAsync(admin, i == 1 && firstQuestionText is not null ? firstQuestionText : $"Question {i}?"));

        var examId = await CreateExamAsync(
            admin, "Taking Flow Exam", questions.Select(q => q.Id).ToList(), startsIn ?? TimeSpan.FromMinutes(-5), durationMinutes: durationMinutes);

        var email = UniqueEmail();
        var invite = await InviteAsync(admin, examId, email);
        var (candidate, _) = await factory.CandidateClientAsync(email);
        (await candidate.PostAsJsonAsync("/v1/invites/accept", new { code = CodeFromLink(invite.GetProperty("inviteLink").GetString()!) })).EnsureSuccessStatusCode();

        return (admin, candidate, examId, questions);
    }

    private static async Task<JsonElement> StartAsync(HttpClient candidate, Guid examId)
    {
        var response = await candidate.PostAsync($"/v1/me/exams/{examId}/attempts", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("title").GetString());
    }

    private const string TinyPng =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    [Fact]
    public async Task TheCandidate_SeesTheFormattedQuestion_WithNothingExecutableAndNoAnswerKey()
    {
        var (admin, candidate, examId, _) = await EnrolledCandidateAsync(
            questionCount: 1, firstQuestionText: $"<p>Pick <strong>one</strong><script>alert(2)</script></p><p><img src=\"{TinyPng}\" onerror=\"alert(1)\"></p>");
        using var _a = admin;
        using var _c = candidate;

        var attempt = await StartAsync(candidate, examId);

        var question = attempt.GetProperty("sections")[0].GetProperty("questions")[0];
        Assert.Equal($"<p>Pick <strong>one</strong></p><p><img src=\"{TinyPng}\"></p>", question.GetProperty("text").GetString());
        Assert.DoesNotContain("isCorrect", attempt.GetRawText());
    }

    [Fact]
    public async Task ACandidate_CanStartAnswerSubmitAndReadTheirScore()
    {
        var (admin, candidate, examId, questions) = await EnrolledCandidateAsync();
        using var _ = admin;
        using var __ = candidate;

        var attempt = await StartAsync(candidate, examId);
        var attemptId = attempt.GetProperty("id").GetGuid();
        Assert.Equal("InProgress", attempt.GetProperty("status").GetString());
        var shown = attempt.GetProperty("sections")[0].GetProperty("questions").EnumerateArray().ToList();
        Assert.Equal(["Question 1?", "Question 2?"], shown.Select(q => q.GetProperty("text").GetString()));

        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{questions[0].Id}", new { optionId = questions[0].CorrectOptionId })).EnsureSuccessStatusCode();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{questions[1].Id}", new { optionId = questions[1].WrongOptionId })).EnsureSuccessStatusCode();
        // Changing one's mind replaces the earlier choice.
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{questions[1].Id}", new { optionId = questions[1].CorrectOptionId })).EnsureSuccessStatusCode();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{questions[1].Id}", new { optionId = questions[1].WrongOptionId })).EnsureSuccessStatusCode();

        var resumed = await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}");
        var saved = resumed.GetProperty("sections")[0].GetProperty("questions").EnumerateArray()
            .Select(q => q.GetProperty("selectedOptionId").GetGuid()).ToList();
        Assert.Equal([questions[0].CorrectOptionId, questions[1].WrongOptionId], saved);

        var result = await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null);
        result.EnsureSuccessStatusCode();
        var submitted = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Submitted", submitted.GetProperty("status").GetString());
        Assert.Equal(1m, submitted.GetProperty("score").GetDecimal());
        Assert.Equal(2m, submitted.GetProperty("maxScore").GetDecimal());
        Assert.False(submitted.GetProperty("autoSubmitted").GetBoolean());
        Assert.Empty(submitted.GetProperty("sections").EnumerateArray());

        // The list now carries the result, and starting again hands back the finished attempt rather than a new one.
        var exam = (await candidate.GetFromJsonAsync<JsonElement>("/v1/me/exams")).EnumerateArray().Single(e => e.GetProperty("examId").GetGuid() == examId);
        Assert.Equal("Submitted", exam.GetProperty("attemptStatus").GetString());
        Assert.Equal(1m, exam.GetProperty("score").GetDecimal());
        var again = await StartAsync(candidate, examId);
        Assert.Equal(attemptId, again.GetProperty("id").GetGuid());
        Assert.Equal("Submitted", again.GetProperty("status").GetString());
    }

    [Fact]
    public async Task WhatACandidateIsShown_NeverRevealsWhichOptionIsCorrect()
    {
        var (admin, candidate, examId, questions) = await EnrolledCandidateAsync(questionCount: 1);
        using var _ = admin;
        using var __ = candidate;

        var started = await candidate.PostAsync($"/v1/me/exams/{examId}/attempts", content: null);
        var raw = await started.Content.ReadAsStringAsync();

        Assert.DoesNotContain("isCorrect", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correct", raw, StringComparison.OrdinalIgnoreCase);
        // The two options are there, only the flag that tells them apart is not.
        Assert.Contains(questions[0].CorrectOptionId.ToString(), raw);
        Assert.Contains(questions[0].WrongOptionId.ToString(), raw);
    }

    [Fact]
    public async Task StartingTwice_ResumesTheSameAttempt_WithItsSavedAnswers()
    {
        var (admin, candidate, examId, questions) = await EnrolledCandidateAsync();
        using var _ = admin;
        using var __ = candidate;
        var first = await StartAsync(candidate, examId);
        var attemptId = first.GetProperty("id").GetGuid();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{questions[0].Id}", new { optionId = questions[0].CorrectOptionId })).EnsureSuccessStatusCode();

        var second = await StartAsync(candidate, examId);

        Assert.Equal(attemptId, second.GetProperty("id").GetGuid());
        // The first answer carries the in-memory value, the second the stored one; Postgres keeps microseconds, .NET ticks are finer.
        Assert.InRange(
            Math.Abs((first.GetProperty("deadlineUtc").GetDateTime() - second.GetProperty("deadlineUtc").GetDateTime()).TotalMilliseconds), 0, 1);
        Assert.Equal(questions[0].CorrectOptionId, second.GetProperty("sections")[0].GetProperty("questions")[0].GetProperty("selectedOptionId").GetGuid());
    }

    [Fact]
    public async Task TheDeadline_IsTheExamDurationFromStart_AndTheServerClockIsReported()
    {
        var (admin, candidate, examId, _) = await EnrolledCandidateAsync(durationMinutes: 20);
        using var _a = admin;
        using var _c = candidate;

        var attempt = await StartAsync(candidate, examId);

        var started = attempt.GetProperty("startedAtUtc").GetDateTime();
        Assert.Equal(TimeSpan.FromMinutes(20), attempt.GetProperty("deadlineUtc").GetDateTime() - started);
        Assert.InRange(Math.Abs((attempt.GetProperty("serverTimeUtc").GetDateTime() - DateTime.UtcNow).TotalSeconds), 0, 60);
    }

    [Fact]
    public async Task SubmittingTwice_ReturnsTheSameResult()
    {
        var (admin, candidate, examId, questions) = await EnrolledCandidateAsync();
        using var _ = admin;
        using var __ = candidate;
        var attemptId = (await StartAsync(candidate, examId)).GetProperty("id").GetGuid();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{questions[0].Id}", new { optionId = questions[0].CorrectOptionId })).EnsureSuccessStatusCode();
        (await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode();

        var second = await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null);

        second.EnsureSuccessStatusCode();
        Assert.Equal(1m, (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("score").GetDecimal());
    }

    [Fact]
    public async Task AfterSubmitting_AnswersAreRefused()
    {
        var (admin, candidate, examId, questions) = await EnrolledCandidateAsync();
        using var _ = admin;
        using var __ = candidate;
        var attemptId = (await StartAsync(candidate, examId)).GetProperty("id").GetGuid();
        (await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode();

        var late = await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{questions[0].Id}", new { optionId = questions[0].CorrectOptionId });

        await AssertProblemAsync(late, HttpStatusCode.Conflict, "attempt_not_in_progress");
    }

    private static IEnumerable<JsonElement> QuestionsOf(JsonElement attempt) => attempt.GetProperty("sections")[0].GetProperty("questions").EnumerateArray();

    [Fact]
    public async Task ACandidate_CanClearAResponse_AndANewAnswerCanBeGivenAfterwards()
    {
        var (admin, candidate, examId, questions) = await EnrolledCandidateAsync();
        using var _ = admin;
        using var __ = candidate;
        var attemptId = (await StartAsync(candidate, examId)).GetProperty("id").GetGuid();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{questions[0].Id}", new { optionId = questions[0].CorrectOptionId })).EnsureSuccessStatusCode();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{questions[1].Id}", new { optionId = questions[1].CorrectOptionId })).EnsureSuccessStatusCode();

        var cleared = await candidate.DeleteAsync($"/v1/me/attempts/{attemptId}/answers/{questions[0].Id}");
        // Asking again, for a question with nothing saved, is the same request and gets the same answer.
        var again = await candidate.DeleteAsync($"/v1/me/attempts/{attemptId}/answers/{questions[0].Id}");

        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        var resumed = QuestionsOf(await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}")).ToList();
        Assert.Equal(JsonValueKind.Null, resumed[0].GetProperty("selectedOptionId").ValueKind);
        Assert.Equal(questions[1].CorrectOptionId, resumed[1].GetProperty("selectedOptionId").GetGuid());

        // A cleared question is unanswered: it earns the unattempted marks (none by default), not the right answer it once had.
        var submitted = await (await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1m, submitted.GetProperty("score").GetDecimal());
    }

    [Fact]
    public async Task ACandidate_CanMarkAndUnmarkQuestionsForReview_AndTheMarksSurviveAResume()
    {
        var (admin, candidate, examId, questions) = await EnrolledCandidateAsync();
        using var _ = admin;
        using var __ = candidate;
        var attemptId = (await StartAsync(candidate, examId)).GetProperty("id").GetGuid();
        Assert.All(QuestionsOf(await StartAsync(candidate, examId)), q => Assert.False(q.GetProperty("markedForReview").GetBoolean()));

        var first = await candidate.PutAsync($"/v1/me/attempts/{attemptId}/marks/{questions[0].Id}", content: null);
        // Marking a marked question is not an error, so a retry after a dropped connection is safe.
        var repeat = await candidate.PutAsync($"/v1/me/attempts/{attemptId}/marks/{questions[0].Id}", content: null);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeat.StatusCode);
        var resumed = QuestionsOf(await StartAsync(candidate, examId)).ToList();
        Assert.True(resumed[0].GetProperty("markedForReview").GetBoolean());
        Assert.False(resumed[1].GetProperty("markedForReview").GetBoolean());

        (await candidate.DeleteAsync($"/v1/me/attempts/{attemptId}/marks/{questions[0].Id}")).EnsureSuccessStatusCode();
        // Unmarking an unmarked question changes nothing and is not an error either.
        Assert.Equal(HttpStatusCode.NoContent, (await candidate.DeleteAsync($"/v1/me/attempts/{attemptId}/marks/{questions[1].Id}")).StatusCode);
        Assert.All(QuestionsOf(await StartAsync(candidate, examId)), q => Assert.False(q.GetProperty("markedForReview").GetBoolean()));
    }

    [Fact]
    public async Task MarkingAQuestion_NeitherChangesItsAnswerNorTheScore()
    {
        var (admin, candidate, examId, questions) = await EnrolledCandidateAsync();
        using var _ = admin;
        using var __ = candidate;
        var attemptId = (await StartAsync(candidate, examId)).GetProperty("id").GetGuid();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{questions[0].Id}", new { optionId = questions[0].CorrectOptionId })).EnsureSuccessStatusCode();

        // Answered and marked, and marked without an answer: both are ordinary states, and only the answer is scored.
        (await candidate.PutAsync($"/v1/me/attempts/{attemptId}/marks/{questions[0].Id}", content: null)).EnsureSuccessStatusCode();
        (await candidate.PutAsync($"/v1/me/attempts/{attemptId}/marks/{questions[1].Id}", content: null)).EnsureSuccessStatusCode();

        var resumed = QuestionsOf(await candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}")).ToList();
        Assert.Equal(questions[0].CorrectOptionId, resumed[0].GetProperty("selectedOptionId").GetGuid());
        Assert.True(resumed[0].GetProperty("markedForReview").GetBoolean());
        Assert.Equal(JsonValueKind.Null, resumed[1].GetProperty("selectedOptionId").ValueKind);
        Assert.True(resumed[1].GetProperty("markedForReview").GetBoolean());
        var submitted = await (await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1m, submitted.GetProperty("score").GetDecimal());
    }

    [Fact]
    public async Task AfterSubmitting_ClearingAndMarkingAreRefused_AndTheScoredAnswerStays()
    {
        var (admin, candidate, examId, questions) = await EnrolledCandidateAsync();
        using var _ = admin;
        using var __ = candidate;
        var attemptId = (await StartAsync(candidate, examId)).GetProperty("id").GetGuid();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{questions[0].Id}", new { optionId = questions[0].CorrectOptionId })).EnsureSuccessStatusCode();
        (await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).EnsureSuccessStatusCode();

        await AssertProblemAsync(
            await candidate.DeleteAsync($"/v1/me/attempts/{attemptId}/answers/{questions[0].Id}"), HttpStatusCode.Conflict, "attempt_not_in_progress");
        await AssertProblemAsync(
            await candidate.PutAsync($"/v1/me/attempts/{attemptId}/marks/{questions[0].Id}", content: null), HttpStatusCode.Conflict, "attempt_not_in_progress");
        await AssertProblemAsync(
            await candidate.DeleteAsync($"/v1/me/attempts/{attemptId}/marks/{questions[0].Id}"), HttpStatusCode.Conflict, "attempt_not_in_progress");

        var again = await (await candidate.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1m, again.GetProperty("score").GetDecimal());
    }

    [Fact]
    public async Task AQuestionThatIsNotInTheExam_CannotBeClearedOrMarked()
    {
        var (admin, candidate, examId, _) = await EnrolledCandidateAsync();
        using var _a = admin;
        using var _c = candidate;
        var attemptId = (await StartAsync(candidate, examId)).GetProperty("id").GetGuid();
        var outsider = await CreateTwoOptionQuestionAsync(admin, "Not in the exam?");

        await AssertProblemAsync(
            await candidate.DeleteAsync($"/v1/me/attempts/{attemptId}/answers/{outsider.Id}"), HttpStatusCode.NotFound, "question_not_in_attempt");
        await AssertProblemAsync(
            await candidate.PutAsync($"/v1/me/attempts/{attemptId}/marks/{outsider.Id}", content: null), HttpStatusCode.NotFound, "question_not_in_attempt");
        await AssertProblemAsync(
            await candidate.DeleteAsync($"/v1/me/attempts/{attemptId}/marks/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "question_not_in_attempt");
    }

    [Fact]
    public async Task AnotherCandidate_CannotClearOrMarkSomeoneElsesAttempt()
    {
        var (admin, owner, examId, questions) = await EnrolledCandidateAsync();
        using var _a = admin;
        using var _o = owner;
        var attemptId = (await StartAsync(owner, examId)).GetProperty("id").GetGuid();
        (await owner.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{questions[0].Id}", new { optionId = questions[0].CorrectOptionId })).EnsureSuccessStatusCode();
        var (stranger, _) = await factory.CandidateClientAsync(UniqueEmail());
        using var _s = stranger;

        await AssertProblemAsync(
            await stranger.DeleteAsync($"/v1/me/attempts/{attemptId}/answers/{questions[0].Id}"), HttpStatusCode.NotFound, "attempt_not_found");
        await AssertProblemAsync(
            await stranger.PutAsync($"/v1/me/attempts/{attemptId}/marks/{questions[0].Id}", content: null), HttpStatusCode.NotFound, "attempt_not_found");
        await AssertProblemAsync(
            await stranger.DeleteAsync($"/v1/me/attempts/{attemptId}/marks/{questions[0].Id}"), HttpStatusCode.NotFound, "attempt_not_found");

        // The owner's answer is untouched by all of that.
        var mine = QuestionsOf(await owner.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}")).First();
        Assert.Equal(questions[0].CorrectOptionId, mine.GetProperty("selectedOptionId").GetGuid());
        Assert.False(mine.GetProperty("markedForReview").GetBoolean());
    }

    [Fact]
    public async Task AnAnswerThatDoesNotBelongToTheExam_IsRefused()
    {
        var (admin, candidate, examId, questions) = await EnrolledCandidateAsync();
        using var _ = admin;
        using var __ = candidate;
        var attemptId = (await StartAsync(candidate, examId)).GetProperty("id").GetGuid();
        var outsider = await CreateTwoOptionQuestionAsync(admin, "Not in the exam?");

        await AssertProblemAsync(
            await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{outsider.Id}", new { optionId = outsider.CorrectOptionId }),
            HttpStatusCode.BadRequest, "invalid_answer");
        await AssertProblemAsync(
            await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{questions[0].Id}", new { optionId = outsider.CorrectOptionId }),
            HttpStatusCode.BadRequest, "invalid_answer");
        await AssertProblemAsync(
            await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{questions[0].Id}", new { optionId = Guid.NewGuid() }),
            HttpStatusCode.BadRequest, "invalid_answer");
    }

    [Fact]
    public async Task ACandidateWhoWasNotInvited_CannotStartTheExam()
    {
        var (admin, invited, examId, _) = await EnrolledCandidateAsync();
        using var _a = admin;
        using var _i = invited;
        var (stranger, _) = await factory.CandidateClientAsync(UniqueEmail());
        using var _s = stranger;

        var response = await stranger.PostAsync($"/v1/me/exams/{examId}/attempts", content: null);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "exam_not_available");
    }

    [Fact]
    public async Task AnotherCandidate_CannotReadAnswerOrSubmitSomeoneElsesAttempt()
    {
        var (admin, owner, examId, questions) = await EnrolledCandidateAsync();
        using var _a = admin;
        using var _o = owner;
        var attemptId = (await StartAsync(owner, examId)).GetProperty("id").GetGuid();
        var (stranger, _) = await factory.CandidateClientAsync(UniqueEmail());
        using var _s = stranger;

        await AssertProblemAsync(await stranger.GetAsync($"/v1/me/attempts/{attemptId}"), HttpStatusCode.NotFound, "attempt_not_found");
        await AssertProblemAsync(
            await stranger.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{questions[0].Id}", new { optionId = questions[0].CorrectOptionId }),
            HttpStatusCode.NotFound, "attempt_not_found");
        await AssertProblemAsync(await stranger.PostAsync($"/v1/me/attempts/{attemptId}/submit", content: null), HttpStatusCode.NotFound, "attempt_not_found");

        // And the owner's attempt is untouched by all of that.
        var mine = await owner.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{attemptId}");
        Assert.Equal("InProgress", mine.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AnExamThatHasNotOpened_CannotBeStarted()
    {
        var (admin, candidate, examId, _) = await EnrolledCandidateAsync(startsIn: TimeSpan.FromHours(1));
        using var _a = admin;
        using var _c = candidate;

        var response = await candidate.PostAsync($"/v1/me/exams/{examId}/attempts", content: null);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "exam_not_open");
    }

    [Fact]
    public async Task AdministratorsAreNotExemptFromEnrolment()
    {
        // The staff permissions are for building exams; sitting one is decided by the invitation alone.
        var (admin, candidate, examId, _) = await EnrolledCandidateAsync();
        using var _a = admin;
        using var _c = candidate;

        var response = await admin.PostAsync($"/v1/me/exams/{examId}/attempts", content: null);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "exam_not_available");
    }

    [Theory]
    [InlineData("POST", "/v1/me/exams/{id}/attempts")]
    [InlineData("GET", "/v1/me/attempts/{id}")]
    [InlineData("GET", "/v1/me/attempts/{id}/review")]
    [InlineData("PUT", "/v1/me/attempts/{id}/answers/{id}")]
    [InlineData("DELETE", "/v1/me/attempts/{id}/answers/{id}")]
    [InlineData("PUT", "/v1/me/attempts/{id}/marks/{id}")]
    [InlineData("DELETE", "/v1/me/attempts/{id}/marks/{id}")]
    [InlineData("PUT", "/v1/me/attempts/{id}/section/{id}")]
    [InlineData("POST", "/v1/me/attempts/{id}/submit")]
    public async Task EveryAttemptRoute_WithoutAuth_Returns401(string method, string pattern)
    {
        using var client = factory.CreateClient();
        var path = pattern.Replace("{id}", Guid.NewGuid().ToString());
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "PUT")
            request.Content = JsonContent.Create(new { optionId = Guid.NewGuid() });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void EveryExamRuntimeRoute_RequiresASignedInCaller()
    {
        var routes = EndpointAuthorizationInspector.ListRoutes(factory.Services)
            .Where(r => r.Pattern.StartsWith("/v1/me/exams", StringComparison.Ordinal) || r.Pattern.StartsWith("/v1/me/attempts", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(11, routes.Count);
        Assert.All(routes, r => Assert.True(r.RequiresAuthorization, $"{r.Key} must require a signed-in caller."));
    }
}
