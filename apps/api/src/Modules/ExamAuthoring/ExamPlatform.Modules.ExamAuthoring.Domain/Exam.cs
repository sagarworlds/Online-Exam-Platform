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

    public DateTime ScheduledStartTime { get; set; }
    public DateTime ScheduledEndTime { get; set; }
    public DateTime? LateEntryDeadline { get; set; }
    public string TimeZone { get; set; } = "Asia/Kolkata";

    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

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

    public void AddSection(string name, int? timeSeconds)
    {
        var order = _sections.Count + 1;
        var section = new ExamSection(name, timeSeconds, order);
        _sections.Add(section);
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

    public void Publish()
    {
        if (Status != ExamStatus.Draft)
            throw new InvalidOperationException("Only draft exams can be published.");

        if (!_sections.Any())
            throw new InvalidExamConfigError("Exam must have at least one section.");

        Status = ExamStatus.Published;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new ExamPublishedEvent(Id, ScheduledStartTime));
    }
}
