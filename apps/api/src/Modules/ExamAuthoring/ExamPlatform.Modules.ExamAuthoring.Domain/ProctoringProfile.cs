using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Domain;

/// <summary>
/// A named bundle of the proctoring settings an exam can have (requirements section 8, FR-46): choosing one sets them together, and the
/// notice a candidate is shown is written from them, so what the candidate is told is always what is collected.
/// </summary>
/// <param name="Id">The profile's stable key, as the requirements name it (for example <c>BROWSER_LOCK</c>).</param>
/// <param name="Name">What an author reads.</param>
/// <param name="Description">Who it is for.</param>
/// <param name="ContentProtection">Whether copy, paste, right-click and print are turned off (FR-23).</param>
/// <param name="FocusViolationLimit">How many times a candidate may leave the exam page before the attempt ends (FR-22); 0 means not watched.</param>
/// <param name="Available">Whether an author can choose it. A profile that promises something not built yet is listed but not offered.</param>
/// <param name="UnavailableReason">Why it cannot be chosen yet; null when it can.</param>
/// <param name="CaptureMedia">Whether the profile uses the camera or records the screen. A candidate under 18 is never held to such a profile (see <see cref="ProctoringProfiles.ForCandidate"/>).</param>
public sealed record ProctoringProfile(
    string Id,
    string Name,
    string Description,
    bool ContentProtection,
    int FocusViolationLimit,
    bool Available = true,
    string? UnavailableReason = null,
    bool CaptureMedia = false);

/// <summary>
/// The profiles the platform knows. A profile is data, so adding one changes this list and nothing that runs an exam: the runtime reads
/// the two settings a profile sets, never a profile's name.
/// </summary>
public static class ProctoringProfiles
{
    /// <summary>The id reported for an exam whose settings do not match any profile, because an author set them one by one.</summary>
    public const string Custom = "CUSTOM";

    /// <summary>How many departures from the exam page end an attempt under <c>BROWSER_LOCK</c>.</summary>
    public const int BrowserLockViolationLimit = 5;

    /// <summary>Nothing is turned off or watched: practice and chapter tests.</summary>
    public static readonly ProctoringProfile Off = new("OFF", "No proctoring", "Nothing is turned off or watched. For practice and chapter tests.", false, 0);

    /// <summary>Copying and printing are turned off and leaving the page is counted: the default for minors.</summary>
    public static readonly ProctoringProfile BrowserLock = new(
        "BROWSER_LOCK",
        "Browser lock",
        "Copy, paste, right-click and print are turned off, and leaving the exam page is counted: the attempt ends after " + BrowserLockViolationLimit + " times. The default for minors.",
        true,
        BrowserLockViolationLimit);

    /// <summary>Every profile, in the order an author sees them.</summary>
    public static readonly IReadOnlyList<ProctoringProfile> All =
    [
        Off,
        BrowserLock,
        new("BROWSER_CAMERA", "Browser lock and camera", "Browser lock, plus webcam snapshots at intervals, with consent.", true, BrowserLockViolationLimit,
            Available: false, UnavailableReason: "Webcam snapshots (FR-24) are not built yet, so this profile cannot be chosen.", CaptureMedia: true),
        new("FULL", "Full proctoring", "Browser lock, camera and screen capture, with a human review queue.", true, BrowserLockViolationLimit,
            Available: false, UnavailableReason: "Webcam snapshots (FR-24) and screen capture (FR-25) are not built yet, so this profile cannot be chosen.", CaptureMedia: true),
    ];

    /// <summary>Finds a profile by its id, ignoring case.</summary>
    /// <param name="id">The id to look for.</param>
    /// <returns>The profile, or null when there is none.</returns>
    public static ProctoringProfile? Find(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The id of the profile an exam's settings amount to, or <see cref="Custom"/> when an author set them one by one and they match none.
    /// Worked out from the settings rather than stored, so the two cannot disagree.
    /// </summary>
    /// <param name="contentProtection">Whether copy, paste, right-click and print are turned off.</param>
    /// <param name="focusViolationLimit">The violation limit, 0 when not watched.</param>
    public static string IdFor(bool contentProtection, int focusViolationLimit) =>
        All.FirstOrDefault(p => p.Available && p.ContentProtection == contentProtection && p.FocusViolationLimit == focusViolationLimit)?.Id ?? Custom;

    /// <summary>
    /// The profile a candidate is actually held to. The minors' default (requirements section 7.2): a candidate under 18 is never watched
    /// by camera or screen capture, whatever the exam's profile says, so a profile that captures media falls back to
    /// <see cref="BrowserLock"/>. An adult keeps the profile they were given. The rule lives in this one place, so counsel's answer on
    /// issue #11 can change it here alone.
    /// </summary>
    /// <param name="profile">The profile the exam uses.</param>
    /// <param name="candidateIsMinor">Whether the candidate was under 18 when the attempt started.</param>
    /// <returns>The profile to hold the candidate to.</returns>
    public static ProctoringProfile ForCandidate(ProctoringProfile profile, bool candidateIsMinor) =>
        candidateIsMinor && profile.CaptureMedia ? BrowserLock : profile;
}

/// <summary>
/// The notice a candidate is shown before they start, written from the exam's proctoring settings (FR-46) so it states exactly what
/// the platform collects and does, no more and no less. When a collection is added (camera, screen), this is the one place its
/// sentence is added, and every exam that uses it then says so.
/// </summary>
public static class ProctoringNotice
{
    /// <summary>The notice as short sentences, in the order they matter.</summary>
    /// <param name="contentProtection">Whether copy, paste, right-click and print are turned off.</param>
    /// <param name="focusViolationLimit">The violation limit, 0 when not watched.</param>
    public static IReadOnlyList<string> For(bool contentProtection, int focusViolationLimit)
    {
        // Always true of every exam, so always said (FR-26).
        var lines = new List<string>
        {
            "Your IP address and a signature of your device and browser are recorded while you sit the exam, and kept with your attempt for the organisers.",
        };

        if (contentProtection)
            lines.Add("Copying, pasting, right-click and printing are turned off during the exam. You can still select text.");

        if (focusViolationLimit > 0)
        {
            lines.Add(
                "Stay on the exam page and in full screen. Switching to another tab or window, or leaving full screen, is recorded. "
                + (focusViolationLimit == 1
                    ? "The exam is submitted for you the first time you leave."
                    : WarningSentence(FocusWarningPoint.For(focusViolationLimit), focusViolationLimit)));
        }

        // Said as plainly as what is collected: nothing else is, and a person should not have to wonder.
        lines.Add("No camera, microphone or screen recording is used.");
        return lines;
    }

    /// <summary>
    /// Says when the candidate is first warned and when the attempt ends, so the warning is not a surprise. The warning point is at most
    /// the third departure, so only the second and third are named as ordinals.
    /// </summary>
    /// <param name="warnAt">The departure from which the candidate is warned; 1 means every departure.</param>
    /// <param name="limit">The departure that ends the attempt.</param>
    private static string WarningSentence(int warnAt, int limit) =>
        (warnAt == 1 ? "You are warned each time you leave. " : $"You are warned from the {(warnAt == 2 ? "second" : "third")} time you leave. ")
        + $"If you leave {limit} times, the exam is submitted for you with the answers saved so far.";
}
