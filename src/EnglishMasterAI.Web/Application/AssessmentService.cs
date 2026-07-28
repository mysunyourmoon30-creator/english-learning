using System.Text.Json;
using EnglishMasterAI.Web.Data;
using EnglishMasterAI.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnglishMasterAI.Web.Application;

public sealed class AssessmentService(ApplicationDbContext db, LearningService learningService)
{
    private static readonly Dictionary<string, string> PriorityLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Pronunciation"] = "Pronunciation & word sounds",
        ["Grammar"] = "Sentence structure & grammar",
        ["Vocabulary"] = "Core vocabulary",
        ["Reading"] = "Reading comprehension",
        ["Listening"] = "Listening comprehension",
        ["Writing"] = "Writing accuracy",
        ["Speaking"] = "Speaking confidence",
        ["Technical English"] = "Technical English",
        ["TOEIC"] = "TOEIC strategy"
    };

    public async Task<IReadOnlyList<QuestionPrompt>> GetQuestionsAsync(AssessmentKind kind)
    {
        var questions = await db.AssessmentQuestions
            .AsNoTracking()
            .Where(x => x.Kind == kind && x.LessonId == null && x.IsPublished)
            .OrderBy(x => x.SortOrder)
            .ToListAsync();

        return questions.Select(ToPrompt).ToList();
    }

    public async Task<AssessmentResult> SubmitAsync(
        string userId,
        AssessmentKind kind,
        IReadOnlyDictionary<Guid, int> answers,
        int durationSeconds)
    {
        if (kind is not (AssessmentKind.Placement or AssessmentKind.ToeicDiagnostic or AssessmentKind.ToeicMock))
        {
            throw new InvalidOperationException("Only standalone assessments can be submitted here.");
        }

        var questions = await db.AssessmentQuestions
            .Where(x => x.Kind == kind && x.LessonId == null && x.IsPublished)
            .OrderBy(x => x.SortOrder)
            .ToListAsync();
        if (questions.Count == 0)
        {
            throw new InvalidOperationException("Assessment has no published questions.");
        }

        var grouped = questions.GroupBy(x => x.Skill);
        var skillScores = grouped.ToDictionary(
            group => group.Key,
            group =>
            {
                var items = group.ToList();
                var correct = items.Count(q => answers.TryGetValue(q.Id, out var answer) && answer == q.CorrectOptionIndex);
                return (int)Math.Round(correct * 100d / items.Count);
            });

        var correctAnswers = questions.Count(q => answers.TryGetValue(q.Id, out var answer) && answer == q.CorrectOptionIndex);
        var accuracy = (int)Math.Round(correctAnswers * 100d / questions.Count);
        var cefr = AssessmentScorer.ToCefrLevel(accuracy);

        var toeicQuestions = questions.Where(x => x.ToeicPart is not null || x.Skill == "TOEIC").ToList();
        var toeicAccuracy = toeicQuestions.Count == 0
            ? accuracy
            : (int)Math.Round(toeicQuestions.Count(q => answers.TryGetValue(q.Id, out var answer) && answer == q.CorrectOptionIndex) * 100d / toeicQuestions.Count);
        var estimatedToeic = AssessmentScorer.EstimateToeicScore(toeicAccuracy);

        var priorities = skillScores
            .OrderBy(x => x.Value)
            .ThenBy(x => x.Key)
            .Take(4)
            .Select(x => PriorityLabels.GetValueOrDefault(x.Key, x.Key))
            .ToList();

        db.PlacementAttempts.Add(new PlacementAttempt
        {
            UserId = userId,
            AssessmentName = kind switch
            {
                AssessmentKind.Placement => "Placement",
                AssessmentKind.ToeicMock => "TOEIC Mock",
                _ => "TOEIC Diagnostic"
            },
            OverallCefr = cefr,
            EstimatedToeicScore = estimatedToeic,
            SkillScoresJson = JsonSerializer.Serialize(skillScores),
            PrioritiesJson = JsonSerializer.Serialize(priorities),
            CorrectAnswers = correctAnswers,
            TotalQuestions = questions.Count,
            DurationSeconds = Math.Max(0, durationSeconds)
        });

        if (kind == AssessmentKind.Placement)
        {
            var profile = await learningService.GetOrCreateProfileAsync(userId);
            profile.CurrentCefr = cefr;
            profile.PlacementCompleted = true;
            profile.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync();
        return new AssessmentResult(cefr, estimatedToeic, correctAnswers, questions.Count, skillScores, priorities);
    }

    public async Task<IReadOnlyList<PlacementAttempt>> GetHistoryAsync(string userId, string? assessmentName = null)
    {
        var query = db.PlacementAttempts.AsNoTracking().Where(x => x.UserId == userId);
        if (!string.IsNullOrWhiteSpace(assessmentName))
        {
            query = query.Where(x => x.AssessmentName == assessmentName);
        }
        var history = await query.ToListAsync();
        return history.OrderByDescending(x => x.CompletedAt).Take(10).ToList();
    }

    private static QuestionPrompt ToPrompt(AssessmentQuestion question)
    {
        var options = JsonSerializer.Deserialize<List<string>>(question.OptionsJson) ?? [];
        return new QuestionPrompt(question.Id, question.Skill, question.Prompt, question.SupportingText, options, question.ToeicPart);
    }
}
