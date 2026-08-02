using System.Text.Json;
using EnglishMasterAI.Web.Data;
using EnglishMasterAI.Web.Domain;

namespace EnglishMasterAI.Tests;

public sealed class SeedContentTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "EnglishMasterAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static readonly string Root = RepositoryRoot();

    [Fact]
    public void Modules_load_with_lessons_vocabulary_and_quizzes()
    {
        var modules = SeedContent.LoadModules(Root);

        Assert.NotEmpty(modules);
        Assert.Equal(
            modules.Select(x => x.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            modules.Count);
        Assert.All(modules, module =>
        {
            Assert.False(string.IsNullOrWhiteSpace(module.Code));
            Assert.False(string.IsNullOrWhiteSpace(module.TitleThai));
            Assert.NotEmpty(module.Lessons);
            Assert.All(module.Lessons, lesson =>
            {
                Assert.False(string.IsNullOrWhiteSpace(lesson.Slug));
                Assert.NotEmpty(lesson.Vocabulary);
                Assert.NotEmpty(lesson.Questions);
                Assert.All(lesson.Questions, q =>
                    Assert.Equal(AssessmentKind.LessonQuiz, q.Kind));
            });
        });
    }

    [Fact]
    public void Every_lesson_carries_a_usable_vocabulary_set()
    {
        var lessons = SeedContent.LoadModules(Root).SelectMany(x => x.Lessons);

        Assert.All(lessons, lesson =>
        {
            Assert.True(
                lesson.Vocabulary.Count >= 3,
                $"Lesson '{lesson.Slug}' has {lesson.Vocabulary.Count} vocabulary items.");
            Assert.Equal(
                lesson.Vocabulary
                    .Select(x => x.Word)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                lesson.Vocabulary.Count);
            Assert.All(lesson.Vocabulary, vocabulary =>
            {
                Assert.False(string.IsNullOrWhiteSpace(vocabulary.Word));
                Assert.False(string.IsNullOrWhiteSpace(vocabulary.ThaiMeaning));
                Assert.False(string.IsNullOrWhiteSpace(vocabulary.WordForm));
                Assert.False(string.IsNullOrWhiteSpace(vocabulary.Collocation));
                Assert.StartsWith("/", vocabulary.Pronunciation, StringComparison.Ordinal);
                Assert.EndsWith("/", vocabulary.Pronunciation, StringComparison.Ordinal);
                // Compared on a stem so an inflected example ("evolve" for "evolution")
                // still counts, while an example that never uses the word does not.
                var head = vocabulary.Word.Split(' ')[0];
                Assert.Contains(
                    head[..Math.Min(4, head.Length)],
                    vocabulary.ExampleSentence,
                    StringComparison.OrdinalIgnoreCase);
            });
        });
    }

    [Fact]
    public void Listening_transcripts_are_not_a_copy_of_the_reading_passage()
    {
        var lessons = SeedContent.LoadModules(Root).SelectMany(x => x.Lessons);

        Assert.All(lessons, lesson =>
        {
            // The player shows the reading in full and then asks the learner to listen
            // before opening the transcript, which only works if they differ.
            Assert.NotEqual(
                lesson.ReadingContent.Trim(),
                lesson.ListeningTranscript.Trim(),
                StringComparer.OrdinalIgnoreCase);
            Assert.True(
                lesson.ListeningTranscript.Split(' ').Length >= 30,
                $"Transcript for '{lesson.Slug}' is too short to listen to.");
        });
    }

    [Fact]
    public void Every_lesson_quiz_question_explains_its_own_answer()
    {
        var lessons = SeedContent.LoadModules(Root).SelectMany(x => x.Lessons);

        Assert.All(lessons, lesson =>
        {
            // The sync in SeedData matches a seeded question to its template on the
            // prompt, so two questions sharing one within a lesson would collide.
            Assert.Equal(
                lesson.Questions
                    .Select(x => x.Prompt)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                lesson.Questions.Count);
            Assert.All(lesson.Questions, question =>
            {
                var options = JsonSerializer.Deserialize<string[]>(question.OptionsJson)!;
                Assert.DoesNotContain(
                    "Review the lesson example and try again",
                    question.Explanation,
                    StringComparison.OrdinalIgnoreCase);
                Assert.Contains(
                    options[question.CorrectOptionIndex],
                    question.Explanation,
                    StringComparison.Ordinal);
                Assert.True(
                    question.Explanation.Length >= 80,
                    $"Explanation for '{question.Prompt}' is too short to say why.");
            });
        });
    }

    [Fact]
    public void Lesson_slugs_are_unique_across_modules()
    {
        var slugs = SeedContent.LoadModules(Root)
            .SelectMany(x => x.Lessons)
            .Select(x => x.Slug)
            .ToList();

        Assert.Equal(slugs.Distinct(StringComparer.OrdinalIgnoreCase).Count(), slugs.Count);
    }

    [Theory]
    [InlineData("placement.json", AssessmentKind.Placement)]
    [InlineData("toeic-diagnostic.json", AssessmentKind.ToeicDiagnostic)]
    public void Standalone_question_sets_load_with_the_requested_kind(
        string fileName,
        AssessmentKind kind)
    {
        var questions = SeedContent.LoadQuestions(Root, fileName, kind);

        Assert.NotEmpty(questions);
        Assert.All(questions, q => Assert.Equal(kind, q.Kind));
        AssertAnswerable(questions);
    }

    [Fact]
    public void Toeic_mock_builds_two_hundred_answerable_questions()
    {
        var questions = SeedContent.BuildToeicMockQuestions(Root);

        Assert.Equal(200, questions.Count);
        AssertAnswerable(questions);
        Assert.All(questions, q => Assert.InRange(q.ToeicPart ?? 0, 1, 7));
        Assert.Equal(
            Enumerable.Range(1, 200),
            questions.Select(x => x.SortOrder));
    }

    [Fact]
    public void Toeic_mock_spreads_the_correct_answer_across_all_positions()
    {
        var byPosition = SeedContent.BuildToeicMockQuestions(Root)
            .GroupBy(x => x.CorrectOptionIndex)
            .ToDictionary(x => x.Key, x => x.Count());

        Assert.Equal(4, byPosition.Count);
        Assert.All(byPosition.Values, count => Assert.Equal(50, count));
    }

    private static void AssertAnswerable(IReadOnlyList<AssessmentQuestion> questions)
    {
        Assert.All(questions, question =>
        {
            var options = JsonSerializer.Deserialize<string[]>(question.OptionsJson);
            Assert.NotNull(options);
            Assert.Equal(4, options!.Length);
            Assert.All(options, option => Assert.False(string.IsNullOrWhiteSpace(option)));
            Assert.InRange(question.CorrectOptionIndex, 0, options.Length - 1);
            Assert.False(string.IsNullOrWhiteSpace(question.Prompt));
            Assert.Contains(options[question.CorrectOptionIndex], question.Explanation);
        });
    }
}
