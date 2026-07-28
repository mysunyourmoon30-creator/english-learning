using EnglishMasterAI.Web.Data;
using EnglishMasterAI.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnglishMasterAI.Web.Application;

public sealed class ContentReviewService(ApplicationDbContext db)
{
    public async Task<IReadOnlyList<ContentReviewAssignment>> GetAssignmentsAsync(
        CancellationToken cancellationToken = default)
    {
        var assignments = await db.ContentReviewAssignments
            .AsNoTracking()
            .Include(x => x.Lesson)
            .ThenInclude(x => x.Module)
            .OrderBy(x => x.Lesson.Module.SortOrder)
            .ThenBy(x => x.ReviewerRole)
            .ToListAsync(cancellationToken);
        return assignments;
    }

    public async Task UpdateAsync(
        Guid assignmentId,
        string reviewerUserId,
        ContentReviewStatus status,
        string notes,
        CancellationToken cancellationToken = default)
    {
        var assignment = await db.ContentReviewAssignments.FindAsync(
            [assignmentId],
            cancellationToken)
            ?? throw new InvalidOperationException("Review assignment not found.");
        assignment.ReviewerUserId = reviewerUserId;
        assignment.Status = status;
        assignment.Notes = WritingFeedbackService.NormalizeText(notes, 1_000);
        assignment.ReviewedAt = status is ContentReviewStatus.Approved
            or ContentReviewStatus.ChangesRequested
            ? DateTimeOffset.UtcNow
            : null;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ContentReviewCoverage> GetCoverageAsync(
        CancellationToken cancellationToken = default)
    {
        var assignments = await db.ContentReviewAssignments
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return new ContentReviewCoverage(
            assignments.Count,
            assignments.Count(x => x.Status == ContentReviewStatus.Approved),
            assignments.Count(x => x.Status == ContentReviewStatus.ChangesRequested),
            assignments.Count(x => x.Status is ContentReviewStatus.Pending
                or ContentReviewStatus.InReview));
    }
}

public sealed record ContentReviewCoverage(
    int Total,
    int Approved,
    int ChangesRequested,
    int Pending);
