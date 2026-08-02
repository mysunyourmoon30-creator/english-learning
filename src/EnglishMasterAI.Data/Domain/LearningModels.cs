using System.ComponentModel.DataAnnotations;

namespace EnglishMasterAI.Web.Domain;

public enum LearningCategory
{
    Foundation,
    Grammar,
    Vocabulary,
    Reading,
    Listening,
    Speaking,
    Writing,
    Workplace,
    Toeic,
    AiEnglish
}

public enum ContentStatus
{
    Draft,
    InReview,
    ChangesRequested,
    Approved,
    Published,
    Deprecated,
    Archived
}

public enum AssessmentKind
{
    Placement,
    LessonQuiz,
    ToeicDiagnostic,
    ToeicMock
}

public enum AuditSeverity
{
    Suggestion,
    Low,
    Medium,
    High,
    Critical
}

public enum ContentReviewStatus
{
    Pending,
    InReview,
    Approved,
    ChangesRequested
}

public class LearnerProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(450)] public required string UserId { get; set; }
    [MaxLength(100)] public string DisplayName { get; set; } = "ผู้เรียน";
    [MaxLength(300)] public string LearningGoal { get; set; } = "Use English confidently as an AI Engineer";
    [MaxLength(10)] public string CurrentCefr { get; set; } = "Pre-A1";
    [MaxLength(10)] public string TargetCefr { get; set; } = "B2";
    public int TargetToeicScore { get; set; } = 750;
    public int DailyMinutes { get; set; } = 60;
    public bool OnboardingCompleted { get; set; }
    public bool PlacementCompleted { get; set; }
    public int CurrentStreak { get; set; }
    public DateOnly? LastLearningDate { get; set; }
    [MaxLength(80)] public string TimeZoneId { get; set; } = "Asia/Bangkok";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class CourseModule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(16)] public required string Code { get; set; }
    [MaxLength(160)] public required string Title { get; set; }
    [MaxLength(160)] public required string TitleThai { get; set; }
    [MaxLength(600)] public required string Summary { get; set; }
    [MaxLength(40)] public required string Phase { get; set; }
    [MaxLength(10)] public required string CefrLevel { get; set; }
    public LearningCategory Category { get; set; }
    public int SortOrder { get; set; }
    public int EstimatedMinutes { get; set; }
    public bool IsPublished { get; set; } = true;
    public List<Lesson> Lessons { get; set; } = [];
}

public class Lesson
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseModuleId { get; set; }
    public CourseModule Module { get; set; } = null!;
    [MaxLength(120)] public required string Slug { get; set; }
    [MaxLength(200)] public required string Title { get; set; }
    [MaxLength(500)] public required string Objective { get; set; }
    public required string ThaiExplanation { get; set; }
    public required string GrammarFocus { get; set; }
    public required string ReadingContent { get; set; }
    public required string ListeningTranscript { get; set; }
    public required string SpeakingPrompt { get; set; }
    public required string WritingPrompt { get; set; }
    public int EstimatedMinutes { get; set; } = 20;
    public int SortOrder { get; set; }
    public int Version { get; set; } = 1;
    public ContentStatus Status { get; set; } = ContentStatus.Published;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<VocabularyItem> Vocabulary { get; set; } = [];
    public List<AssessmentQuestion> Questions { get; set; } = [];
}

public class VocabularyItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;
    [MaxLength(100)] public required string Word { get; set; }
    [MaxLength(200)] public required string ThaiMeaning { get; set; }
    [MaxLength(100)] public required string Pronunciation { get; set; }
    [MaxLength(80)] public required string WordForm { get; set; }
    [MaxLength(160)] public required string Collocation { get; set; }
    [MaxLength(400)] public required string ExampleSentence { get; set; }
}

public class AssessmentQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? LessonId { get; set; }
    public Lesson? Lesson { get; set; }
    public AssessmentKind Kind { get; set; }
    [MaxLength(40)] public required string Skill { get; set; }
    public int? ToeicPart { get; set; }
    [MaxLength(600)] public required string Prompt { get; set; }
    [MaxLength(1200)] public string? SupportingText { get; set; }
    public required string OptionsJson { get; set; }
    public int CorrectOptionIndex { get; set; }
    [MaxLength(800)] public required string Explanation { get; set; }
    public int Difficulty { get; set; } = 1;
    public int SortOrder { get; set; }
    public bool IsPublished { get; set; } = true;
}

public class LearningProgress
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(450)] public required string UserId { get; set; }
    public Guid LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;
    public int BestScore { get; set; }
    public int AttemptCount { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;
}

public class PlacementAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(450)] public required string UserId { get; set; }
    [MaxLength(30)] public required string AssessmentName { get; set; }
    [MaxLength(10)] public required string OverallCefr { get; set; }
    public int EstimatedToeicScore { get; set; }
    public required string SkillScoresJson { get; set; }
    public required string PrioritiesJson { get; set; }
    public int CorrectAnswers { get; set; }
    public int TotalQuestions { get; set; }
    public int DurationSeconds { get; set; }
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ReviewSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(450)] public required string UserId { get; set; }
    public Guid VocabularyItemId { get; set; }
    public VocabularyItem VocabularyItem { get; set; } = null!;
    public int Repetition { get; set; }
    public int IntervalDays { get; set; }
    public double EaseFactor { get; set; } = 2.5;
    public DateTimeOffset NextReviewAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastReviewedAt { get; set; }
}

public class WritingSubmission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(450)] public required string UserId { get; set; }
    [MaxLength(200)] public required string Prompt { get; set; }
    public required string Content { get; set; }
    public int Score { get; set; }
    public required string FeedbackJson { get; set; }
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class SpeakingSubmission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(450)] public required string UserId { get; set; }
    [MaxLength(300)] public required string Prompt { get; set; }
    public required string Transcript { get; set; }
    public int DurationSeconds { get; set; }
    public int Score { get; set; }
    [MaxLength(40)] public required string EvaluationMode { get; set; }
    public required string FeedbackJson { get; set; }
    public double? PronunciationScore { get; set; }
    public double? AccuracyScore { get; set; }
    public double? CompletenessScore { get; set; }
    public double? ProsodyScore { get; set; }
    [MaxLength(40)] public string? PronunciationProvider { get; set; }
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class LearningActivity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(450)] public required string UserId { get; set; }
    [MaxLength(60)] public required string Kind { get; set; }
    [MaxLength(160)] public required string DeduplicationKey { get; set; }
    public int Minutes { get; set; }
    public int Points { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

public class LearnerAchievement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(450)] public required string UserId { get; set; }
    [MaxLength(60)] public required string Code { get; set; }
    [MaxLength(120)] public required string Title { get; set; }
    [MaxLength(300)] public required string Description { get; set; }
    public DateTimeOffset EarnedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class AiUsageRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(450)] public required string UserId { get; set; }
    [MaxLength(40)] public required string Operation { get; set; }
    [MaxLength(40)] public required string Provider { get; set; }
    [MaxLength(80)] public required string Model { get; set; }
    [MaxLength(20)] public required string Status { get; set; }
    public int InputUnits { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int DurationMilliseconds { get; set; }
    [MaxLength(100)] public string? RequestId { get; set; }
    [MaxLength(120)] public string? FailureType { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ContentReviewAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;
    [MaxLength(450)] public string? ReviewerUserId { get; set; }
    [MaxLength(80)] public required string ReviewerRole { get; set; }
    public ContentReviewStatus Status { get; set; } = ContentReviewStatus.Pending;
    [MaxLength(1000)] public string Notes { get; set; } = string.Empty;
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAt { get; set; }
}

public class ContentRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;
    public int Version { get; set; }
    public ContentStatus Status { get; set; }
    [MaxLength(450)] public required string ChangedByUserId { get; set; }
    [MaxLength(300)] public required string ChangeSummary { get; set; }
    public required string SnapshotJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class AuditFinding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;
    [MaxLength(80)] public required string AuditorRole { get; set; }
    public AuditSeverity Severity { get; set; }
    [MaxLength(200)] public required string Location { get; set; }
    [MaxLength(1000)] public required string Issue { get; set; }
    [MaxLength(1000)] public required string Recommendation { get; set; }
    public bool IsResolved { get; set; }
    [MaxLength(450)] public required string CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }
}
