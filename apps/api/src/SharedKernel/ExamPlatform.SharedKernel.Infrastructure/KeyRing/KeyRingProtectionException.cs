namespace ExamPlatform.SharedKernel.Infrastructure.KeyRing;

/// <summary>
/// The Data Protection key ring could not be read or written with the key-encryption key this deployment has. It is raised instead of
/// returning an empty or partial key ring: a ring that looks empty makes Data Protection create a new key, and everything protected under
/// the keys that could not be read would then be unreadable, with nothing to say why.
/// </summary>
public sealed class KeyRingProtectionException : InvalidOperationException
{
    /// <summary>Creates the exception with a message that says what to change.</summary>
    /// <param name="message">What went wrong and what the operator should do. It never contains a key.</param>
    public KeyRingProtectionException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception for a failure inside the cipher or while reading a stored entry.</summary>
    /// <param name="message">What went wrong and what the operator should do. It never contains a key.</param>
    /// <param name="innerException">The failure that was caught.</param>
    public KeyRingProtectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
