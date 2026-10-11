using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Domain;

/// <summary>
/// A reusable set of instructions that staff write once and start an exam's instructions from (FR-41). When an exam starts from a template
/// it takes its own copy of the text, so editing a template later never changes an exam that already uses it. Candidates acknowledge the
/// exam's instructions, and an acknowledgement must always refer to the text the candidate actually read.
/// </summary>
public sealed class InstructionTemplate : Entity
{
    /// <summary>The longest title a template may have.</summary>
    public const int MaxTitleLength = 120;

    /// <summary>The longest body a template may have. The same limit as an exam's instructions, since the body is copied into one.</summary>
    public const int MaxBodyLength = Exam.MaxInstructionsLength;

    private InstructionTemplate() : base(Guid.Empty)
    {
    }

    private InstructionTemplate(Guid id, string title, string body, DateTime nowUtc) : base(id)
    {
        Title = title;
        Body = body;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>The name staff see when they choose a template.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>The instructions text, copied into an exam when the template is used.</summary>
    public string Body { get; private set; } = string.Empty;

    /// <summary>When the template was created.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>When the template last changed.</summary>
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Creates a template.</summary>
    /// <param name="title">The template's name; surrounding whitespace is removed.</param>
    /// <param name="body">The instructions text; surrounding whitespace is removed.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>The new template.</returns>
    /// <exception cref="InvalidInstructionTemplateError">The title or body is blank, or too long.</exception>
    public static InstructionTemplate Create(string? title, string? body, DateTime nowUtc)
    {
        var (cleanTitle, cleanBody) = Clean(title, body);
        return new InstructionTemplate(Guid.NewGuid(), cleanTitle, cleanBody, nowUtc);
    }

    /// <summary>Changes the template's title and body. Exams that already copied the old body keep it.</summary>
    /// <param name="title">The new title.</param>
    /// <param name="body">The new instructions text.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="InvalidInstructionTemplateError">The title or body is blank, or too long. Nothing is changed.</exception>
    public void Change(string? title, string? body, DateTime nowUtc)
    {
        (Title, Body) = Clean(title, body);
        UpdatedAtUtc = nowUtc;
    }

    // One rule for creating and changing a template, so the two can never disagree about what a template may hold.
    private static (string Title, string Body) Clean(string? title, string? body)
    {
        var trimmedTitle = title?.Trim();
        if (string.IsNullOrEmpty(trimmedTitle))
        {
            throw new InvalidInstructionTemplateError("Give the template a title.");
        }

        if (trimmedTitle.Length > MaxTitleLength)
        {
            throw new InvalidInstructionTemplateError($"A template title must be at most {MaxTitleLength} characters.");
        }

        var trimmedBody = body?.Trim();
        if (string.IsNullOrEmpty(trimmedBody))
        {
            throw new InvalidInstructionTemplateError("Write the instructions the template holds.");
        }

        if (trimmedBody.Length > MaxBodyLength)
        {
            throw new InvalidInstructionTemplateError($"The instructions must be at most {MaxBodyLength} characters.");
        }

        return (trimmedTitle, trimmedBody);
    }
}
