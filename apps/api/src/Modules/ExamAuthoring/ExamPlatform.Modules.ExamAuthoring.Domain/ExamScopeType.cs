namespace ExamPlatform.Modules.ExamAuthoring.Domain;

/// <summary>What an exam's questions may be drawn from (FR-11).</summary>
public enum ExamScopeType
{
    /// <summary>Anywhere in the question bank: the default, and how every exam worked before scopes existed.</summary>
    Independent = 0,

    /// <summary>Any chapter of one book.</summary>
    Book = 1,

    /// <summary>A chosen set of chapters of one book.</summary>
    Chapters = 2,
}
