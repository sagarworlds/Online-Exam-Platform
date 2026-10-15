using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// NFR-4 and ADR 0004, through the real API: a save or a clear carries a sequence number, a write whose number is not higher than the one
/// held is refused with <c>409 answer_superseded</c> and the number held, and so an old request that arrives late cannot replace a newer
/// answer or bring back one that was cleared. A client that sends no number keeps working.
/// </summary>
public sealed class AnswerSequenceFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Question(Guid Id, Guid OptionA, Guid OptionB);

    private sealed record Sitting(HttpClient Admin, HttpClient Candidate, Guid AttemptId, Question Question) : IDisposable
    {
        public void Dispose()
        {
            Admin.Dispose();
            Candidate.Dispose();
        }

        public string AnswerUrl => $"/v1/me/attempts/{AttemptId}/answers/{Question.Id}";
    }

    /// <summary>A candidate who has started an attempt at a published exam with one two-option question.</summary>
    private async Task<Sitting> StartedAsync()
    {
        var admin = await factory.AdminClientAsync();
        var created = await admin.PostAsJsonAsync("/v1/questions", new
        {
            text = "Which is it?",
            options = new[] { new { text = "A", isCorrect = true }, new { text = "B", isCorrect = false } },
        });
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var options = body.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("id").GetGuid()).ToList();
        var question = new Question(body.GetProperty("id").GetGuid(), options[0], options[1]);

        var examId = await CreateExamAsync(admin, "Answer Sequence Exam", [question.Id], TimeSpan.FromMinutes(-5), durationMinutes: 60);
        var email = UniqueEmail();
        var invite = await InviteAsync(admin, examId, email);
        var (candidate, _) = await factory.CandidateClientAsync(email);
        (await candidate.PostAsJsonAsync("/v1/invites/accept", new { code = CodeFromLink(invite.GetProperty("inviteLink").GetString()!) })).EnsureSuccessStatusCode();

        var start = await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true });
        start.EnsureSuccessStatusCode();
        var attemptId = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        return new Sitting(admin, candidate, attemptId, question);
    }

    private static Task<HttpResponseMessage> SaveAsync(Sitting sitting, Guid option, long? sequence) =>
        sitting.Candidate.PutAsJsonAsync(sitting.AnswerUrl, new { optionId = option, sequence });

    private static Task<HttpResponseMessage> ClearAsync(Sitting sitting, long? sequence) =>
        sitting.Candidate.DeleteAsync(sequence is null ? sitting.AnswerUrl : $"{sitting.AnswerUrl}?sequence={sequence}");

    /// <summary>What the server holds for the question: the option chosen, or null, and the sequence number.</summary>
    private static async Task<(Guid? Selected, long? Sequence)> HeldAsync(Sitting sitting)
    {
        var attempt = await sitting.Candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{sitting.AttemptId}");
        var question = attempt.GetProperty("sections")[0].GetProperty("questions")[0];
        var selected = question.GetProperty("selectedOptionId");
        var sequence = question.TryGetProperty("answerSequence", out var held) && held.ValueKind == JsonValueKind.Number ? held.GetInt64() : (long?)null;
        return (selected.ValueKind == JsonValueKind.Null ? null : selected.GetGuid(), sequence);
    }

    private static async Task AssertSupersededAsync(HttpResponseMessage response, long heldSequence)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("answer_superseded", problem.GetProperty("title").GetString());
        Assert.Equal(heldSequence, problem.GetProperty("currentSequence").GetInt64());
    }

    [Fact]
    public async Task ASaveWithASequence_IsApplied_AndTheAttemptReportsTheNumberHeld()
    {
        using var sitting = await StartedAsync();

        var response = await SaveAsync(sitting, sitting.Question.OptionA, sequence: 1);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal((sitting.Question.OptionA, 1L), await HeldAsync(sitting));
    }

    [Fact]
    public async Task ALateCopyOfAnOlderSave_IsRefusedWithTheNumberHeld_AndTheNewerAnswerStays()
    {
        using var sitting = await StartedAsync();
        (await SaveAsync(sitting, sitting.Question.OptionA, sequence: 1)).EnsureSuccessStatusCode();
        (await SaveAsync(sitting, sitting.Question.OptionB, sequence: 2)).EnsureSuccessStatusCode();

        // The first request again, as a copy held up in the network and delivered after the second would be.
        var late = await SaveAsync(sitting, sitting.Question.OptionA, sequence: 1);

        await AssertSupersededAsync(late, heldSequence: 2);
        Assert.Equal((sitting.Question.OptionB, 2L), await HeldAsync(sitting));
    }

    [Fact]
    public async Task ARepeatOfASaveThatWasApplied_IsRefusedWithTheNumberHeld_AndSendingItAboveThatNumberApplies()
    {
        using var sitting = await StartedAsync();
        (await SaveAsync(sitting, sitting.Question.OptionA, sequence: 1)).EnsureSuccessStatusCode();

        await AssertSupersededAsync(await SaveAsync(sitting, sitting.Question.OptionA, sequence: 1), heldSequence: 1);
        var again = await SaveAsync(sitting, sitting.Question.OptionA, sequence: 2);

        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal((sitting.Question.OptionA, 2L), await HeldAsync(sitting));
    }

    [Fact]
    public async Task ALateSaveCannotBringBackAClearedAnswer()
    {
        using var sitting = await StartedAsync();
        (await SaveAsync(sitting, sitting.Question.OptionA, sequence: 1)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await ClearAsync(sitting, sequence: 2)).StatusCode);

        await AssertSupersededAsync(await SaveAsync(sitting, sitting.Question.OptionA, sequence: 1), heldSequence: 2);

        // Still cleared, and the number the clear took is still held, though the answer row is gone.
        Assert.Equal((null, 2L), await HeldAsync(sitting));
        Assert.Equal(HttpStatusCode.NoContent, (await SaveAsync(sitting, sitting.Question.OptionB, sequence: 3)).StatusCode);
        Assert.Equal((sitting.Question.OptionB, 3L), await HeldAsync(sitting));
    }

    [Fact]
    public async Task ALateClearCannotRemoveANewerAnswer()
    {
        using var sitting = await StartedAsync();
        (await SaveAsync(sitting, sitting.Question.OptionA, sequence: 2)).EnsureSuccessStatusCode();

        await AssertSupersededAsync(await ClearAsync(sitting, sequence: 1), heldSequence: 2);

        Assert.Equal((sitting.Question.OptionA, 2L), await HeldAsync(sitting));
    }

    [Fact]
    public async Task AClientThatSendsNoSequence_StillWorks_AndTheNumberAdvancesByArrival()
    {
        using var sitting = await StartedAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await SaveAsync(sitting, sitting.Question.OptionA, sequence: null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SaveAsync(sitting, sitting.Question.OptionB, sequence: null)).StatusCode);
        Assert.Equal((sitting.Question.OptionB, 2L), await HeldAsync(sitting));

        // A client that does send numbers has to go above the ones the older client used.
        await AssertSupersededAsync(await SaveAsync(sitting, sitting.Question.OptionA, sequence: 2), heldSequence: 2);
        Assert.Equal(HttpStatusCode.NoContent, (await SaveAsync(sitting, sitting.Question.OptionA, sequence: 3)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ClearAsync(sitting, sequence: null)).StatusCode);
        Assert.Equal((null, 4L), await HeldAsync(sitting));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task ASequenceBelowOne_IsRefusedAsAnInvalidAttempt_AndChangesNothing(long sequence)
    {
        using var sitting = await StartedAsync();

        var response = await SaveAsync(sitting, sitting.Question.OptionA, sequence);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_attempt", problem.GetProperty("title").GetString());
        Assert.Equal((null, null), await HeldAsync(sitting));
    }

    [Fact]
    public async Task TwoWritesToTheSameQuestionAtTheSameMoment_NeverLeaveTheOlderOneStanding()
    {
        using var sitting = await StartedAsync();
        long sequence = 0;

        for (var round = 0; round < 12; round++)
        {
            var older = ++sequence;
            var newer = ++sequence;

            // Both requests are in flight together, so the order the server sees them in is not the order they were numbered in.
            var results = await Task.WhenAll(
                SaveAsync(sitting, sitting.Question.OptionA, older),
                SaveAsync(sitting, sitting.Question.OptionB, newer));

            var applied = new List<long>();
            for (var i = 0; i < results.Length; i++)
            {
                var number = i == 0 ? older : newer;
                if (results[i].StatusCode == HttpStatusCode.NoContent)
                {
                    applied.Add(number);
                    continue;
                }

                // A write that is not applied is refused, not failed: either a newer one got there first, or it lost a race for the
                // same question and the client sends it again.
                Assert.Equal(HttpStatusCode.Conflict, results[i].StatusCode);
                var problem = await results[i].Content.ReadFromJsonAsync<JsonElement>();
                Assert.Contains(problem.GetProperty("title").GetString(), new[] { "answer_superseded", "concurrency_conflict" });
            }

            // Whatever happened, the answer held is the one with the highest number that was applied: the older write never wins.
            Assert.NotEmpty(applied);
            var highest = applied.Max();
            var expectedOption = highest == older ? sitting.Question.OptionA : sitting.Question.OptionB;
            Assert.Equal((expectedOption, highest), await HeldAsync(sitting));

            // If the newer write lost a race, the client sends it again above the number held, as the page does.
            if (highest == older)
                sequence = older;
        }
    }
}
