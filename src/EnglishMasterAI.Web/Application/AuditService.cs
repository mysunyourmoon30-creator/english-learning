using EnglishMasterAI.Web.Data;
using EnglishMasterAI.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnglishMasterAI.Web.Application;

public sealed class AuditService(ApplicationDbContext db)
{
    public async Task<IReadOnlyList<Lesson>> GetContentLessonsAsync()
    {
        return await db.Lessons
            .AsNoTracking()
            .Include(x => x.Module)
            .OrderBy(x => x.Module.SortOrder)
            .ThenBy(x => x.SortOrder)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<ContentRevision>> GetRevisionsAsync(Guid lessonId)
    {
        var revisions = await db.ContentRevisions
            .AsNoTracking()
            .Where(x => x.LessonId == lessonId)
            .ToListAsync();
        return revisions
            .OrderByDescending(x => x.Version)
            .ThenByDescending(x => x.CreatedAt)
            .ToList();
    }

    public async Task<IReadOnlyList<AuditFinding>> GetFindingsAsync(bool includeResolved = false)
    {
        var query = db.AuditFindings
            .AsNoTracking()
            .Include(x => x.Lesson)
            .ThenInclude(x => x.Module)
            .AsQueryable();
        if (!includeResolved)
        {
            query = query.Where(x => !x.IsResolved);
        }
        var findings = await query.ToListAsync();
        return findings.OrderByDescending(x => x.Severity).ThenByDescending(x => x.CreatedAt).ToList();
    }

    public async Task<AuditSummary> GetSummaryAsync(Guid? lessonId = null)
    {
        var query = db.AuditFindings.AsNoTracking().AsQueryable();
        if (lessonId is not null)
        {
            query = query.Where(x => x.LessonId == lessonId);
        }
        var findings = await query.ToListAsync();
        var open = findings.Where(x => !x.IsResolved).ToList();
        return new AuditSummary(
            findings.Count,
            open.Count(x => x.Severity == AuditSeverity.Critical),
            open.Count(x => x.Severity == AuditSeverity.High),
            open.Count,
            open.All(x => (int)x.Severity < (int)AuditSeverity.High));
    }

    public async Task AddFindingAsync(
        Guid lessonId,
        string userId,
        string auditorRole,
        AuditSeverity severity,
        string location,
        string issue,
        string recommendation)
    {
        var lessonExists = await db.Lessons.AnyAsync(x => x.Id == lessonId);
        if (!lessonExists)
        {
            throw new InvalidOperationException("Lesson not found.");
        }

        db.AuditFindings.Add(new AuditFinding
        {
            LessonId = lessonId,
            CreatedByUserId = userId,
            AuditorRole = RequiredText(auditorRole, 80, "Auditor role"),
            Severity = severity,
            Location = RequiredText(location, 200, "Location"),
            Issue = RequiredText(issue, 1_000, "Issue"),
            Recommendation = RequiredText(recommendation, 1_000, "Recommendation")
        });
        await db.SaveChangesAsync();
    }

    public async Task ResolveAsync(Guid findingId)
    {
        var finding = await db.AuditFindings.FindAsync(findingId)
            ?? throw new InvalidOperationException("Audit finding not found.");
        finding.IsResolved = true;
        finding.ResolvedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task UpdateLessonAsync(
        Guid lessonId,
        string userId,
        LessonEditInput input)
    {
        var lesson = await db.Lessons.FindAsync(lessonId)
            ?? throw new InvalidOperationException("Lesson not found.");

        lesson.Title = RequiredText(input.Title, 200, "Title");
        lesson.Objective = RequiredText(input.Objective, 500, "Objective");
        lesson.ThaiExplanation = RequiredText(input.ThaiExplanation, 20_000, "Thai explanation");
        lesson.GrammarFocus = RequiredText(input.GrammarFocus, 20_000, "Grammar focus");
        lesson.ReadingContent = RequiredText(input.ReadingContent, 40_000, "Reading content");
        lesson.ListeningTranscript = RequiredText(input.ListeningTranscript, 40_000, "Listening transcript");
        lesson.SpeakingPrompt = RequiredText(input.SpeakingPrompt, 5_000, "Speaking prompt");
        lesson.WritingPrompt = RequiredText(input.WritingPrompt, 5_000, "Writing prompt");
        lesson.EstimatedMinutes = Math.Clamp(input.EstimatedMinutes, 5, 240);
        lesson.Version++;
        lesson.Status = ContentStatus.Draft;
        lesson.UpdatedAt = DateTimeOffset.UtcNow;
        db.ContentRevisions.Add(ContentVersioning.CreateRevision(
            lesson,
            userId,
            RequiredText(input.ChangeSummary, 300, "Change summary")));
        var reviewAssignments = await db.ContentReviewAssignments
            .Where(x => x.LessonId == lessonId)
            .ToListAsync();
        foreach (var assignment in reviewAssignments)
        {
            assignment.Status = ContentReviewStatus.Pending;
            assignment.ReviewedAt = null;
            assignment.Notes = string.Empty;
        }
        await db.SaveChangesAsync();
    }

    public async Task ChangeContentStatusAsync(
        Guid lessonId,
        ContentStatus status,
        string userId)
    {
        var lesson = await db.Lessons.FindAsync(lessonId)
            ?? throw new InvalidOperationException("Lesson not found.");
        if (lesson.Status == status)
        {
            return;
        }

        if (!CanTransition(lesson.Status, status))
        {
            throw new InvalidOperationException(
                $"Content cannot move directly from {lesson.Status} to {status}.");
        }

        if (status == ContentStatus.Published)
        {
            var summary = await GetSummaryAsync(lessonId);
            if (!summary.CanPublish)
            {
                throw new InvalidOperationException(
                    "Resolve all Critical and High findings before publishing.");
            }

            var category = await db.Lessons
                .Where(x => x.Id == lessonId)
                .Select(x => x.Module.Category)
                .SingleAsync();
            if (category == LearningCategory.AiEnglish)
            {
                var reviews = await db.ContentReviewAssignments
                    .Where(x => x.LessonId == lessonId)
                    .ToListAsync();
                if (reviews.Count < 2
                    || reviews.Any(x => x.Status != ContentReviewStatus.Approved))
                {
                    throw new InvalidOperationException(
                        "AI English lessons require approved language and AI subject-matter reviews before publishing.");
                }
            }
        }

        lesson.Status = status;
        lesson.Version++;
        lesson.UpdatedAt = DateTimeOffset.UtcNow;
        db.ContentRevisions.Add(ContentVersioning.CreateRevision(
            lesson,
            userId,
            $"Status changed to {status}"));
        await db.SaveChangesAsync();
    }

    public async Task RollbackAsync(
        Guid lessonId,
        Guid revisionId,
        string userId)
    {
        var lesson = await db.Lessons.FindAsync(lessonId)
            ?? throw new InvalidOperationException("Lesson not found.");
        var revision = await db.ContentRevisions
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == revisionId && x.LessonId == lessonId)
            ?? throw new InvalidOperationException("Content revision not found.");

        ContentVersioning.ApplySnapshot(lesson, revision.SnapshotJson);
        lesson.Version++;
        lesson.Status = ContentStatus.Draft;
        lesson.UpdatedAt = DateTimeOffset.UtcNow;
        db.ContentRevisions.Add(ContentVersioning.CreateRevision(
            lesson,
            userId,
            $"Restored content from version {revision.Version}"));
        await db.SaveChangesAsync();
    }

    private static bool CanTransition(ContentStatus from, ContentStatus to) =>
        (from, to) switch
        {
            (ContentStatus.Draft, ContentStatus.InReview) => true,
            (ContentStatus.InReview, ContentStatus.ChangesRequested) => true,
            (ContentStatus.InReview, ContentStatus.Approved) => true,
            (ContentStatus.ChangesRequested, ContentStatus.Draft) => true,
            (ContentStatus.Approved, ContentStatus.Published) => true,
            (ContentStatus.Approved, ContentStatus.ChangesRequested) => true,
            (ContentStatus.Published, ContentStatus.Deprecated) => true,
            (ContentStatus.Deprecated, ContentStatus.Archived) => true,
            (_, ContentStatus.Archived) => true,
            _ => false
        };

    private static string RequiredText(string? value, int maxLength, string fieldName)
    {
        var cleaned = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            throw new ArgumentException($"{fieldName} is required.");
        }

        if (cleaned.Length > maxLength)
        {
            throw new ArgumentException($"{fieldName} cannot exceed {maxLength:N0} characters.");
        }

        return cleaned;
    }
}
