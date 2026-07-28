using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using EnglishMasterAI.Web.Domain;

namespace EnglishMasterAI.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<LearnerProfile> LearnerProfiles => Set<LearnerProfile>();
    public DbSet<CourseModule> CourseModules => Set<CourseModule>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<VocabularyItem> VocabularyItems => Set<VocabularyItem>();
    public DbSet<AssessmentQuestion> AssessmentQuestions => Set<AssessmentQuestion>();
    public DbSet<LearningProgress> LearningProgress => Set<LearningProgress>();
    public DbSet<PlacementAttempt> PlacementAttempts => Set<PlacementAttempt>();
    public DbSet<ReviewSchedule> ReviewSchedules => Set<ReviewSchedule>();
    public DbSet<WritingSubmission> WritingSubmissions => Set<WritingSubmission>();
    public DbSet<SpeakingSubmission> SpeakingSubmissions => Set<SpeakingSubmission>();
    public DbSet<ContentRevision> ContentRevisions => Set<ContentRevision>();
    public DbSet<AuditFinding> AuditFindings => Set<AuditFinding>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<LearnerProfile>()
            .HasIndex(x => x.UserId)
            .IsUnique();

        builder.Entity<CourseModule>()
            .HasIndex(x => x.Code)
            .IsUnique();

        builder.Entity<Lesson>()
            .HasIndex(x => x.Slug)
            .IsUnique();

        builder.Entity<LearningProgress>()
            .HasIndex(x => new { x.UserId, x.LessonId })
            .IsUnique();

        builder.Entity<ReviewSchedule>()
            .HasIndex(x => new { x.UserId, x.VocabularyItemId })
            .IsUnique();

        builder.Entity<ContentRevision>()
            .HasIndex(x => new { x.LessonId, x.Version });

        builder.Entity<CourseModule>()
            .Property(x => x.Category)
            .HasConversion<string>();

        builder.Entity<Lesson>()
            .Property(x => x.Status)
            .HasConversion<string>();

        builder.Entity<AssessmentQuestion>()
            .Property(x => x.Kind)
            .HasConversion<string>();

        builder.Entity<AuditFinding>()
            .Property(x => x.Severity)
            .HasConversion<string>();

        builder.Entity<ContentRevision>()
            .Property(x => x.Status)
            .HasConversion<string>();

        builder.Entity<CourseModule>()
            .HasMany(x => x.Lessons)
            .WithOne(x => x.Module)
            .HasForeignKey(x => x.CourseModuleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Lesson>()
            .HasMany(x => x.Vocabulary)
            .WithOne(x => x.Lesson)
            .HasForeignKey(x => x.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Lesson>()
            .HasMany(x => x.Questions)
            .WithOne(x => x.Lesson)
            .HasForeignKey(x => x.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ContentRevision>()
            .HasOne(x => x.Lesson)
            .WithMany()
            .HasForeignKey(x => x.LessonId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
