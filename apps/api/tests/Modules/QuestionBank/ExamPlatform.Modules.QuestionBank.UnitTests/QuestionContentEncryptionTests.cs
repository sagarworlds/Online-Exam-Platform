using System.Security.Cryptography;
using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Infrastructure.Encryption;
using Microsoft.AspNetCore.DataProtection;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>What is stored for question content (#57): encrypted, read back exactly, refused when it is plaintext or has been altered.</summary>
public class QuestionContentEncryptionTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    private static QuestionContentCipher NewCipher() => new(new EphemeralDataProtectionProvider());

    [Fact]
    public void Encrypt_HidesTheTextInTheStoredForm_AndDecryptReturnsItExactly()
    {
        var cipher = NewCipher();

        var stored = cipher.Encrypt("What is 2 + 2?");

        Assert.StartsWith(QuestionContentCipher.Prefix, stored, StringComparison.Ordinal);
        Assert.DoesNotContain("2 + 2", stored, StringComparison.Ordinal);
        Assert.Equal("What is 2 + 2?", cipher.Decrypt(stored));
    }

    [Fact]
    public void Decrypt_RefusesPlaintext_InsteadOfReturningIt()
    {
        var cipher = NewCipher();

        Assert.Throws<ContentNotEncryptedException>(() => cipher.Decrypt("What is 2 + 2?"));
    }

    [Fact]
    public void IsCiphertext_IsFalseForPlaintext_EvenWhenItStartsWithThePrefix()
    {
        var cipher = NewCipher();

        Assert.False(cipher.IsCiphertext("What is 2 + 2?"));
        Assert.False(cipher.IsCiphertext(QuestionContentCipher.Prefix + "not a payload"));
        Assert.True(cipher.IsCiphertext(cipher.Encrypt("What is 2 + 2?")));
    }

    [Fact]
    public void AnAlteredPayload_IsRefused()
    {
        var cipher = NewCipher();
        var stored = cipher.Encrypt("What is 2 + 2?");
        var index = QuestionContentCipher.Prefix.Length + 30;
        var altered = stored[..index] + (stored[index] == 'A' ? 'B' : 'A') + stored[(index + 1)..];

        Assert.Throws<CryptographicException>(() => cipher.Decrypt(altered));
        Assert.False(cipher.IsCiphertext(altered));
    }

    [Fact]
    public void AValueEncryptedWithOtherKeys_IsNotCiphertextHere()
    {
        var stored = NewCipher().Encrypt("What is 2 + 2?");

        Assert.False(NewCipher().IsCiphertext(stored));
    }

    [Fact]
    public void TheAnswerListConverter_RoundTripsEveryAnswerInOrder_AndStoresNoneOfThemPlain()
    {
        var converter = ContentConverters.Strings(NewCipher());
        var answers = new[] { "Paris", "paris, france", "Paris \"capital\"" };

        var stored = (string)converter.ConvertToProviderExpression.Compile()(answers);
        var back = converter.ConvertFromProviderExpression.Compile()(stored);

        Assert.DoesNotContain("paris, france", stored, StringComparison.Ordinal);
        Assert.Equal(answers, back);
    }

    [Fact]
    public void TheVersionOptionsConverter_RoundTripsTheOptionsAVersionHeld()
    {
        var converter = ContentConverters.VersionOptions(NewCipher());
        IReadOnlyList<QuestionVersionOption> options =
        [
            new(Guid.NewGuid(), "Paris", true, 0, false),
            new(Guid.NewGuid(), "Rome", false, 1, true),
        ];

        var stored = (string)converter.ConvertToProviderExpression.Compile()(options);
        var back = converter.ConvertFromProviderExpression.Compile()(stored);

        Assert.DoesNotContain("Rome", stored, StringComparison.Ordinal);
        Assert.Equal(options, back);
    }

    [Fact]
    public void ASearchTerm_IsTrimmed_AndBlankIsNoSearch()
    {
        Assert.Equal("Physics", QuestionTextSearch.TermOf("  Physics "));
        Assert.Null(QuestionTextSearch.TermOf("   "));
        Assert.Null(QuestionTextSearch.TermOf(null));
    }

    [Fact]
    public void ASearch_FindsTheTermInTheStem_IgnoringCase()
    {
        var question = Question.Create("<p>Capital of France?</p>", [new("Paris", true), new("Rome", false)], Guid.NewGuid(), Now);
        question.IndexText("Capital of France?");

        Assert.True(QuestionTextSearch.Matches("capital", question));
        Assert.True(QuestionTextSearch.Matches("FRANCE", question));
    }

    [Fact]
    public void ASearch_FindsTheTermInAnOption()
    {
        var question = Question.Create("<p>Capital of France?</p>", [new("Paris", true), new("Rome", false)], Guid.NewGuid(), Now);
        question.IndexText("Capital of France?");

        Assert.True(QuestionTextSearch.Matches("rom", question));
    }

    [Fact]
    public void ASearch_DoesNotTreatThePercentOrUnderscoreAsWildcards()
    {
        var question = Question.Create("<p>Score 100%?</p>", [new("Yes", true), new("No", false)], Guid.NewGuid(), Now);
        question.IndexText("Score 100%?");

        Assert.True(QuestionTextSearch.Matches("100%", question));
        Assert.False(QuestionTextSearch.Matches("1_0", question));
    }
}
