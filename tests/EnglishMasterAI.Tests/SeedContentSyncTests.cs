using EnglishMasterAI.Web.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishMasterAI.Tests;

[Collection(EnglishMasterWebCollection.Name)]
public sealed class SeedContentSyncTests(EnglishMasterWebFactory factory)
{
    private Task ReseedAsync(IServiceProvider scoped) =>
        SeedData.InitializeAsync(
            scoped,
            factory.Services.GetRequiredService<IConfiguration>(),
            factory.Services.GetRequiredService<IWebHostEnvironment>());

    [Fact]
    public async Task Reseeding_restores_vocabulary_added_after_the_module_was_created()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var lesson = await db.Lessons
            .Include(x => x.Vocabulary)
            .OrderBy(x => x.Slug)
            .FirstAsync(x => x.Vocabulary.Count > 1);
        var removed = lesson.Vocabulary.OrderBy(x => x.Word).First();
        var removedWord = removed.Word;
        var expectedCount = lesson.Vocabulary.Count;

        await db.ReviewSchedules
            .Where(x => x.VocabularyItemId == removed.Id)
            .ExecuteDeleteAsync();
        db.VocabularyItems.Remove(removed);
        await db.SaveChangesAsync();

        await ReseedAsync(scope.ServiceProvider);

        var words = await db.VocabularyItems
            .Where(x => x.LessonId == lesson.Id)
            .Select(x => x.Word)
            .ToListAsync();
        Assert.Equal(expectedCount, words.Count);
        Assert.Contains(removedWord, words);
    }

    [Fact]
    public async Task Reseeding_refreshes_an_existing_word_without_replacing_the_row()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var vocabulary = await db.VocabularyItems
            .OrderBy(x => x.Word)
            .FirstAsync();
        var id = vocabulary.Id;
        var thaiMeaning = vocabulary.ThaiMeaning;
        vocabulary.ThaiMeaning = "ความหมายที่ล้าสมัย";
        await db.SaveChangesAsync();

        await ReseedAsync(scope.ServiceProvider);

        var refreshed = await db.VocabularyItems.SingleAsync(x => x.Id == id);
        Assert.Equal(thaiMeaning, refreshed.ThaiMeaning);
    }

    [Fact]
    public async Task Reseeding_rewrites_a_stale_quiz_explanation_in_place()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var question = await db.AssessmentQuestions
            .Where(x => x.LessonId != null)
            .OrderBy(x => x.Prompt)
            .FirstAsync();
        var id = question.Id;
        var explanation = question.Explanation;
        question.Explanation = "Review the lesson example and try again.";
        await db.SaveChangesAsync();

        await ReseedAsync(scope.ServiceProvider);

        var refreshed = await db.AssessmentQuestions.SingleAsync(x => x.Id == id);
        Assert.Equal(explanation, refreshed.Explanation);
    }

    [Fact]
    public async Task Reseeding_restores_lesson_prose_that_no_editor_has_touched()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var lesson = await db.Lessons.OrderBy(x => x.Slug).FirstAsync(x => x.Version == 1);
        var id = lesson.Id;
        var transcript = lesson.ListeningTranscript;
        lesson.ListeningTranscript = lesson.ReadingContent;
        await db.SaveChangesAsync();

        await ReseedAsync(scope.ServiceProvider);

        var refreshed = await db.Lessons.SingleAsync(x => x.Id == id);
        Assert.Equal(transcript, refreshed.ListeningTranscript);
    }

    [Fact]
    public async Task Reseeding_leaves_a_lesson_alone_once_it_has_been_edited()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var lesson = await db.Lessons.OrderBy(x => x.Slug).FirstAsync(x => x.Version == 1);
        var id = lesson.Id;
        var seeded = lesson.ListeningTranscript;
        const string edited = "An editor rewrote this transcript in the content studio.";
        lesson.ListeningTranscript = edited;
        lesson.Version = 2;
        await db.SaveChangesAsync();

        await ReseedAsync(scope.ServiceProvider);

        var refreshed = await db.Lessons.SingleAsync(x => x.Id == id);
        Assert.Equal(edited, refreshed.ListeningTranscript);

        refreshed.ListeningTranscript = seeded;
        refreshed.Version = 1;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Reseeding_restores_a_quiz_question_that_is_missing_from_a_lesson()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var lesson = await db.Lessons
            .Include(x => x.Questions)
            .OrderBy(x => x.Slug)
            .FirstAsync(x => x.Questions.Count > 1);
        var removed = lesson.Questions.OrderBy(x => x.Prompt).First();
        var removedPrompt = removed.Prompt;
        var expectedCount = lesson.Questions.Count;

        db.AssessmentQuestions.Remove(removed);
        await db.SaveChangesAsync();

        await ReseedAsync(scope.ServiceProvider);

        var prompts = await db.AssessmentQuestions
            .Where(x => x.LessonId == lesson.Id)
            .Select(x => x.Prompt)
            .ToListAsync();
        Assert.Equal(expectedCount, prompts.Count);
        Assert.Contains(removedPrompt, prompts);
    }
}
