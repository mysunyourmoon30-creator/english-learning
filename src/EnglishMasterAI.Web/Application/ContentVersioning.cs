using System.Text.Json;
using EnglishMasterAI.Web.Domain;

namespace EnglishMasterAI.Web.Application;

public static class ContentVersioning
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ContentRevision CreateRevision(
        Lesson lesson,
        string userId,
        string summary) =>
        new()
        {
            LessonId = lesson.Id,
            Version = lesson.Version,
            Status = lesson.Status,
            ChangedByUserId = string.IsNullOrWhiteSpace(userId) ? "system" : userId,
            ChangeSummary = Clean(summary, 300),
            SnapshotJson = JsonSerializer.Serialize(ToSnapshot(lesson), JsonOptions)
        };

    public static void ApplySnapshot(Lesson lesson, string snapshotJson)
    {
        var snapshot = JsonSerializer.Deserialize<LessonSnapshot>(snapshotJson, JsonOptions)
            ?? throw new InvalidOperationException("Content revision snapshot is invalid.");
        lesson.Title = snapshot.Title;
        lesson.Objective = snapshot.Objective;
        lesson.ThaiExplanation = snapshot.ThaiExplanation;
        lesson.GrammarFocus = snapshot.GrammarFocus;
        lesson.ReadingContent = snapshot.ReadingContent;
        lesson.ListeningTranscript = snapshot.ListeningTranscript;
        lesson.SpeakingPrompt = snapshot.SpeakingPrompt;
        lesson.WritingPrompt = snapshot.WritingPrompt;
        lesson.EstimatedMinutes = snapshot.EstimatedMinutes;
    }

    private static LessonSnapshot ToSnapshot(Lesson lesson) =>
        new(
            lesson.Title,
            lesson.Objective,
            lesson.ThaiExplanation,
            lesson.GrammarFocus,
            lesson.ReadingContent,
            lesson.ListeningTranscript,
            lesson.SpeakingPrompt,
            lesson.WritingPrompt,
            lesson.EstimatedMinutes);

    private static string Clean(string value, int maxLength)
    {
        var result = value.Trim();
        return result.Length <= maxLength ? result : result[..maxLength];
    }

    private sealed record LessonSnapshot(
        string Title,
        string Objective,
        string ThaiExplanation,
        string GrammarFocus,
        string ReadingContent,
        string ListeningTranscript,
        string SpeakingPrompt,
        string WritingPrompt,
        int EstimatedMinutes);
}
