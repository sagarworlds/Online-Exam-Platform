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
        SeriesId = seriesId;
        Name = name;
        Description = description;
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

        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new InvalidExamConfigError("A section needs a name.");
        if (trimmed.Length > MaxSectionNameLength)
            throw new InvalidExamConfigError($"A section name must be at most {MaxSectionNameLength} characters.");
        if (timeSeconds is <= 0)
            throw new InvalidExamConfigError("A section time limit must be positive.");

        var section = new ExamSection(Id, trimmed, timeSeconds, _sections.Count + 1);
        _sections.Add(section);
        UpdatedAt = DateTime.UtcNow;
        return section;
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

    public void RemoveSection(Guid sectionId)
    {
        var section = _sections.FirstOrDefault(s => s.Id == sectionId);
        if (section != null)
        {
            _sections.Remove(section);
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public ExamSection? GetSection(Guid sectionId) => _sections.FirstOrDefault(s => s.Id == sectionId);

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

        if (!_sections.Any(s => s.Questions.Count > 0))
            throw new InvalidExamConfigError("Add at least one question before publishing.");

        Status = ExamStatus.Published;
        UpdatedAt = nowUtc;
        AddDomainEvent(new ExamPublishedEvent(Id, ScheduledStartTime));
    }

    private void EnsureDraft()
    {
        if (Status != ExamStatus.Draft)
            throw new ExamNotDraftError();
    }
}
