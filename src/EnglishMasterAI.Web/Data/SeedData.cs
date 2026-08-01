using System.Text.Json;
using EnglishMasterAI.Web.Application;
using EnglishMasterAI.Web.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnglishMasterAI.Web.Data;

public static class SeedData
{
    public static async Task InitializeAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        bool forceMigrationsAndSeed = false)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var databaseOptions = configuration
            .GetSection(Configuration.DatabaseOptions.SectionName)
            .Get<Configuration.DatabaseOptions>() ?? new Configuration.DatabaseOptions();
        if (forceMigrationsAndSeed || databaseOptions.ApplyMigrationsOnStartup)
        {
            await db.Database.MigrateAsync();
        }

        if (!forceMigrationsAndSeed && !databaseOptions.SeedOnStartup)
        {
            return;
        }

        await db.LearnerProfiles
            .Where(x => x.TimeZoneId == string.Empty)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    x => x.TimeZoneId,
                    "Asia/Bangkok"));

        await SeedRolesAndDevelopmentAdminAsync(services, configuration, environment);
        var contentRoot = environment.ContentRootPath;
        var moduleTemplates = SeedContent.LoadModules(contentRoot);
        var existingModules = await db.CourseModules
            .Include(x => x.Lessons)
            .ThenInclude(x => x.Vocabulary)
            .ToListAsync();
        var existingCodeSet = existingModules
            .Select(x => x.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in existingModules)
        {
            var template = moduleTemplates.SingleOrDefault(
                x => x.Code.Equals(existing.Code, StringComparison.OrdinalIgnoreCase));
            if (template is null)
            {
                continue;
            }

            existing.Title = template.Title;
            existing.TitleThai = template.TitleThai;
            existing.Summary = template.Summary;
            existing.Phase = template.Phase;
            existing.CefrLevel = template.CefrLevel;
            existing.Category = template.Category;
            existing.SortOrder = template.SortOrder;
            existing.EstimatedMinutes = template.EstimatedMinutes;
            SyncLessonVocabulary(db, existing, template);
        }
        var missingModules = moduleTemplates
            .Where(x => !existingCodeSet.Contains(x.Code))
            .ToList();
        if (missingModules.Count > 0)
        {
            db.CourseModules.AddRange(missingModules);
        }
        await db.SaveChangesAsync();

        if (!await db.AssessmentQuestions.AnyAsync(x => x.LessonId == null))
        {
            db.AssessmentQuestions.AddRange(SeedContent.LoadQuestions(
                contentRoot, "placement.json", AssessmentKind.Placement));
            db.AssessmentQuestions.AddRange(SeedContent.LoadQuestions(
                contentRoot, "toeic-diagnostic.json", AssessmentKind.ToeicDiagnostic));
            await db.SaveChangesAsync();
        }

        if (!await db.AssessmentQuestions.AnyAsync(x => x.Kind == AssessmentKind.ToeicMock))
        {
            db.AssessmentQuestions.AddRange(
                SeedContent.BuildToeicMockQuestions(contentRoot));
            await db.SaveChangesAsync();
        }

        if (!await db.AuditFindings.AnyAsync())
        {
            var lesson = await db.Lessons.SingleAsync(x => x.Slug == "rag-architecture");
            db.AuditFindings.Add(new AuditFinding
            {
                LessonId = lesson.Id,
                CreatedByUserId = "system-seed",
                AuditorRole = "Instructional Designer",
                Severity = AuditSeverity.Medium,
                Location = "RAG Architecture / Mini project",
                Issue = "The first draft should include an explicit evidence-check step.",
                Recommendation = "Ask learners to label the retrieved source used by the generated answer."
            });
            await db.SaveChangesAsync();
        }

        if (!await db.ContentRevisions.AnyAsync())
        {
            var lessons = await db.Lessons.AsNoTracking().ToListAsync();
            db.ContentRevisions.AddRange(lessons.Select(lesson =>
                ContentVersioning.CreateRevision(
                    lesson,
                    "system-seed",
                    "Initial seeded content")));
            await db.SaveChangesAsync();
        }

        var aiLessons = await db.Lessons
            .Include(x => x.Module)
            .Where(x => x.Module.Category == LearningCategory.AiEnglish)
            .ToListAsync();
        var existingReviewKeys = await db.ContentReviewAssignments
            .Select(x => new { x.LessonId, x.ReviewerRole })
            .ToListAsync();
        var reviewKeySet = existingReviewKeys
            .Select(x => $"{x.LessonId:N}:{x.ReviewerRole}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reviewerRoles = new[] { "English Reviewer", "AI Subject Matter Expert" };
        foreach (var lesson in aiLessons)
        {
            foreach (var reviewerRole in reviewerRoles)
            {
                if (reviewKeySet.Contains($"{lesson.Id:N}:{reviewerRole}"))
                {
                    continue;
                }

                db.ContentReviewAssignments.Add(new ContentReviewAssignment
                {
                    LessonId = lesson.Id,
                    ReviewerRole = reviewerRole,
                    Status = ContentReviewStatus.Pending
                });
            }
        }
        await db.SaveChangesAsync();
    }

    // Modules are only inserted when their code is missing, so vocabulary added to
    // content/curriculum/modules.json after the first seed would never reach an existing
    // database. Rows are matched by word and updated in place rather than replaced, so the
    // review schedules that point at them survive a content revision.
    private static void SyncLessonVocabulary(
        ApplicationDbContext db,
        CourseModule existing,
        CourseModule template)
    {
        foreach (var lesson in existing.Lessons)
        {
            var lessonTemplate = template.Lessons.SingleOrDefault(
                x => x.Slug.Equals(lesson.Slug, StringComparison.OrdinalIgnoreCase));
            if (lessonTemplate is null)
            {
                continue;
            }

            var existingWords = lesson.Vocabulary
                .ToDictionary(x => x.Word, StringComparer.OrdinalIgnoreCase);
            foreach (var vocabularyTemplate in lessonTemplate.Vocabulary)
            {
                if (!existingWords.TryGetValue(vocabularyTemplate.Word, out var vocabulary))
                {
                    // Added through the set rather than through lesson.Vocabulary: a template
                    // item already carries a generated key, so a change tracker that discovers
                    // it through the navigation reads it as an existing row and updates a
                    // record that was never inserted.
                    vocabularyTemplate.LessonId = lesson.Id;
                    db.VocabularyItems.Add(vocabularyTemplate);
                    continue;
                }

                vocabulary.ThaiMeaning = vocabularyTemplate.ThaiMeaning;
                vocabulary.Pronunciation = vocabularyTemplate.Pronunciation;
                vocabulary.WordForm = vocabularyTemplate.WordForm;
                vocabulary.Collocation = vocabularyTemplate.Collocation;
                vocabulary.ExampleSentence = vocabularyTemplate.ExampleSentence;
                vocabulary.IsCritical = vocabularyTemplate.IsCritical;
            }
        }
    }

    private static async Task SeedRolesAndDevelopmentAdminAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var roleName in new[] { "Admin", "ContentAdmin" })
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole(roleName));
            }
        }

        if (!environment.IsDevelopment() || !configuration.GetValue("SeedAdmin:Enabled", false))
        {
            return;
        }

        var email = configuration["SeedAdmin:Email"];
        var password = configuration["SeedAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await userManager.FindByEmailAsync(email);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };
            var result = await userManager.CreateAsync(admin, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Could not create development admin: {string.Join(", ", result.Errors.Select(x => x.Description))}");
            }
        }

        foreach (var roleName in new[] { "Admin", "ContentAdmin" })
        {
            if (!await userManager.IsInRoleAsync(admin, roleName))
            {
                await userManager.AddToRoleAsync(admin, roleName);
            }
        }
    }
}
