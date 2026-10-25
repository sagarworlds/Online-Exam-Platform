using System.Globalization;
using System.Text.Json;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.AiEvaluation;

/// <summary>What reading a model's reply gave: a verdict, or the reason there is none.</summary>
/// <param name="Evaluation">The verdict, or null when the reply held none.</param>
/// <param name="Problem">Why there is no verdict, in a few words; null when there is one.</param>
internal readonly record struct VerdictRead(TextEvaluation? Evaluation, string? Problem)
{
    public static VerdictRead Done(TextEvaluation evaluation) => new(evaluation, null);

    public static VerdictRead Unusable(string problem) => new(null, problem);
}

/// <summary>
/// Reads the JSON a model is asked to reply with, <c>{"verdict": "correct" or "wrong", "reason": "..."}</c>, whichever provider it came from. Models
/// that were asked for JSON sometimes still wrap it in a code fence or add a word, so the reading is tolerant of that and of nothing else: a
/// verdict that is not one of the two words is as good as no reply, and asking again may get one.
/// </summary>
internal static class VerdictReader
{
    /// <summary>Reads a model's reply text.</summary>
    /// <param name="text">The text the model replied with.</param>
    /// <param name="model">The model that gave it, as the provider named it.</param>
    /// <returns>The verdict, or why there is none.</returns>
    public static VerdictRead Read(string text, string model)
    {
        try
        {
            using var reply = JsonDocument.Parse(StripFence(text));
            var root = reply.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("verdict", out var verdictProperty)
                || verdictProperty.ValueKind != JsonValueKind.String)
                return VerdictRead.Unusable("The AI service's reply held no verdict.");

            var verdict = verdictProperty.GetString()?.Trim().ToLower(CultureInfo.InvariantCulture) switch
            {
                "correct" => (TypedAnswerVerdict?)TypedAnswerVerdict.Correct,
                "wrong" => TypedAnswerVerdict.Wrong,
                _ => null,
            };
            if (verdict is null)
                return VerdictRead.Unusable("The AI service's verdict was neither correct nor wrong.");

            var reason = root.TryGetProperty("reason", out var reasonProperty) && reasonProperty.ValueKind == JsonValueKind.String ? reasonProperty.GetString() : null;
            return VerdictRead.Done(new TextEvaluation(verdict.Value, reason, model));
        }
        catch (JsonException)
        {
            return VerdictRead.Unusable("The AI service's reply was not JSON.");
        }
    }

    /// <summary>Takes the JSON out of a Markdown code fence, when the model wrapped it in one.</summary>
    /// <param name="text">The reply text.</param>
    public static string StripFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var firstLineEnd = trimmed.IndexOf('\n', StringComparison.Ordinal);
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstLineEnd >= 0 && lastFence > firstLineEnd ? trimmed[(firstLineEnd + 1)..lastFence].Trim() : trimmed;
    }
}
