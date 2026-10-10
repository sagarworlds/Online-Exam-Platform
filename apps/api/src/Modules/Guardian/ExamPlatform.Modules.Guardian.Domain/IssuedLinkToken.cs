namespace ExamPlatform.Modules.Guardian.Domain;

/// <summary>A newly issued link code.</summary>
/// <param name="Raw">The code as it goes into the guardian's e-mail. It is never stored.</param>
/// <param name="Hash">The code's hash, which is what the platform keeps.</param>
/// <param name="ExpiresAtUtc">When the code stops working.</param>
public sealed record IssuedLinkToken(string Raw, string Hash, DateTime ExpiresAtUtc);
