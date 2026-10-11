using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Encryption;

/// <summary>What the content scan found: how many stored content values there are, and how many are still plaintext.</summary>
/// <param name="ContentValues">The stored content values, across every encrypted column.</param>
/// <param name="PlaintextValues">The values that are not yet ciphertext this key ring opens.</param>
public sealed record ContentEncryptionScan(int ContentValues, int PlaintextValues)
{
    /// <summary>Whether every stored content value is encrypted.</summary>
    public bool IsComplete => PlaintextValues == 0;
}

/// <summary>
/// Encrypts, in place, the content that was stored as plaintext before question content was encrypted (#57), and reports how much of it is
/// still plaintext. It reads and writes the columns with plain SQL, not through the entities, because the entities refuse a plaintext value
/// on read; a row that predates the encryption must be read to be encrypted.
/// </summary>
/// <remarks>
/// It runs from the migrate-and-seed step, before any replica serves questions (ADR 0002), inside one transaction per run. Each value is
/// checked against the key ring before it is touched: a value that already opens is left alone, so a run on an encrypted bank changes
/// nothing. The columns of a text array or of the version options are held as JSON text by the migration that precedes this, so every value
/// here is text, and encrypting it is the same operation for all of them.
/// </remarks>
public sealed class QuestionContentBackfill(QuestionBankDbContext context, QuestionContentCipher cipher, ILogger<QuestionContentBackfill> logger)
{
    /// <summary>The columns that hold content, as the table and column names the schema uses.</summary>
    internal static readonly (string Table, string Column)[] ContentColumns =
    [
        ("Questions", "Text"),
        ("Questions", "SearchText"),
        ("Questions", "AcceptedAnswers"),
        ("QuestionOptions", "Text"),
        ("QuestionVersions", "Text"),
        ("QuestionVersions", "Options"),
        ("QuestionVersions", "AcceptedAnswers"),
    ];

    /// <summary>Encrypts every content value that is still plaintext, and reports how many it changed.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of values encrypted by this run; zero when the bank was already encrypted.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var scan = await ScanAsync(apply: true, cancellationToken);
        if (scan.PlaintextValues > 0)
            logger.LogInformation("Encrypted {Count} question content values that were stored as plaintext.", scan.PlaintextValues);

        return scan.PlaintextValues;
    }

    /// <summary>Counts the content values that are still plaintext, without changing anything. The admin status reads this.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ContentEncryptionScan> ReadStatusAsync(CancellationToken cancellationToken) => ScanAsync(apply: false, cancellationToken);

    private async Task<ContentEncryptionScan> ScanAsync(bool apply, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var wasOpen = connection.State == ConnectionState.Open;
        if (!wasOpen)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var transaction = apply ? await connection.BeginTransactionAsync(cancellationToken) : null;
            var values = 0;
            var plaintext = 0;
            foreach (var (table, column) in ContentColumns)
            {
                var (columnValues, columnPlaintext) = await ScanColumnAsync(connection, transaction, table, column, apply, cancellationToken);
                values += columnValues;
                plaintext += columnPlaintext;
            }

            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            return new ContentEncryptionScan(values, plaintext);
        }
        finally
        {
            if (!wasOpen)
                await connection.CloseAsync();
        }
    }

    // Why the suppression: SQL cannot take a table or column name as a parameter, so the names have to be put in the statement text. They
    // are safe there only because the one caller passes them from ContentColumns, a constant list inside this class, and nothing that a
    // request can change reaches them. The values themselves are passed as parameters. Add a caller that passes anything else and this
    // suppression must be removed.
    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Table and column names come only from the constant ContentColumns list; values are passed as parameters.")]
    private async Task<(int Values, int Plaintext)> ScanColumnAsync(
        DbConnection connection, DbTransaction? transaction, string table, string column, bool apply, CancellationToken cancellationToken)
    {
        // The names come from ContentColumns, which is a constant list, so they are safe to put in the statement.
        var rows = new List<(Guid Id, string Value)>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = $"SELECT \"Id\", \"{column}\" FROM \"questionBank\".\"{table}\" WHERE \"{column}\" IS NOT NULL";
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                rows.Add((reader.GetGuid(0), reader.GetString(1)));
        }

        var plaintext = 0;
        foreach (var (id, value) in rows)
        {
            if (cipher.IsCiphertext(value))
                continue;

            plaintext++;
            if (!apply)
                continue;

            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = $"UPDATE \"questionBank\".\"{table}\" SET \"{column}\" = @value WHERE \"Id\" = @id";
            AddParameter(update, "value", cipher.Encrypt(value));
            AddParameter(update, "id", id);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        return (rows.Count, plaintext);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
