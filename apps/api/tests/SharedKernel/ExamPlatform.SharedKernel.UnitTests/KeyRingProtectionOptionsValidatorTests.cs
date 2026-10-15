using System.Security.Cryptography;
using ExamPlatform.SharedKernel.Infrastructure.KeyRing;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>
/// The startup check for the key that protects the Data Protection key ring (NFR-5, #86). Outside Development a missing or malformed key
/// stops the host and names the setting; Development may leave it out. No message ever contains a key.
/// </summary>
public class KeyRingProtectionOptionsValidatorTests
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyRingProtectionOptions.KeySizeBytes));

    private static KeyRingProtectionOptionsValidator Validator(string environment) => new(new FakeHostEnvironment(environment));

    [Fact]
    public void Development_MayLeaveTheKeyOut()
    {
        var result = Validator("Development").Validate(null, new KeyRingProtectionOptions());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void EveryOtherEnvironment_IsRefusedWithoutAKey_WithAnAccountOfWhatToSet(string environment)
    {
        var result = Validator(environment).Validate(null, new KeyRingProtectionOptions());

        Assert.False(result.Succeeded);
        Assert.Contains(KeyRingProtectionOptions.KeySettingName, result.FailureMessage);
        Assert.Contains("openssl rand -base64 32", result.FailureMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankKey_IsTheSameAsNoKey(string blank)
    {
        var result = Validator("Production").Validate(null, new KeyRingProtectionOptions { KeyEncryptionKey = blank });

        Assert.False(result.Succeeded);
        Assert.Contains(KeyRingProtectionOptions.KeySettingName, result.FailureMessage);
    }

    [Fact]
    public void AValidKey_Succeeds_InAnyEnvironment()
    {
        Assert.True(Validator("Production").Validate(null, new KeyRingProtectionOptions { KeyEncryptionKey = NewKey() }).Succeeded);
        Assert.True(Validator("Development").Validate(null, new KeyRingProtectionOptions { KeyEncryptionKey = NewKey() }).Succeeded);
    }

    [Theory]
    [InlineData("this is not base64 at all!!")]
    [InlineData("AAAA")]
    public void AKeyThatIsNotAKey_IsRefused_WithoutRepeatingIt(string text)
    {
        var result = Validator("Production").Validate(null, new KeyRingProtectionOptions { KeyEncryptionKey = text });

        Assert.False(result.Succeeded);
        Assert.Contains(KeyRingProtectionOptions.KeySettingName, result.FailureMessage);
        Assert.DoesNotContain(text, result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public void AKeyOfTheWrongLength_IsRefused(int length)
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(length));

        var result = Validator("Production").Validate(null, new KeyRingProtectionOptions { KeyEncryptionKey = key });

        Assert.False(result.Succeeded);
        Assert.Contains($"{KeyRingProtectionOptions.KeySizeBytes} bytes", result.FailureMessage);
        Assert.DoesNotContain(key, result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TheUrlSafeAlphabet_AndMissingPadding_AreAcceptedAsTheSameKey()
    {
        // A host's secret generator may write base64 either way. The bytes are what is the key, so the form is not a reason to refuse a deployment.
        var bytes = new byte[KeyRingProtectionOptions.KeySizeBytes];
        Array.Fill(bytes, (byte)0xFB);
        var standard = Convert.ToBase64String(bytes);
        var urlSafe = standard.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.NotEqual(standard, urlSafe);

        Assert.True(Validator("Production").Validate(null, new KeyRingProtectionOptions { KeyEncryptionKey = urlSafe }).Succeeded);
    }

    [Fact]
    public void RetiredKeys_AreAcceptedAlongsideTheCurrentOne_AndBlankEntriesAreIgnored()
    {
        var options = new KeyRingProtectionOptions { KeyEncryptionKey = NewKey(), PreviousKeyEncryptionKeys = [NewKey(), "", NewKey()] };

        Assert.True(Validator("Production").Validate(null, options).Succeeded);
    }

    [Fact]
    public void ARetiredKeyThatIsNotAKey_IsRefused_NamingItsPosition()
    {
        var options = new KeyRingProtectionOptions { KeyEncryptionKey = NewKey(), PreviousKeyEncryptionKeys = [NewKey(), "not a key"] };

        var result = Validator("Production").Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains($"{KeyRingProtectionOptions.PreviousKeysSettingName}:1", result.FailureMessage);
        Assert.DoesNotContain("not a key", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ARetiredKeyThatRepeatsAnotherKey_IsRefused()
    {
        var key = NewKey();

        var asCurrent = Validator("Production").Validate(null, new KeyRingProtectionOptions { KeyEncryptionKey = key, PreviousKeyEncryptionKeys = [key] });
        var twice = Validator("Production").Validate(null, new KeyRingProtectionOptions
        {
            KeyEncryptionKey = NewKey(),
            PreviousKeyEncryptionKeys = [key, key],
        });

        Assert.False(asCurrent.Succeeded);
        Assert.False(twice.Succeeded);
    }

    [Fact]
    public void RetiredKeysWithoutACurrentKey_AreRefused_EvenInDevelopment()
    {
        var result = Validator("Development").Validate(null, new KeyRingProtectionOptions { PreviousKeyEncryptionKeys = [NewKey()] });

        Assert.False(result.Succeeded);
        Assert.Contains(KeyRingProtectionOptions.PreviousKeysSettingName, result.FailureMessage);
    }

    [Fact]
    public void EveryProblemIsReportedAtOnce_SoOneDeployFixesThemAll()
    {
        var options = new KeyRingProtectionOptions { KeyEncryptionKey = "short", PreviousKeyEncryptionKeys = ["also short"] };

        var result = Validator("Production").Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(KeyRingProtectionOptions.KeySettingName, result.FailureMessage);
        Assert.Contains($"{KeyRingProtectionOptions.PreviousKeysSettingName}:0", result.FailureMessage);
    }
}
