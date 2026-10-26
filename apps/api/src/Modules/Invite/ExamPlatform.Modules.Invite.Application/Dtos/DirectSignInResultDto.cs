namespace ExamPlatform.Modules.Invite.Application.Dtos;

/// <summary>What a direct sign-in link returns: the new session, and the exam the invitation was for.</summary>
/// <param name="AccessToken">The signed token the web app sends from now on.</param>
/// <param name="SessionId">The session the token belongs to.</param>
/// <param name="ExamId">The exam the invitation was for, so the web app can open its start page.</param>
public sealed record DirectSignInResultDto(string AccessToken, Guid SessionId, Guid ExamId);
