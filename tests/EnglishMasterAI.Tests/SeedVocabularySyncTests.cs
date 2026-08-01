using EnglishMasterAI.Web.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishMasterAI.Tests;

[Collection(EnglishMasterWebCollection.Name)]
public sealed class SeedVocabularySyncTests(EnglishMasterWebFactory factory)
{
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

        await SeedData.InitializeAsync(
            scope.ServiceProvider,
            factory.Services.GetRequiredService<IConfiguration>(),
            factory.Services.GetRequiredService<IWebHostEnvironment>());

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

        await SeedData.InitializeAsync(
            scope.ServiceProvider,
            factory.Services.GetRequiredService<IConfiguration>(),
            factory.Services.GetRequiredService<IWebHostEnvironment>());

        var refreshed = await db.VocabularyItems.SingleAsync(x => x.Id == id);
        Assert.Equal(thaiMeaning, refreshed.ThaiMeaning);
    }
}
