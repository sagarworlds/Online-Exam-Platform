namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>What a stored capture shows (FR-24, FR-25).</summary>
public enum CaptureKind
{
    /// <summary>A photo of the candidate from their webcam.</summary>
    Webcam = 0,

    /// <summary>A picture of the candidate's screen.</summary>
    Screen = 1,
}
