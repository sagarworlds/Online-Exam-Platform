using System.Text.Json;
using ExamPlatform.Modules.QuestionBank.Domain;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Encryption;

/// <summary>
/// The EF Core converters that store question content encrypted. Each converter encrypts on write and decrypts on read, so the rest of the
/// bank keeps working with plain strings, and nothing outside the bank's persistence sees ciphertext.
/// </summary>
/// <remarks>
/// Only content is converted. Columns used to find things (the text key that finds duplicates, the topics, the status) stay plain, because a
/// query has to match them in the database.
/// </remarks>
public static class ContentConverters
{
    /// <summary>A text value, such as a stem, an option or a search text.</summary>
    /// <param name="cipher">The cipher that holds the keys.</param>
    public static ValueConverter<string, string> Text(QuestionContentCipher cipher) =>
        new(plain => cipher.Encrypt(plain), stored => cipher.Decrypt(stored));

    /// <summary>A list of answers, held as JSON and then encrypted, so the list keeps its order and any character in an answer.</summary>
    /// <param name="cipher">The cipher that holds the keys.</param>
    public static ValueConverter<string[], string> Strings(QuestionContentCipher cipher) => new(
        values => cipher.Encrypt(JsonSerializer.Serialize(values, (JsonSerializerOptions?)null)),
        stored => JsonSerializer.Deserialize<string[]>(cipher.Decrypt(stored), (JsonSerializerOptions?)null) ?? Array.Empty<string>());

    /// <summary>The options a version held, as JSON and then encrypted.</summary>
    /// <param name="cipher">The cipher that holds the keys.</param>
    public static ValueConverter<IReadOnlyList<QuestionVersionOption>, string> VersionOptions(QuestionContentCipher cipher) => new(
        options => cipher.Encrypt(JsonSerializer.Serialize(options, (JsonSerializerOptions?)null)),
        stored => JsonSerializer.Deserialize<List<QuestionVersionOption>>(cipher.Decrypt(stored), (JsonSerializerOptions?)null) ?? new());

    /// <summary>Compares two answer lists by their values, so EF notices an edit and does not rewrite an unchanged list.</summary>
    public static ValueComparer<string[]> StringsComparer() => new(
        (a, b) => (a ?? Array.Empty<string>()).SequenceEqual(b ?? Array.Empty<string>()),
        values => values.Aggregate(0, (hash, value) => HashCode.Combine(hash, value)),
        values => values.ToArray());

    /// <summary>Compares two version option lists by their values, as <see cref="StringsComparer"/> does.</summary>
    public static ValueComparer<IReadOnlyList<QuestionVersionOption>> VersionOptionsComparer() => new(
        (a, b) => (a ?? new List<QuestionVersionOption>()).SequenceEqual(b ?? new List<QuestionVersionOption>()),
        options => options.Aggregate(0, (hash, o) => HashCode.Combine(hash, o)),
        options => options.ToList());
}
