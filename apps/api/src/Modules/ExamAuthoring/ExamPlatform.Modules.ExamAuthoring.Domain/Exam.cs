using ExamPlatform.SharedKernel.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Events;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamAuthoring.Domain;

/// Exam aggregate root (FR-11, FR-12, FR-13). Manages sections, questions, config, and scheduling.
public class Exam : AggregateRoot
{
    public new Guid Id => base.Id;
    public Guid? SeriesId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public ExamStatus Status { get; set; } = ExamStatus.Draft;
    public ExamConfig Config { get; set; } = new();

    /// <summary>
    /// What the exam's questions may be drawn from (FR-11): anywhere, one book, or chosen chapters. Enforced when a
    /// question is added and when the scope changes, so an exam can never hold a question outside its scope.
    /// </summary>
    public ExamScope Scope { get; private set; } = ExamScope.Independent();

    public DateTime ScheduledStartTime { get; set; }
    public DateTime ScheduledEndTime { get; set; }
    public DateTime? LateEntryDeadline { get; set; }
    public string TimeZone { get; set; } = "Asia/Kolkata";

    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }

    private readonly List<ExamSection> _sections = [];
    public IReadOnlyList<ExamSection> Sections => _sections.AsReadOnly();

    private Exam() : base(Guid.Empty) { }

    public Exam(Guid? seriesId, string name, string? description, DateTime scheduledStartTime, DateTime scheduledEndTime, Guid createdBy)
        : base(Guid.NewGuid())
    {
        // The same rule as correcting the name later, so an exam can never be created with a name it could not be given.
        var (cleanName, cleanDescription) = CleanDetails(name, description);

        SeriesId = seriesId;
        Name = cleanName;
        Description = cleanDescription;
        ScheduledStartTime = scheduledStartTime;
        ScheduledEndTime = scheduledEndTime;
        CreatedBy = createdBy;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;

        AddDomainEvent(new ExamCreatedEvent(Id, Name, CreatedBy));
    }

    public void UpdateConfig(ExamConfig config)
    {
        Config = config;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// What an exam's start and end hold until it is scheduled. A UTC instant, not <see cref="DateTime.MinValue"/>
    /// itself, because the persistence layer refuses any timestamp whose kind is not UTC.
    /// </summary>
    public static readonly DateTime NotScheduledAt = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);

    /// <summary>The longest name an exam may have.</summary>
    public const int MaxNameLength = 255;

    /// <summary>The longest description an exam may have.</summary>
    public const int MaxDescriptionLength = 1000;

    /// <summary>The longest name an exam section may have.</summary>
    public const int MaxSectionNameLength = 255;

    /// <summary>The longest time zone id an exam may carry.</summary>
    public const int MaxTimeZoneLength = 50;

    /// <summary>Whether a start and end have been set; a new exam has neither.</summary>
    public bool IsScheduled => ScheduledEndTime > ScheduledStartTime;

    /// <summary>
    /// Sets when the exam runs (FR-13): the window candidates may start in, how long each attempt lasts,
    /// and an optional cutoff after which nobody may begin.
    /// </summary>
    /// <param name="startUtc">When the window opens.</param>
    /// <param name="endUtc">When the window closes; every attempt ends by then.</param>
    /// <param name="timeZone">The IANA time zone the exam is described in (display only; the instants are UTC); blank keeps the current one.</param>
    /// <param name="lateEntryDeadlineUtc">The last moment a candidate may still start, or null for "any time before the end".</param>
    /// <param name="durationSeconds">How long one attempt lasts, or null for "until the window closes".</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="InvalidExamConfigError">The times, the duration or the time zone are not usable.</exception>
    public void Schedule(
        DateTime startUtc,
        DateTime endUtc,
        string? timeZone,
        DateTime? lateEntryDeadlineUtc,
        int? durationSeconds,
        DateTime nowUtc)
    {
        EnsureDraft();

        if (endUtc <= startUtc)
            throw new InvalidExamConfigError("The end must be after the start.");

        if (endUtc <= nowUtc)
            throw new InvalidExamConfigError("The exam must end in the future.");

        if (lateEntryDeadlineUtc is { } lateEntry && (lateEntry < startUtc || lateEntry > endUtc))
            throw new InvalidExamConfigError("The late-entry deadline must fall between the start and the end.");

        if (durationSeconds is <= 0)
            throw new InvalidExamConfigError("The duration must be positive.");

        if (durationSeconds is { } duration && duration > (endUtc - startUtc).TotalSeconds)
            throw new InvalidExamConfigError("The duration cannot be longer than the window between start and end.");

        var zone = timeZone?.Trim();
        if (zone is { Length: > MaxTimeZoneLength })
            throw new InvalidExamConfigError($"The time zone must be at most {MaxTimeZoneLength} characters.");

        ScheduledStartTime = startUtc;
        ScheduledEndTime = endUtc;
        LateEntryDeadline = lateEntryDeadlineUtc;
        if (!string.IsNullOrEmpty(zone))
            TimeZone = zone;
        // The nested MarkingScheme is copied as well: handing the same instance to the new config makes
        // EF Core treat it as moved between owners and write NULL for its columns.
        Config = Config with { TotalTimeSeconds = durationSeconds, MarkingScheme = Config.MarkingScheme with { } };
        UpdatedAt = nowUtc;
    }

    /// <summary>Appends a section to the exam.</summary>
    /// <param name="name">The section's name; leading and trailing whitespace is removed.</param>
    /// <param name="timeSeconds">An optional time limit for the section.</param>
    /// <returns>The new section.</returns>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="InvalidExamConfigError">The name is blank or too long, or the time limit is not positive.</exception>
    public ExamSection AddSection(string name, int? timeSeconds)
    {
        EnsureDraft();

        var trimmed = CleanSectionName(name, timeSeconds);

        var section = new ExamSection(Id, trimmed, timeSeconds, _sections.Count + 1);
        _sections.Add(section);
        UpdatedAt = DateTime.UtcNow;
        return section;
    }

    /// <summary>Renames a section and sets its time limit.</summary>
    /// <param name="sectionId">The section to change.</param>
    /// <param name="name">The section's new name; leading and trailing whitespace is removed.</param>
    /// <param name="timeSeconds">The section's new time limit, or <see langword="null"/> for none.</param>
    /// <exception cref="ExamNotDraftError">The exam is already published. A section's time limit changes how long a candidate has, so it is fixed with the rest of the exam.</exception>
    /// <exception cref="SectionNotFoundError">The exam has no such section.</exception>
    /// <exception cref="InvalidExamConfigError">The name is blank or too long, or the time limit is not positive.</exception>
    public void EditSection(Guid sectionId, string? name, int? timeSeconds)
    {
        EnsureDraft();

        var section = GetSection(sectionId) ?? throw new SectionNotFoundError(sectionId);
        var trimmed = CleanSectionName(name, timeSeconds);

        section.Name = trimmed;
        section.TimeSeconds = timeSeconds;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Changes the name and description candidates see. Unlike the questions and the schedule this may change after
    /// publishing, for the reason the answer review may: it changes nothing that is asked or scored, so correcting a
    /// typo in the title of an exam that is already open does no harm.
    /// </summary>
    /// <param name="name">The exam's new name; leading and trailing whitespace is removed.</param>
    /// <param name="description">The new description; blank means none.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="ExamArchivedError">The exam is archived.</exception>
    /// <exception cref="InvalidExamConfigError">The name is blank or too long, or the description is too long.</exception>
    public void Describe(string? name, string? description, DateTime nowUtc)
    {
        EnsureNotArchived();

        (Name, Description) = CleanDetails(name, description);
        UpdatedAt = nowUtc;
    }

    // Checks an exam's name and description and returns them cleaned up: trimmed, and a blank description as none. One rule
    // for creating an exam and for correcting one, so the two can never disagree about what an exam may be called.
    private static (string Name, string? Description) CleanDetails(string? name, string? description)
    {
        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName))
            throw new InvalidExamConfigError("An exam needs a name.");
        if (trimmedName.Length > MaxNameLength)
            throw new InvalidExamConfigError($"An exam name must be at most {MaxNameLength} characters.");

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription?.Length > MaxDescriptionLength)
            throw new InvalidExamConfigError($"An exam description must be at most {MaxDescriptionLength} characters.");

        return (trimmedName, trimmedDescription);
    }

    // Checks a section's name and time limit and returns the name cleaned up. One rule for a new section and an edited
    // one, so the two can never disagree about what a section may be.
    private static string CleanSectionName(string? name, int? timeSeconds)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new InvalidExamConfigError("A section needs a name.");
        if (trimmed.Length > MaxSectionNameLength)
            throw new InvalidExamConfigError($"A section name must be at most {MaxSectionNameLength} characters.");
        if (timeSeconds is <= 0)
            throw new InvalidExamConfigError("A section time limit must be positive.");

        return trimmed;
    }

    /// <summary>Checks that questions could be added to a section now, without adding any.</summary>
    /// <param name="sectionId">The section they would go into.</param>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="SectionNotFoundError">The exam has no such section.</exception>
    public void EnsureCanAddQuestions(Guid sectionId)
    {
        EnsureDraft();

        if (GetSection(sectionId) is null)
            throw new SectionNotFoundError(sectionId);
    }

    /// <summary>Appends a question to a section.</summary>
    /// <param name="sectionId">The section to add it to.</param>
    /// <param name="questionId">The question's id in the question bank.</param>
    /// <param name="placement">Where the question is filed in the bank, which the exam's scope is checked against.</param>
    /// <returns>The question as it now sits in the exam.</returns>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="SectionNotFoundError">The exam has no such section.</exception>
    /// <exception cref="QuestionOutsideExamScopeError">The question is not in the exam's book or chapters.</exception>
    /// <exception cref="DuplicateQuestionError">The question is already somewhere in this exam.</exception>
    public ExamQuestion AddQuestion(Guid sectionId, Guid questionId, QuestionPlacement placement)
    {
        EnsureDraft();

        var section = GetSection(sectionId) ?? throw new SectionNotFoundError(sectionId);

        if (!Scope.Allows(placement))
            throw new QuestionOutsideExamScopeError(questionId);

        // Once per exam, not just once per section: a question that appears twice would be asked
        // and scored twice.
        if (_sections.Any(s => s.Questions.Any(q => q.QuestionVersionId == questionId)))
            throw new DuplicateQuestionError(questionId, sectionId);

        var question = section.AddQuestion(questionId, section.Questions.Count + 1);
        UpdatedAt = DateTime.UtcNow;
        return question;
    }

    /// <summary>Adds a rule to a section that draws questions at random for each candidate who starts an attempt.</summary>
    /// <param name="sectionId">The section.</param>
    /// <param name="count">How many to draw.</param>
    /// <param name="bookId">Only questions of this book, or null for any.</param>
    /// <param name="chapterId">Only questions of this chapter, or null for any.</param>
    /// <param name="difficulty">Only this difficulty ("easy", "medium", "hard"), or blank for any.</param>
    /// <param name="topic">Only this topic, or blank for any.</param>
    /// <returns>The rule as added.</returns>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="SectionNotFoundError">The exam has no such section.</exception>
    /// <exception cref="InvalidExamConfigError">A part of the rule is invalid, or the section has the most rules it may.</exception>
    public SectionDrawRule AddDrawRule(Guid sectionId, int count, Guid? bookId, Guid? chapterId, string? difficulty, string? topic)
    {
        EnsureDraft();

        var section = GetSection(sectionId) ?? throw new SectionNotFoundError(sectionId);
        var rule = section.AddDrawRule(count, bookId, chapterId, difficulty, topic);
        UpdatedAt = DateTime.UtcNow;
        return rule;
    }

    /// <summary>Takes a draw rule out of a section.</summary>
    /// <param name="sectionId">The section.</param>
    /// <param name="ruleId">The rule's id.</param>
    /// <exception cref="ExamNotDraftError">The exam is already published. A published exam may already have been sat, and its rules are what its papers are drawn from.</exception>
    /// <exception cref="SectionNotFoundError">The exam has no such section.</exception>
    /// <exception cref="DrawRuleNotFoundError">The section has no such rule.</exception>
    public void RemoveDrawRule(Guid sectionId, Guid ruleId)
    {
        EnsureDraft();

        var section = GetSection(sectionId) ?? throw new SectionNotFoundError(sectionId);
        if (!section.RemoveDrawRule(ruleId))
            throw new DrawRuleNotFoundError(ruleId, sectionId);

        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Takes a question out of a section.</summary>
    /// <param name="sectionId">The section it is in.</param>
    /// <param name="questionId">The question's id in the question bank.</param>
    /// <exception cref="ExamNotDraftError">The exam is already published. A published exam may already have been sat, and its questions are what its scores mean.</exception>
    /// <exception cref="SectionNotFoundError">The exam has no such section.</exception>
    /// <exception cref="QuestionNotInExamError">The section does not hold that question.</exception>
    public void RemoveQuestion(Guid sectionId, Guid questionId)
    {
        EnsureDraft();

        var section = GetSection(sectionId) ?? throw new SectionNotFoundError(sectionId);
        if (!section.RemoveQuestion(questionId))
            throw new QuestionNotInExamError(questionId, sectionId);

        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Changes what the exam's questions may be drawn from.</summary>
    /// <param name="scope">The new scope. The caller has checked that its book and chapters exist and are open.</param>
    /// <param name="placements">
    /// Where each question already in the exam is filed, by question id; one missing from it counts as not filed.
    /// </param>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="QuestionOutsideExamScopeError">
    /// The exam already holds questions the new scope would leave outside it. Refused rather than quietly dropping them.
    /// </exception>
    public void SetScope(ExamScope scope, IReadOnlyDictionary<Guid, QuestionPlacement> placements)
    {
        EnsureDraft();

        var outside = _sections
            .SelectMany(s => s.Questions)
            .Select(q => q.QuestionVersionId)
            .Distinct()
            .Where(id => !scope.Allows(placements.GetValueOrDefault(id)))
            .ToList();
        if (outside.Count > 0)
            throw new QuestionOutsideExamScopeError(outside);

        Scope = scope;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Chooses when candidates may see which of their answers were right, with the correct options (the answer review).
    /// Unlike the schedule and the questions this may change after publishing: the review is only worked out when a
    /// candidate asks for it, so the author can still move the date or hold the answers back until everyone has sat the exam.
    /// </summary>
    /// <param name="mode">Instant (as soon as an attempt is submitted), Scheduled (from <paramref name="releaseTimeUtc"/>) or Manual (when an administrator releases them).</param>
    /// <param name="releaseTimeUtc">When the answers become visible; required for Scheduled and ignored otherwise, so a stale time can never release a manual exam by itself.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="ExamArchivedError">The exam is archived.</exception>
    /// <exception cref="InvalidExamConfigError">The mode is not one of the three, or a scheduled release has no time.</exception>
    public void SetResultRelease(ResultReleaseMode mode, DateTime? releaseTimeUtc, DateTime nowUtc)
    {
        EnsureNotArchived();

        if (!Enum.IsDefined(mode))
            throw new InvalidExamConfigError("Choose when the answers are shown: right after submitting, at a set time, or when released.");

        if (mode == ResultReleaseMode.Scheduled && releaseTimeUtc is null)
            throw new InvalidExamConfigError("Choose the time from which the answers are shown.");

        // The nested MarkingScheme is copied as well, for the reason given in Schedule.
        Config = Config with
        {
            ResultReleaseMode = mode,
            ResultReleaseTime = mode == ResultReleaseMode.Scheduled ? releaseTimeUtc : null,
            MarkingScheme = Config.MarkingScheme with { },
        };
        UpdatedAt = nowUtc;
    }

    /// <summary>
    /// Replaces the marking scheme. Only a draft may change it: once candidates can sit the exam, new marks would make
    /// attempts scored earlier disagree with attempts scored later.
    /// </summary>
    /// <param name="scheme">The marks for correct, incorrect and unattempted questions.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="ExamArchivedError">The exam is archived.</exception>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="InvalidExamConfigError">The scheme is out of range.</exception>
    public void SetMarkingScheme(MarkingScheme scheme, DateTime nowUtc)
    {
        EnsureNotArchived();
        EnsureDraft();
        scheme.EnsureValid();

        // A fresh copy, for the reason given in Schedule.
        Config = Config with { MarkingScheme = scheme with { } };
        UpdatedAt = nowUtc;
    }

    /// <summary>
    /// Chooses whether candidates see this exam's questions and options in a shuffled order. Only a draft may change it: the
    /// order an attempt shows is worked out from these settings every time it is read, so changing them later would move the
    /// questions under candidates already sitting, or already reviewing, the exam.
    /// </summary>
    /// <param name="shuffleQuestions">Whether the questions within each section are shuffled, on every attempt.</param>
    /// <param name="shuffleOptions">Whether the options of each question are shuffled, on every attempt.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="ExamArchivedError">The exam is archived.</exception>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    public void SetShuffle(bool shuffleQuestions, bool shuffleOptions, DateTime nowUtc)
    {
        EnsureNotArchived();
        EnsureDraft();

        // The nested MarkingScheme is copied as well, for the reason given in Schedule.
        Config = Config with { ShuffleQuestions = shuffleQuestions, ShuffleOptions = shuffleOptions, MarkingScheme = Config.MarkingScheme with { } };
        UpdatedAt = nowUtc;
    }

    /// <summary>
    /// Sets how many attempts every enrolled candidate has before an administrator gives them any extra. Like the answer
    /// review this may change after publishing, because it changes nothing that is asked or scored: lowering it never takes
    /// back an attempt already started, it only stops further ones, and raising it opens more at once.
    /// </summary>
    /// <param name="attempts">The attempts each candidate has, from <see cref="ExamConfig.FewestAttempts"/> to <see cref="ExamConfig.MostAttempts"/>.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="ExamArchivedError">The exam is archived.</exception>
    /// <exception cref="InvalidExamConfigError">The number is outside the allowed range.</exception>
    public void SetMaxAttempts(int attempts, DateTime nowUtc)
    {
        EnsureNotArchived();

        if (attempts is < ExamConfig.FewestAttempts or > ExamConfig.MostAttempts)
            throw new InvalidExamConfigError($"Choose a number of attempts from {ExamConfig.FewestAttempts} to {ExamConfig.MostAttempts}.");

        // The nested MarkingScheme is copied as well, for the reason given in Schedule.
        Config = Config with { MaxAttempts = attempts, MarkingScheme = Config.MarkingScheme with { } };
        UpdatedAt = nowUtc;
    }

    /// <summary>
    /// Chooses whether the exam page turns off copying, pasting, right-click and printing while the exam is sat (FR-23). It may change
    /// after publishing, like the attempt limit, because it changes nothing that is asked or scored: it only decides what the page
    /// allows, and an attempt in progress picks the new choice up the next time it is loaded.
    /// </summary>
    /// <param name="enabled">Whether the protection is on.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="ExamArchivedError">The exam is archived.</exception>
    public void SetContentProtection(bool enabled, DateTime nowUtc)
    {
        EnsureNotArchived();

        // The nested MarkingScheme is copied as well, for the reason given in Schedule.
        Config = Config with { ContentProtection = enabled, MarkingScheme = Config.MarkingScheme with { } };
        UpdatedAt = nowUtc;
    }

    /// <summary>
    /// Chooses how many times a candidate may leave the exam page before the attempt is ended (FR-22); 0 turns the watch off. Like
    /// the attempt limit and content protection it may change after publishing, because it changes nothing that is asked or scored;
    /// an attempt in progress picks the new limit up the next time it reports a violation or is loaded.
    /// </summary>
    /// <param name="limit">The violations allowed, from 0 (not watched) to <see cref="ExamConfig.MostViolations"/>.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="ExamArchivedError">The exam is archived.</exception>
    /// <exception cref="InvalidExamConfigError">The limit is outside the allowed range.</exception>
    public void SetFocusViolationLimit(int limit, DateTime nowUtc)
    {
        EnsureNotArchived();

        if (limit is < ExamConfig.NoViolationLimit or > ExamConfig.MostViolations)
            throw new InvalidExamConfigError($"The violation limit is from {ExamConfig.NoViolationLimit} (not watched) to {ExamConfig.MostViolations}.");

        // The nested MarkingScheme is copied as well, for the reason given in Schedule.
        Config = Config with { FocusViolationLimit = limit, MarkingScheme = Config.MarkingScheme with { } };
        UpdatedAt = nowUtc;
    }

    /// <summary>
    /// Applies a proctoring profile (FR-46): sets the copy block and the violation limit together, to what the profile says. The settings
    /// stay editable one by one afterwards, in which case the exam reports its profile as custom. Like those settings it may change after
    /// publishing, because it changes nothing that is asked or scored.
    /// </summary>
    /// <param name="profileId">The profile's id, ignoring case.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="ExamArchivedError">The exam is archived.</exception>
    /// <exception cref="InvalidExamConfigError">There is no such profile, or it cannot be chosen yet.</exception>
    public void SetProctoringProfile(string? profileId, DateTime nowUtc)
    {
        EnsureNotArchived();

        var profile = ProctoringProfiles.Find(profileId)
            ?? throw new InvalidExamConfigError("There is no such proctoring profile.");
        if (!profile.Available)
            throw new InvalidExamConfigError(profile.UnavailableReason ?? "This proctoring profile cannot be chosen yet.");

        // The nested MarkingScheme is copied as well, for the reason given in Schedule.
        Config = Config with
        {
            ContentProtection = profile.ContentProtection,
            FocusViolationLimit = profile.FocusViolationLimit,
            MarkingScheme = Config.MarkingScheme with { },
        };
        UpdatedAt = nowUtc;
    }

    /// <summary>
    /// Releases the answers of a manual-release exam now: the release time is set to the current instant, so the one rule
    /// "released when the mode is Instant or the release time has arrived" covers every mode. Calling it again keeps the first time.
    /// </summary>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="InvalidExamConfigError">The exam is not published, or its answers are not set to be released by hand.</exception>
    public void ReleaseResults(DateTime nowUtc)
    {
        if (Status != ExamStatus.Published)
            throw new InvalidExamConfigError("Only a published exam has answers to release.");

        if (Config.ResultReleaseMode != ResultReleaseMode.Manual)
            throw new InvalidExamConfigError("The answers are released by hand only when the exam is set to manual release.");

        if (Config.ResultReleaseTime is not null)
            return;

        Config = Config with { ResultReleaseTime = nowUtc, MarkingScheme = Config.MarkingScheme with { } };
        UpdatedAt = nowUtc;
    }

    /// <summary>
    /// Takes a section out of the exam together with the questions in it. Those questions only lose their place in this
    /// exam; they stay in the question bank, free to go into another section or exam.
    /// </summary>
    /// <param name="sectionId">The section to remove.</param>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="SectionNotFoundError">The exam has no such section.</exception>
    public void RemoveSection(Guid sectionId)
    {
        EnsureDraft();

        var section = GetSection(sectionId) ?? throw new SectionNotFoundError(sectionId);
        _sections.Remove(section);

        // Sections are numbered 1, 2, 3 ... and the next one added takes the number after the count, so a hole here
        // would give two sections the same number.
        var order = 1;
        foreach (var remaining in _sections.OrderBy(s => s.Order))
            remaining.Order = order++;

        UpdatedAt = DateTime.UtcNow;
    }

    public ExamSection? GetSection(Guid sectionId) => _sections.FirstOrDefault(s => s.Id == sectionId);

    /// <summary>Checks the part of "may this exam be deleted?" that the exam itself knows: only a draft may.</summary>
    /// <exception cref="ExamNotDeletableError">
    /// The exam is published or archived. Candidates may already have been invited to it or sat it, and their results
    /// refer to it, so it stays.
    /// </exception>
    public void EnsureCanBeDeleted()
    {
        if (Status != ExamStatus.Draft)
            throw new ExamNotDeletableError("Only a draft can be deleted; once an exam is published, candidates may have been invited to it or sat it.");
    }

    /// <summary>
    /// Deletes the exam. It is only marked deleted, as every module's records are: every query leaves it out, so it is gone
    /// for everyone, and the question bank stops counting it as using the questions it held.
    /// </summary>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="ExamNotDeletableError">The exam is not a draft.</exception>
    public void Delete(DateTime nowUtc)
    {
        EnsureCanBeDeleted();

        IsDeleted = true;
        UpdatedAt = nowUtc;
    }

    /// <summary>
    /// Opens the exam to the candidates invited to it. Refused until the exam is scheduled and has
    /// something to ask, so a published exam can always be taken.
    /// </summary>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="InvalidExamConfigError">The exam is not scheduled, ends in the past, or has no questions.</exception>
    public void Publish(DateTime nowUtc)
    {
        EnsureDraft();

        if (!IsScheduled)
            throw new InvalidExamConfigError("Schedule the exam before publishing it.");

        if (ScheduledEndTime <= nowUtc)
            throw new InvalidExamConfigError("The exam's end is in the past; schedule it again before publishing.");

        if (!_sections.Any(s => s.Questions.Count > 0 || s.DrawRules.Count > 0))
            throw new InvalidExamConfigError("Add at least one question, or a rule that draws questions, before publishing.");

        Status = ExamStatus.Published;
        UpdatedAt = nowUtc;
        AddDomainEvent(new ExamPublishedEvent(Id, ScheduledStartTime));
    }

    private void EnsureDraft()
    {
        if (Status != ExamStatus.Draft)
            throw new ExamNotDraftError();
    }

    private void EnsureNotArchived()
    {
        if (Status == ExamStatus.Archived)
            throw new ExamArchivedError();
    }
}
