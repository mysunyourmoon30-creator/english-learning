using System.Text.Json;
using System.Text.Json.Serialization;
using EnglishMasterAI.Web.Domain;

namespace EnglishMasterAI.Web.Data;

/// <summary>
/// Reads the seeded curriculum and assessment content from content/curriculum.
/// The material itself is reserved rather than covered by the project licence,
/// so it lives in data files instead of being embedded in source. See NOTICE.
/// </summary>
public static class SeedContent
{
    public const string ContentDirectory = "content/curriculum";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static IReadOnlyList<CourseModule> LoadModules(string contentRoot) =>
        Read<List<ModuleDto>>(contentRoot, "modules.json")
            .Select(ToModule)
            .ToList();

    public static IReadOnlyList<AssessmentQuestion> LoadQuestions(
        string contentRoot,
        string fileName,
        AssessmentKind kind) =>
        Read<List<QuestionDto>>(contentRoot, fileName)
            .Select(q => ToQuestion(q, kind))
            .ToList();

    public static IReadOnlyList<AssessmentQuestion> BuildToeicMockQuestions(string contentRoot)
    {
        var seeds = Read<MockSeedsDto>(contentRoot, "toeic-mock-seeds.json");
        var questions = new List<AssessmentQuestion>(200);

        void Add(int part, string prompt, string correct, IReadOnlyList<string> distractors, string? supportingText = null)
        {
            var correctIndex = questions.Count % 4;
            var options = distractors.ToList();
            options.Insert(correctIndex, correct);
            questions.Add(new AssessmentQuestion
            {
                Kind = AssessmentKind.ToeicMock,
                Skill = part <= 4 ? "TOEIC Listening" : "TOEIC Reading",
                ToeicPart = part,
                Prompt = $"Part {part} — {prompt}",
                SupportingText = supportingText,
                OptionsJson = JsonSerializer.Serialize(options),
                CorrectOptionIndex = correctIndex,
                Explanation = $"The best answer is “{correct}”.",
                Difficulty = part is 3 or 4 or 7 ? 3 : 2,
                SortOrder = questions.Count + 1
            });
        }

        foreach (var scene in seeds.PartOne.Scenes)
        {
            Add(
                1,
                seeds.PartOne.PromptTemplate.Replace("{scene}", scene.Scene),
                scene.Correct,
                [scene.D1, scene.D2, scene.D3]);
        }

        for (var index = 0; index < seeds.PartTwo.QuestionCount; index++)
        {
            var template = seeds.PartTwo.Templates[index % seeds.PartTwo.Templates.Count];
            string Fill(string value) => value
                .Replace("{number}", (index + 1).ToString())
                .Replace("{day}", seeds.PartTwo.Days[index % seeds.PartTwo.Days.Count])
                .Replace("{floor}", ((index % 8) + 2).ToString())
                .Replace("{name}", seeds.PartTwo.Names[index % seeds.PartTwo.Names.Count])
                .Replace("{months}", ((index % 4) + 1).ToString());

            Add(2, Fill(template.Prompt), Fill(template.Correct), template.Distractors);
        }

        foreach (var conversation in seeds.PartThree.Conversations)
        {
            foreach (var question in seeds.PartThree.Questions)
            {
                Add(
                    3,
                    question.Prompt,
                    question.Answer switch
                    {
                        "location" => conversation.Location,
                        "purpose" => conversation.Purpose,
                        "nextAction" => conversation.NextAction,
                        _ => throw new InvalidOperationException(
                            $"Unknown part three answer field '{question.Answer}'.")
                    },
                    question.Distractors,
                    conversation.Dialogue);
            }
        }

        foreach (var talk in seeds.PartFour.Talks)
        {
            foreach (var question in seeds.PartFour.Questions)
            {
                Add(
                    4,
                    question.Prompt.Replace("{timeReference}", talk.TimeReference),
                    question.Answer switch
                    {
                        "purpose" => talk.Purpose,
                        "location" => talk.Location,
                        "action" => talk.Action,
                        _ => throw new InvalidOperationException(
                            $"Unknown part four answer field '{question.Answer}'.")
                    },
                    question.Distractors,
                    talk.Text);
            }
        }

        foreach (var item in seeds.PartFive.Items)
        {
            Add(5, item.Prompt, item.Correct, [item.D1, item.D2, item.D3]);
        }

        foreach (var passage in seeds.PartSix.Passages)
        {
            foreach (var question in seeds.PartSix.Questions)
            {
                var (correct, distractors) = question.Answer switch
                {
                    "one" => (passage.CorrectOne, (IReadOnlyList<string>)[passage.OneA, passage.OneB, passage.OneC]),
                    "two" => (passage.CorrectTwo, [passage.TwoA, passage.TwoB, passage.TwoC]),
                    "purpose" => (passage.Purpose, question.Distractors),
                    "detail" => (passage.Detail, question.Distractors),
                    _ => throw new InvalidOperationException(
                        $"Unknown part six answer field '{question.Answer}'.")
                };
                Add(6, question.Prompt, correct, distractors, passage.Text);
            }
        }

        foreach (var passage in seeds.PartSeven.Passages)
        {
            foreach (var question in seeds.PartSeven.Questions)
            {
                Add(
                    7,
                    question.Prompt,
                    question.Answer switch
                    {
                        "writer" => passage.Writer,
                        "purpose" => passage.Purpose,
                        "date" => passage.Date,
                        "action" => passage.Action,
                        "location" => passage.Location,
                        "detail" => passage.Detail,
                        _ => throw new InvalidOperationException(
                            $"Unknown part seven answer field '{question.Answer}'.")
                    },
                    question.Distractors,
                    passage.Text);
            }
        }

        if (questions.Count != 200)
        {
            throw new InvalidOperationException(
                $"TOEIC mock seed must contain exactly 200 questions; found {questions.Count}.");
        }

        return questions;
    }

    /// <summary>
    /// Resolves against the content root first, then the output directory, so the
    /// files work both when running from the repository and from a published app.
    /// </summary>
    public static string ResolvePath(string contentRoot, string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(contentRoot, ContentDirectory, fileName));
        if (File.Exists(path))
        {
            return path;
        }

        var fallback = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, ContentDirectory, fileName));
        if (File.Exists(fallback))
        {
            return fallback;
        }

        throw new FileNotFoundException(
            $"Seed content file '{fileName}' was not found at '{path}' or '{fallback}'.",
            path);
    }

    private static T Read<T>(string contentRoot, string fileName)
    {
        var path = ResolvePath(contentRoot, fileName);
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
            ?? throw new InvalidOperationException(
                $"Seed content file '{fileName}' deserialized to null.");
    }

    private static CourseModule ToModule(ModuleDto dto)
    {
        var module = new CourseModule
        {
            Code = dto.Code,
            Title = dto.Title,
            TitleThai = dto.TitleThai,
            Summary = dto.Summary,
            Phase = dto.Phase,
            CefrLevel = dto.CefrLevel,
            Category = Enum.Parse<LearningCategory>(dto.Category),
            SortOrder = dto.SortOrder,
            EstimatedMinutes = dto.EstimatedMinutes,
            IsPublished = dto.IsPublished
        };

        foreach (var lessonDto in dto.Lessons)
        {
            var lesson = new Lesson
            {
                Slug = lessonDto.Slug,
                Title = lessonDto.Title,
                Objective = lessonDto.Objective,
                ThaiExplanation = lessonDto.ThaiExplanation,
                GrammarFocus = lessonDto.GrammarFocus,
                ReadingContent = lessonDto.ReadingContent,
                ListeningTranscript = lessonDto.ListeningTranscript,
                SpeakingPrompt = lessonDto.SpeakingPrompt,
                WritingPrompt = lessonDto.WritingPrompt,
                EstimatedMinutes = lessonDto.EstimatedMinutes,
                SortOrder = lessonDto.SortOrder
            };
            lesson.Vocabulary.AddRange(lessonDto.Vocabulary.Select(v => new VocabularyItem
            {
                Word = v.Word,
                ThaiMeaning = v.ThaiMeaning,
                Pronunciation = v.Pronunciation,
                WordForm = v.WordForm,
                Collocation = v.Collocation,
                ExampleSentence = v.ExampleSentence,
                IsCritical = v.IsCritical
            }));
            lesson.Questions.AddRange(
                lessonDto.Questions.Select(q => ToQuestion(q, AssessmentKind.LessonQuiz)));
            module.Lessons.Add(lesson);
        }

        return module;
    }

    private static AssessmentQuestion ToQuestion(QuestionDto dto, AssessmentKind kind) =>
        new()
        {
            Kind = kind,
            Skill = dto.Skill,
            ToeicPart = dto.ToeicPart,
            Prompt = dto.Prompt,
            SupportingText = dto.SupportingText,
            OptionsJson = JsonSerializer.Serialize(dto.Options),
            CorrectOptionIndex = dto.CorrectOptionIndex,
            Explanation = dto.Explanation,
            Difficulty = dto.Difficulty,
            SortOrder = dto.SortOrder
        };

    private sealed record ModuleDto(
        string Code,
        string Title,
        string TitleThai,
        string Summary,
        string Phase,
        string CefrLevel,
        string Category,
        int SortOrder,
        int EstimatedMinutes,
        bool IsPublished,
        List<LessonDto> Lessons);

    private sealed record LessonDto(
        string Slug,
        string Title,
        string Objective,
        string ThaiExplanation,
        string GrammarFocus,
        string ReadingContent,
        string ListeningTranscript,
        string SpeakingPrompt,
        string WritingPrompt,
        int EstimatedMinutes,
        int SortOrder,
        List<VocabularyDto> Vocabulary,
        List<QuestionDto> Questions);

    private sealed record VocabularyDto(
        string Word,
        string ThaiMeaning,
        string Pronunciation,
        string WordForm,
        string Collocation,
        string ExampleSentence,
        bool IsCritical);

    private sealed record QuestionDto(
        string Skill,
        int? ToeicPart,
        string Prompt,
        string? SupportingText,
        List<string> Options,
        int CorrectOptionIndex,
        string Explanation,
        int Difficulty,
        int SortOrder);

    private sealed record MockSeedsDto(
        PartOneDto PartOne,
        PartTwoDto PartTwo,
        PartThreeDto PartThree,
        PartFourDto PartFour,
        PartFiveDto PartFive,
        PartSixDto PartSix,
        PartSevenDto PartSeven);

    private sealed record PartOneDto(string PromptTemplate, List<SceneDto> Scenes);

    private sealed record SceneDto(string Scene, string Correct, string D1, string D2, string D3);

    private sealed record PartTwoDto(
        int QuestionCount,
        List<string> Days,
        List<string> Names,
        List<PartTwoTemplateDto> Templates);

    private sealed record PartTwoTemplateDto(string Prompt, string Correct, List<string> Distractors);

    private sealed record TemplatedQuestionDto(string Prompt, string Answer, List<string> Distractors);

    private sealed record PartThreeDto(
        List<TemplatedQuestionDto> Questions,
        List<ConversationDto> Conversations);

    private sealed record ConversationDto(
        string Location,
        string Purpose,
        string NextAction,
        string Dialogue);

    private sealed record PartFourDto(List<TemplatedQuestionDto> Questions, List<TalkDto> Talks);

    private sealed record TalkDto(
        string Purpose,
        string Location,
        string Action,
        string TimeReference,
        string Text);

    private sealed record PartFiveDto(List<PartFiveItemDto> Items);

    private sealed record PartFiveItemDto(
        string Prompt,
        string Correct,
        string D1,
        string D2,
        string D3);

    private sealed record PartSixDto(
        List<TemplatedQuestionDto> Questions,
        List<PartSixPassageDto> Passages);

    private sealed record PartSixPassageDto(
        string Text,
        string CorrectOne,
        string OneA,
        string OneB,
        string OneC,
        string CorrectTwo,
        string TwoA,
        string TwoB,
        string TwoC,
        string Purpose,
        string Detail);

    private sealed record PartSevenDto(
        List<TemplatedQuestionDto> Questions,
        List<PartSevenPassageDto> Passages);

    private sealed record PartSevenPassageDto(
        string Writer,
        string Purpose,
        string Date,
        string Action,
        string Location,
        string Detail,
        string Text);
}
