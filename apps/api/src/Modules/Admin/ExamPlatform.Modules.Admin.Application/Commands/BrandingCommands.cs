namespace ExamPlatform.Modules.Admin.Application.Commands;

/// <summary>Sets the institute's name and primary colour (FR-41).</summary>
/// <param name="InstituteName">The name candidates should see; blank clears it.</param>
/// <param name="PrimaryColour">A six-digit hex colour such as <c>#1A56DB</c>; blank clears it.</param>
public sealed record UpdateBrandingCommand(string? InstituteName, string? PrimaryColour);

/// <summary>Replaces the institute's logo (FR-41).</summary>
/// <param name="Content">The image's bytes, as uploaded. The format is checked from these bytes.</param>
public sealed record SetBrandingLogoCommand(byte[] Content);
