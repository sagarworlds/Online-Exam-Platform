using System.Net.Http.Json;
using System.Text.Json;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// With the minors switch on, a candidate under 18 is scored like anyone else and the queue says the gap is closed. The switch-off path is
/// covered in <see cref="ProctoringRiskFlagFlowTests"/>.
/// </summary>
public sealed class ProctoringMinorsScanFlowTests(MinorsScanEnabledApiFactory factory) : IClassFixture<MinorsScanEnabledApiFactory>
{
    [Fact]
    public async Task WithMinorsScanned_AScan_ScoresAnUnder18Attempt_AndTheQueueStatesNoGap()
    {
        using var admin = await factory.AdminClientAsync();
        var examId = await ProctoringRiskFlagFlowTests.PublishedExamAsync(admin);
        using var minor = await ProctoringRiskFlagFlowTests.EnrollMinorAsync(factory, admin, examId);
        await ProctoringRiskFlagFlowTests.SitAndSubmitAsync(minor, examId, departures: 3);

        var summary = await (await admin.PostAsync($"/v1/proctoring/exams/{examId}/risk-scan", content: null)).Content.ReadFromJsonAsync<JsonElement>();
        var queue = await ProctoringRiskFlagFlowTests.QueueAsync(admin, examId, "open");

        Assert.Equal(1, summary.GetProperty("scored").GetInt32());
        Assert.Equal(1, summary.GetProperty("flagged").GetInt32());
        Assert.Equal(0, summary.GetProperty("excludedUnder18").GetInt32());
        Assert.True(queue.GetProperty("minorsScanEnabled").GetBoolean());
        Assert.Equal(0, queue.GetProperty("excludedUnder18Attempts").GetInt32());
        Assert.Equal(1, queue.GetProperty("total").GetInt32());
    }
}
