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
            .Include(x => x.Lessons)
            .ThenInclude(x => x.Questions)
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
            SyncLessonContent(db, existing, template);
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

    // Modules are only inserted when their code is missing, so anything a lesson gained in
    // content/curriculum/modules.json after the first seed would never reach an existing
    // database. Rows are matched on their natural key - the word, the prompt - and updated
    // in place rather than replaced, so the review schedules and quiz answers that point at
    // them survive a content revision.
    private static void SyncLessonContent(
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

            SyncLessonText(lesson, lessonTemplate);
            SyncVocabulary(db, lesson, lessonTemplate);
            SyncQuestions(db, lesson, lessonTemplate);
        }
    }

    // Lesson prose is editable in the content studio, which bumps Version, snapshots the
    // old text into a ContentRevision and reopens the reviews. Refreshing only while the
    // lesson is still at version 1 keeps the content files authoritative up to the point an
    // editor takes over, and silent about the lesson from then on.
    private static void SyncLessonText(Lesson lesson, Lesson template)
    {
        if (lesson.Version != 1)
        {
            return;
        }

        lesson.Title = template.Title;
        lesson.Objective = template.Objective;
        lesson.ThaiExplanation = template.ThaiExplanation;
        lesson.GrammarFocus = template.GrammarFocus;
        lesson.ReadingContent = template.ReadingContent;
        lesson.ListeningTranscript = template.ListeningTranscript;
        lesson.SpeakingPrompt = template.SpeakingPrompt;
        lesson.WritingPrompt = template.WritingPrompt;
        lesson.EstimatedMinutes = template.EstimatedMinutes;
        lesson.SortOrder = template.SortOrder;
    }

    private static void SyncVocabulary(
        ApplicationDbContext db,
        Lesson lesson,
        Lesson template)
    {
        var existingWords = lesson.Vocabulary
            .ToDictionary(x => x.Word, StringComparer.OrdinalIgnoreCase);
        foreach (var vocabularyTemplate in template.Vocabulary)
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
        }
    }

    private static void SyncQuestions(
        ApplicationDbContext db,
        Lesson lesson,
        Lesson template)
    {
        // A question the content file no longer carries has been retired rather than
        // reworded, and leaving it behind would let a lesson keep an item its own quiz no
        // longer contains. Nothing references a question row, so removing it is safe.
        var templatePrompts = template.Questions
            .Select(x => x.Prompt)
            .ToHashSet(StringComparer.Ordinal);
        db.AssessmentQuestions.RemoveRange(
            lesson.Questions.Where(x => !templatePrompts.Contains(x.Prompt)));

        var existingPrompts = lesson.Questions
            .GroupBy(x => x.Prompt, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        foreach (var questionTemplate in template.Questions)
        {
            if (!existingPrompts.TryGetValue(questionTemplate.Prompt, out var question))
            {
                questionTemplate.LessonId = lesson.Id;
                db.AssessmentQuestions.Add(questionTemplate);
                continue;
            }

            question.Skill = questionTemplate.Skill;
            question.ToeicPart = questionTemplate.ToeicPart;
            question.SupportingText = questionTemplate.SupportingText;
            question.OptionsJson = questionTemplate.OptionsJson;
            question.CorrectOptionIndex = questionTemplate.CorrectOptionIndex;
            question.Explanation = questionTemplate.Explanation;
            question.Difficulty = questionTemplate.Difficulty;
            question.SortOrder = questionTemplate.SortOrder;
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
