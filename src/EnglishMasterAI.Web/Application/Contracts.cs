using EnglishMasterAI.Web.Domain;

namespace EnglishMasterAI.Web.Application;

public sealed record QuestionPrompt(
    Guid Id,
    string Skill,
    string Prompt,
    string? SupportingText,
    IReadOnlyList<string> Options,
    int? ToeicPart);

public sealed record AssessmentResult(
    string OverallCefr,
    int EstimatedToeicScore,
    int CorrectAnswers,
    int TotalQuestions,
    IReadOnlyDictionary<string, int> SkillScores,
    IReadOnlyList<string> Priorities);

public sealed record ModuleProgress(
    Guid Id,
    string Code,
    string Title,
    string TitleThai,
    string Summary,
    string Phase,
    string CefrLevel,
    LearningCategory Category,
    int EstimatedMinutes,
    int CompletedLessons,
    int TotalLessons,
    int ProgressPercent,
    string? FirstLessonSlug);

public sealed record DailyTask(string Title, string Detail, int Minutes, string Url, string Kind);

public sealed record DashboardData(
    LearnerProfile Profile,
    int OverallProgress,
    int DueReviews,
    int CompletedLessons,
    int TotalLessons,
    IReadOnlyList<string> WeakAreas,
    IReadOnlyList<DailyTask> DailyPlan,
    IReadOnlyList<ModuleProgress> ContinueModules,
    WeeklyProgress WeeklyProgress,
    IReadOnlyList<LearnerAchievement> Achievements);

public sealed record WeeklyProgressDay(
    DateOnly Date,
    int Minutes,
    int ActivityCount);

public sealed record WeeklyProgress(
    int Minutes,
    int Points,
    int ActivityCount,
    int LessonsCompleted,
    int ReviewsCompleted,
    int AssessmentsCompleted,
    IReadOnlyList<WeeklyProgressDay> Days);

public sealed record QuizResult(
    int Score,
    int Correct,
    int Total,
    bool Passed,
    IReadOnlyDictionary<Guid, bool> Correctness,
    IReadOnlyDictionary<Guid, string> Explanations);

public sealed record ReviewUpdate(int Repetition, int IntervalDays, double EaseFactor, DateTimeOffset NextReviewAt);

public sealed record WritingIssue(
    string Category,
    string Original,
    string Suggestion,
    string Explanation);

public sealed record WritingRubric(
    int Grammar,
    int Clarity,
    int Organization,
    int Vocabulary,
    int TaskCompletion);

public sealed record WritingFeedback(
    int Score,
    int WordCount,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Suggestions,
    IReadOnlyList<WritingIssue> Issues,
    string RevisedVersion,
    WritingRubric Rubric,
    string EvaluationMode,
    string Notice);

public sealed record SpeakingRubric(
    int Fluency,
    int Grammar,
    int Vocabulary,
    int Relevance,
    int Organization);

public sealed record SpeakingFeedback(
    int Score,
    string Transcript,
    int WordCount,
    int WordsPerMinute,
    int FillerWordCount,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Suggestions,
    SpeakingRubric Rubric,
    string EvaluationMode,
    string Notice,
    PronunciationFeedback? Pronunciation = null);

public sealed record PronunciationFeedback(
    double PronunciationScore,
    double AccuracyScore,
    double FluencyScore,
    double CompletenessScore,
    double ProsodyScore,
    string Provider,
    string Notice);

public sealed record LessonEditInput(
    string Title,
    string Objective,
    string ThaiExplanation,
    string GrammarFocus,
    string ReadingContent,
    string ListeningTranscript,
    string SpeakingPrompt,
    string WritingPrompt,
    int EstimatedMinutes,
    string ChangeSummary);

public sealed record AuditSummary(
    int Total,
    int Critical,
    int High,
    int Open,
    bool CanPublish);
