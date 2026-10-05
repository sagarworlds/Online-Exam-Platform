using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Bulk CSV import and export of questions (FR-6): a bad row is reported and skipped rather than failing the whole
/// file, and a bank's own export is a file it can re-import unchanged.
/// </summary>
public sealed class QuestionBulkImportExportFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Header = "Text,Option1,Correct1,Option2,Correct2,Option3,Correct3,Option4,Correct4,Option5,Correct5,Option6,Correct6,AllowsMultiple,Difficulty,Topics";

    [Fact]
    public async Task Importing_CreatesTheGoodRowsAndReportsTheBadOne()
    {
        using var admin = await factory.AdminClientAsync();
        var csv = Header + "\r\n"
            + "Capital of France?,Paris,true,Rome,false,,,,,,,,,false,easy,geography\r\n"
            + ",Paris,true,Rome,false,,,,,,,,,false,,\r\n"; // blank text, must be rejected

        var response = await admin.PostAsJsonAsync("/v1/questions/import", new { csv });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        var created = result.GetProperty("created").EnumerateArray().ToList();
        var rejected = result.GetProperty("rejected").EnumerateArray().ToList();
        Assert.Equal(1, created.Count);
        Assert.Equal(2, created[0].GetProperty("row").GetInt32());
        Assert.Equal(1, rejected.Count);
        Assert.Equal(3, rejected[0].GetProperty("row").GetInt32());

        var questionId = created[0].GetProperty("id").GetGuid();
        var question = await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{questionId}");
        Assert.Contains("Capital of France?", question.GetProperty("text").GetString());
    }

    [Fact]
    public async Task ExportingThenImporting_RecreatesTheSameQuestion()
    {
        using var admin = await factory.AdminClientAsync();
        await CreateQuestionAsync(admin, "2 + 2?", "4", "5");

        var exportResponse = await admin.GetAsync("/v1/questions/export");
        exportResponse.EnsureSuccessStatusCode();
        var csv = await exportResponse.Content.ReadAsStringAsync();
        Assert.Contains("2 + 2?", csv);

        var importResponse = await admin.PostAsJsonAsync("/v1/questions/import", new { csv });
        importResponse.EnsureSuccessStatusCode();
        var result = await importResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Empty(result.GetProperty("rejected").EnumerateArray());
        Assert.True(result.GetProperty("created").EnumerateArray().Count() >= 1);

        var list = await admin.GetFromJsonAsync<JsonElement>($"/v1/questions?q={Uri.EscapeDataString("2 + 2")}");
        Assert.True(list.EnumerateArray().Count() >= 2);
    }
}
