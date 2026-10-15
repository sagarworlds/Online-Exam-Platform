using System.Text;
using ExamPlatform.Modules.Batch.Application;
using ExamPlatform.Modules.Batch.Application.Commands;
using ExamPlatform.Modules.Batch.Application.Dtos;
using ExamPlatform.Modules.Batch.Application.Ports;
using ExamPlatform.Modules.Batch.Domain.Exceptions;
using NSubstitute;
using BatchAggregate = ExamPlatform.Modules.Batch.Domain.Batch;

namespace ExamPlatform.Modules.Batch.UnitTests;

/// <summary>
/// The roster import's request handling (FR-50): the file is read and checked before anything is saved, good rows are
/// saved together, and the report says what happened to every row by its number.
/// </summary>
public class ImportBatchRosterHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

    private readonly IBatchRepository repository = Substitute.For<IBatchRepository>();
    private readonly IBatchUnitOfWork unitOfWork = Substitute.For<IBatchUnitOfWork>();
    private readonly ImportBatchRosterHandler handler;

    public ImportBatchRosterHandlerTests()
    {
        handler = new ImportBatchRosterHandler(repository, unitOfWork, new FakeClock(Now));
    }

    private BatchAggregate GivenBatch(int maxMembers = 10)
    {
        var batch = new BatchAggregate(Guid.NewGuid(), "Batch A", null, maxMembers, Guid.NewGuid(), Now);
        repository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        return batch;
    }

    private Task<ImportBatchRosterResultDto> ImportAsync(BatchAggregate batch, string csv) =>
        handler.HandleAsync(new ImportBatchRosterCommand(batch.Id, csv), CancellationToken.None);

    [Fact]
    public async Task HandleAsync_SeatsTheGoodRows_SavesOnce_AndReportsEveryRow()
    {
        var batch = GivenBatch();

        var result = await ImportAsync(batch, "email,phone\r\namy@example.com,\r\nnot-an-address,\r\nben@example.com,+919876543210\r\n");

        Assert.Equal([2, 4], result.Added.Select(a => a.Row));
        Assert.Equal(batch.Members.Select(m => m.Id), result.Added.Select(a => a.MemberId));
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal(3, skipped.Row);
        Assert.Equal("invalid_email", skipped.Code);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenNoRowIsSeated_SavesNothing()
    {
        var batch = GivenBatch();

        var result = await ImportAsync(batch, "nope\r\nalso nope\r\n");

        Assert.Empty(result.Added);
        Assert.Equal(2, result.Skipped.Count);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ReportsEachKindOfSkippedRow_WithItsCode_AndNeverTheAddress()
    {
        var batch = GivenBatch(maxMembers: 2);
        batch.AddMember("held@example.com", null, Now);

        var result = await ImportAsync(
            batch,
            "email,phone\r\n,+919876543210\r\nbroken,\r\nok@example.com,12\r\nheld@example.com,\r\nnew@example.com,\r\nNEW@example.com,\r\nlate@example.com,\r\n");

        Assert.Equal(
            ["missing_email", "invalid_email", "invalid_phone", "already_in_batch", "repeated_in_file", "batch_full"],
            result.Skipped.Select(s => s.Code));
        Assert.Equal([2, 3, 4, 5, 7, 8], result.Skipped.Select(s => s.Row));
        Assert.Contains("row 6", result.Skipped.Single(s => s.Code == "repeated_in_file").Message);
        Assert.All(result.Skipped, s => Assert.DoesNotContain("@", s.Message));
    }

    [Fact]
    public async Task HandleAsync_ReportsTheColumnsItIgnored()
    {
        var batch = GivenBatch();

        var result = await ImportAsync(batch, "Name,Email\r\nAmy,amy@example.com\r\n");

        Assert.Equal(["Name"], result.IgnoredColumns);
    }

    [Fact]
    public async Task HandleAsync_UnknownBatch_ThrowsBatchNotFoundError_BeforeReadingTheFile()
    {
        // An unknown batch is the first thing wrong with the request, whatever the file holds.
        await Assert.ThrowsAsync<BatchNotFoundError>(() =>
            handler.HandleAsync(new ImportBatchRosterCommand(Guid.NewGuid(), string.Empty), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_AnUnreadableFile_IsRefusedWhole_AndSavesNothing()
    {
        var batch = GivenBatch();

        await Assert.ThrowsAsync<RosterUnreadableError>(() => ImportAsync(batch, "email\r\n"));

        Assert.Empty(batch.Members);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AtTheMaximumNumberOfRows_IsAccepted()
    {
        var batch = GivenBatch(maxMembers: ImportBatchRosterHandler.MaxRows);

        var result = await ImportAsync(batch, Rows(ImportBatchRosterHandler.MaxRows));

        Assert.Equal(ImportBatchRosterHandler.MaxRows, result.Added.Count);
    }

    [Fact]
    public async Task HandleAsync_OverTheMaximumNumberOfRows_IsRefusedWhole_AndSavesNothing()
    {
        var batch = GivenBatch(maxMembers: ImportBatchRosterHandler.MaxRows + 5);

        var error = await Assert.ThrowsAsync<RosterTooLargeError>(() => ImportAsync(batch, Rows(ImportBatchRosterHandler.MaxRows + 1)));

        Assert.Contains(ImportBatchRosterHandler.MaxRows.ToString(), error.Message);
        Assert.Empty(batch.Members);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_TheHeaderAndBlankLines_DoNotCountTowardsTheMaximum()
    {
        var batch = GivenBatch(maxMembers: ImportBatchRosterHandler.MaxRows);
        var csv = "email\r\n\r\n" + Rows(ImportBatchRosterHandler.MaxRows) + "\r\n\r\n";

        var result = await ImportAsync(batch, csv);

        Assert.Equal(ImportBatchRosterHandler.MaxRows, result.Added.Count);
    }

    private static string Rows(int count)
    {
        var csv = new StringBuilder();
        for (var i = 1; i <= count; i++)
            csv.Append($"candidate{i}@example.com\r\n");
        return csv.ToString();
    }
}
