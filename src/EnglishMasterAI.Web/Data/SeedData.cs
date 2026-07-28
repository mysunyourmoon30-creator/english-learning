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
        var moduleTemplates = BuildModules();
        var existingModules = await db.CourseModules.ToListAsync();
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
            db.AssessmentQuestions.AddRange(BuildPlacementQuestions());
            db.AssessmentQuestions.AddRange(BuildToeicQuestions());
            await db.SaveChangesAsync();
        }

        if (!await db.AssessmentQuestions.AnyAsync(x => x.Kind == AssessmentKind.ToeicMock))
        {
            db.AssessmentQuestions.AddRange(BuildToeicMockQuestions());
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

    private static IReadOnlyList<CourseModule> BuildModules()
    {
        return
        [
            Module("E01", "Alphabet, Sounds & Basic Pronunciation", "ตัวอักษร เสียง และการออกเสียงพื้นฐาน",
                "อ่านตัวอักษร สะกดคำ และแยกเสียงที่ผู้เรียนไทยมักสับสน", "Phase 1", "Pre-A1", LearningCategory.Foundation, 1,
                LessonData(
                    "alphabet-and-sounds",
                    "Read new words with a sound-first method",
                    "แยกตัวอักษร ชื่ออักษร และเสียงของอักษรให้ชัด เริ่มจากเสียงท้ายคำและคู่เสียง R/L, V/W, TH ก่อนใช้ IPA เท่าที่จำเป็น",
                    "A letter has a name, but it can represent different sounds. Focus on the sound inside a real word.",
                    "Listen to the final sound. The word work ends with /k/. The word live ends with /v/. Clear final sounds make your message easier to understand.",
                    "Say: very, work, reliable, three. Then spell your name and one technical word.",
                    "Write four short sentences about the sounds you want to improve.",
                    [
                        Vocab("sound", "เสียง", "/saʊnd/", "noun / verb", "final sound", "Listen to the final sound."),
                        Vocab("spell", "สะกดคำ", "/spel/", "verb", "spell a word", "Can you spell your name?"),
                        Vocab("syllable", "พยางค์", "/ˈsɪl.ə.bəl/", "noun", "stress a syllable", "Architecture has four syllables.")
                    ],
                    [
                        Quiz("Pronunciation", "Which word ends with the /v/ sound?", 1, "work", "live", "write", "light"),
                        Quiz("Pronunciation", "How many syllables are in “model”?", 1, "one", "two", "three", "four")
                    ])),

            Module("E02", "Phonics & Word Reading", "โฟนิกส์และการอ่านคำ",
                "แบ่งพยางค์ อ่าน vowel patterns และเดาคำเทคนิคอย่างมีหลักการ", "Phase 1", "A1", LearningCategory.Foundation, 2,
                LessonData(
                    "phonics-word-reading",
                    "Break long technical words into readable chunks",
                    "ไม่ต้องจำกฎ phonics ทุกข้อ ให้มอง vowel pattern, prefix, suffix และแบ่งคำยาวเป็นพยางค์ก่อน",
                    "Prefixes and suffixes give clues. Re-li-a-bil-i-ty becomes easier when you read it in chunks.",
                    "First, find the vowels. Next, divide the word into chunks. Then, mark the stressed syllable. Finally, read the complete word.",
                    "Break down and read: architecture, embedding, reliability, validation.",
                    "Divide five technical words into syllables and mark the stressed part.",
                    [
                        Vocab("prefix", "คำเติมหน้า", "/ˈpriː.fɪks/", "noun", "common prefix", "The prefix re- often means again."),
                        Vocab("suffix", "คำเติมท้าย", "/ˈsʌf.ɪks/", "noun", "add a suffix", "The suffix -able changes the word form."),
                        Vocab("stress", "การเน้นเสียง", "/stres/", "noun / verb", "word stress", "Correct word stress makes speech clearer.")
                    ],
                    [
                        Quiz("Reading", "Which is the best chunking for “reliability”?", 2, "rel-iab-il-ity", "reli-abili-ty", "re-li-a-bil-i-ty", "r-eliability"),
                        Quiz("Vocabulary", "A suffix is added to the…", 1, "beginning of a word", "end of a word", "middle of a sentence", "sound only")
                    ])),

            Module("E03", "Core Vocabulary & Chunks", "คำศัพท์แกนกลางและวลีพร้อมใช้",
                "เรียนคำเป็น collocation และ chunks พร้อมระบบทบทวนแบบเว้นระยะ", "Phase 1", "A1", LearningCategory.Vocabulary, 3,
                LessonData(
                    "core-vocabulary-chunks",
                    "Use six high-frequency technical chunks in complete sentences",
                    "จำคำพร้อมเสียง รูปคำ collocation และประโยคตัวอย่าง เช่น make a request, handle an error และ retrieve data",
                    "A useful chunk is easier to retrieve than an isolated word. Learn “send a response,” not only “response.”",
                    "The client makes a request. The API validates the input and sends a response. If the request fails, the system handles the error.",
                    "Explain a simple API flow using make a request, send a response, and handle an error.",
                    "Write one sentence for each chunk: retrieve data, deploy an application, evaluate the result.",
                    [
                        Vocab("request", "คำขอ", "/rɪˈkwest/", "noun / verb", "make a request", "The client makes a request."),
                        Vocab("response", "การตอบกลับ", "/rɪˈspɒns/", "noun", "send a response", "The API sends a JSON response."),
                        Vocab("retrieve", "ดึงกลับมา", "/rɪˈtriːv/", "verb", "retrieve data", "The service retrieves customer data."),
                        Vocab("evaluate", "ประเมิน", "/ɪˈvæl.ju.eɪt/", "verb", "evaluate the result", "We evaluate the result before release.")
                    ],
                    [
                        Quiz("Vocabulary", "Choose the natural collocation.", 2, "do a request", "build a request", "make a request", "cook a request"),
                        Quiz("Vocabulary", "The service ___ data from the database.", 1, "returns to", "retrieves", "sends at", "evaluations")
                    ])),

            Module("E04", "Sentence Building", "การสร้างประโยค",
                "มองประธาน กริยา กรรม และส่วนเติมเต็มในประโยคจริง", "Phase 1", "A1", LearningCategory.Grammar, 4,
                LessonData(
                    "sentence-building",
                    "Build accurate Subject + Verb + Object sentences",
                    "ทุกประโยคหลักต้องมองหาใครหรืออะไรเป็นประธาน และเกิดการกระทำหรือสถานะใด จากนั้นตรวจว่ากริยาต้องการกรรมหรือไม่",
                    "Subject + Verb: The service runs. Subject + Verb + Object: The model generates text. Subject + be + adjective: The system is reliable.",
                    "The client sends a request. The API validates the input. The model generates an answer. The response is useful.",
                    "Describe a four-step system. Start every sentence with a clear subject.",
                    "Write four sentences about an AI application. Underline the subject and circle the main verb.",
                    [
                        Vocab("subject", "ประธาน", "/ˈsʌb.dʒekt/", "noun", "sentence subject", "The subject performs the action."),
                        Vocab("verb", "คำกริยา", "/vɜːb/", "noun", "main verb", "Every complete clause needs a verb."),
                        Vocab("object", "กรรม", "/ˈɒb.dʒekt/", "noun", "direct object", "Text is the object in “The model generates text.”")
                    ],
                    [
                        Quiz("Grammar", "Which sentence has Subject + Verb + Object?", 2, "The API is reliable.", "The service runs.", "The model generates text.", "The output is JSON."),
                        Quiz("Grammar", "Choose the correct sentence.", 1, "The system reliable.", "The system is reliable.", "The system is reliability.", "System are reliable.")
                    ])),

            Module("E05", "Nouns, Pronouns & Articles", "คำนาม สรรพนาม และคำนำหน้านาม",
                "ใช้ a, an, the และคำนามนับได้/นับไม่ได้ในบริบทข้อมูลและโมเดล", "Phase 2", "A1", LearningCategory.Grammar, 5,
                CompactLesson("nouns-pronouns-articles", "Use nouns and articles accurately with AI vocabulary",
                    "Data และ information มักเป็นคำนามนับไม่ได้ จึงใช้ some data, much information หรือ a piece of information",
                    "The model uses some data. It returns a piece of information.",
                    Vocab("information", "ข้อมูลสารสนเทศ", "/ˌɪn.fəˈmeɪ.ʃən/", "uncountable noun", "a piece of information", "The response contains useful information."),
                    Quiz("Grammar", "Choose the correct phrase.", 2, "an information", "many information", "a piece of information", "informations"))),

            Module("E06", "Verb System", "ระบบคำกริยา",
                "เข้าใจ V1, V2, V3, V-ing, V-s และกริยาช่วยก่อนเรียน tense", "Phase 2", "A1", LearningCategory.Grammar, 6,
                CompactLesson("verb-system", "Choose the correct verb form after auxiliaries",
                    "จำสูตรหลัก: Modal + V1, have + V3, be + V-ing และประธาน he/she/it ใช้ V-s ใน Present Simple",
                    "The service can process requests. The team has deployed the update.",
                    Vocab("process", "ประมวลผล", "/ˈprəʊ.ses/", "verb / noun", "process a request", "The service processes each request."),
                    Quiz("Grammar", "The model can ___ text.", 1, "generates", "generate", "generated", "generating"))),

            Module("E07", "Essential Tenses", "กาลที่จำเป็น",
                "ใช้ present, past, perfect และ future กับสถานการณ์ทำงาน", "Phase 2", "A2", LearningCategory.Grammar, 7,
                CompactLesson("essential-tenses", "Report current, past, completed, and future work",
                    "Present Simple ใช้กับระบบทำงานทั่วไป Past Simple ใช้กับเหตุการณ์จบแล้ว Present Perfect เชื่อมผลถึงปัจจุบัน",
                    "We deployed the service yesterday. The team has completed the evaluation. We will improve the pipeline.",
                    Vocab("deploy", "นำระบบขึ้นใช้งาน", "/dɪˈplɔɪ/", "verb", "deploy a service", "We deployed the service yesterday."),
                    Quiz("Grammar", "We ___ the service yesterday.", 2, "deploy", "have deploy", "deployed", "will deployed"))),

            Module("E08", "Questions, Negatives & Modals", "คำถาม ปฏิเสธ และ Modal verbs",
                "ถามข้อมูล ขอความช่วยเหลือ และระบุข้อกำหนดในที่ประชุม", "Phase 2", "A2", LearningCategory.Grammar, 8,
                CompactLesson("questions-negatives-modals", "Ask clear technical questions and state requirements",
                    "Wh-question ใช้ question word + auxiliary + subject + verb ส่วน modal ตามด้วย V1 เสมอ",
                    "What does this function return? Could you explain the architecture? The system must validate the input.",
                    Vocab("clarify", "ทำให้ชัดเจน", "/ˈklær.ɪ.faɪ/", "verb", "clarify a requirement", "Could you clarify the requirement?"),
                    Quiz("Grammar", "Choose the correct question.", 1, "What this function returns?", "What does this function return?", "What does return this function?", "What this function does return?"))),

            Module("E09", "Description & Connection", "การบรรยายและเชื่อมความคิด",
                "เชื่อมลำดับ เหตุผล ผลลัพธ์ และการเปรียบเทียบ", "Phase 2", "A2", LearningCategory.Grammar, 9,
                CompactLesson("description-and-connection", "Explain a process with sequence and cause-effect words",
                    "ใช้ first, next, then, finally สำหรับลำดับ because สำหรับเหตุผล และ therefore/as a result สำหรับผลลัพธ์",
                    "First, the system retrieves documents. Next, it ranks them. Finally, the model generates an answer.",
                    Vocab("therefore", "ดังนั้น", "/ˈðeə.fɔːr/", "adverb", "and therefore", "The input was invalid; therefore, the request failed."),
                    Quiz("Grammar", "Which connector introduces a result?", 2, "although", "because", "therefore", "unless"))),

            Module("E10", "Work & TOEIC Grammar", "ไวยากรณ์สำหรับงานและ TOEIC",
                "ใช้ passive voice, relative clauses, word forms และ conditionals ที่พบบ่อย", "Phase 2", "B1", LearningCategory.Grammar, 10,
                CompactLesson("work-toeic-grammar", "Recognize high-frequency workplace grammar patterns",
                    "Passive voice ใช้ be + V3 เมื่อต้องการเน้นสิ่งที่ถูกกระทำ เช่น The input is validated before processing.",
                    "The report was reviewed by the team. If the test passes, we will publish the release.",
                    Vocab("validate", "ตรวจสอบความถูกต้อง", "/ˈvæl.ɪ.deɪt/", "verb", "validate input", "All input is validated before processing."),
                    Quiz("Grammar", "Choose the correct passive sentence.", 3, "The input validates.", "The input is validate.", "The input validating.", "The input is validated."))),

            ..BuildSkillModules(),

            Module("AI02", "LLM Architecture", "สถาปัตยกรรม LLM",
                "นิยามองค์ประกอบและอธิบาย flow ของโมเดลด้วย noun phrases", "AI English", "B1", LearningCategory.AiEnglish, 102,
                LessonData(
                    "llm-architecture",
                    "Explain the main components of an LLM architecture",
                    "เชื่อมความรู้ AI กับภาษา: นิยาม component ด้วย “X is a component that…” และใช้ noun phrases เพื่ออธิบายหน้าที่",
                    "A tokenizer converts text into tokens. The model processes those tokens through multiple layers. A decoder predicts the next token.",
                    "First, the tokenizer converts the input into tokens. Next, the model processes the sequence. Finally, the decoder produces output tokens.",
                    "Give a 60-second explanation of tokenizer, layers, and decoder.",
                    "Write a 70-word explanation of an LLM architecture for a junior developer.",
                    [
                        Vocab("tokenizer", "ตัวแปลงข้อความเป็นโทเคน", "/ˈtəʊ.kən.aɪ.zər/", "noun", "use a tokenizer", "The tokenizer converts text into tokens."),
                        Vocab("layer", "ชั้นของโมเดล", "/ˈleɪ.ər/", "noun", "neural layer", "Each layer transforms the representation."),
                        Vocab("predict", "ทำนาย", "/prɪˈdɪkt/", "verb", "predict a token", "The model predicts the next token.")
                    ],
                    [
                        Quiz("Technical English", "What does a tokenizer do?", 1, "It stores passwords.", "It converts text into tokens.", "It deploys the API.", "It ranks databases."),
                        Quiz("Grammar", "Choose the natural definition.", 2, "A decoder a component produces output.", "A decoder is produce output.", "A decoder is a component that produces output.", "A decoder which output.")
                    ])),

            Module("AI06", "Prompt Engineering", "วิศวกรรมพรอมป์",
                "เขียน prompt ที่มี Goal, Context, Rules และ Output ชัดเจน", "AI English", "B1", LearningCategory.AiEnglish, 106,
                LessonData(
                    "prompt-engineering",
                    "Write an unambiguous prompt with constraints and output format",
                    "ใช้ imperative verbs เช่น summarize, classify, return และระบุข้อจำกัดเชิงบวกพร้อมตัวอย่าง output",
                    "A strong prompt states the goal, provides context, sets rules, and defines the expected output.",
                    "Summarize the incident in three bullet points. Use only the supplied log. Return valid JSON with summary, cause, and nextAction fields.",
                    "Read the prompt aloud, then explain why each constraint is necessary.",
                    "Create a prompt that extracts API errors into a JSON array.",
                    [
                        Vocab("constraint", "ข้อจำกัด", "/kənˈstreɪnt/", "noun", "set a constraint", "The prompt includes a length constraint."),
                        Vocab("instruction", "คำสั่ง", "/ɪnˈstrʌk.ʃən/", "noun", "follow an instruction", "The model should follow the instruction."),
                        Vocab("output format", "รูปแบบผลลัพธ์", "/ˈaʊt.pʊt ˈfɔː.mæt/", "noun phrase", "define the output format", "Define the output format explicitly.")
                    ],
                    [
                        Quiz("Technical English", "Which prompt is most specific?", 3, "Tell me things.", "Write about the log.", "Analyze it.", "Summarize the supplied log in three bullets and name the likely cause."),
                        Quiz("Vocabulary", "A rule that limits an answer is a…", 2, "retrieval", "response", "constraint", "tokenizer")
                    ])),

            Module("AI13", "RAG Architecture", "สถาปัตยกรรม RAG",
                "อธิบาย retrieval-to-generation process และการอ้างอิงหลักฐาน", "AI English", "B1", LearningCategory.AiEnglish, 113,
                LessonData(
                    "rag-architecture",
                    "Explain a grounded RAG flow from query to answer",
                    "RAG ผสาน retrieval กับ generation ภาษาหลักของบทคือ sequence words, passive voice และคำว่า relevant, source, context",
                    "In RAG, relevant chunks are retrieved from a knowledge source. They are added to the prompt as context. The model then generates a grounded answer.",
                    "First, the query is converted into an embedding. Next, relevant chunks are retrieved and ranked. Then, the context is sent to the model. Finally, the answer is generated with source references.",
                    "Explain the RAG workflow in 90 seconds using first, next, then, and finally.",
                    "Write a technical summary that explains how RAG reduces unsupported answers.",
                    [
                        Vocab("retrieve", "ค้นและดึงกลับมา", "/rɪˈtriːv/", "verb", "retrieve a document", "The system retrieves relevant documents."),
                        Vocab("relevant", "เกี่ยวข้องตรงประเด็น", "/ˈrel.ə.vənt/", "adjective", "relevant context", "Only relevant context is sent to the model."),
                        Vocab("grounded", "ยึดโยงกับหลักฐาน", "/ˈɡraʊn.dɪd/", "adjective", "grounded answer", "A grounded answer is supported by a source."),
                        Vocab("source", "แหล่งข้อมูล", "/sɔːs/", "noun", "cite a source", "The answer cites its source.")
                    ],
                    [
                        Quiz("Technical English", "What happens before the model generates a RAG answer?", 1, "The database is deleted.", "Relevant context is retrieved.", "The API is archived.", "The password is displayed."),
                        Quiz("Grammar", "Choose the correct passive form.", 2, "Relevant chunks retrieve.", "Relevant chunks are retrieve.", "Relevant chunks are retrieved.", "Relevant chunks retrieved are.")
                    ])),

            Module("AI23", "Secure Production Deployment", "การนำระบบขึ้นใช้งานอย่างปลอดภัย",
                "นำเสนอ permission, policy, risk และ security checklist เป็นภาษาอังกฤษ", "AI English", "B2", LearningCategory.AiEnglish, 123,
                LessonData(
                    "secure-production-deployment",
                    "Present a practical security checklist for an AI service",
                    "ใช้ must/must not สำหรับข้อบังคับ should สำหรับข้อแนะนำ และ may/might สำหรับความเสี่ยง",
                    "Secrets must be stored outside source code. The API should validate every input. Logs must not expose personal data.",
                    "Before deployment, permissions are reviewed and secrets are rotated. Inputs are validated and rate limits are enabled. After release, security logs are monitored.",
                    "Present five security checks and explain one risk for each check.",
                    "Write a release security checklist for an AI API.",
                    [
                        Vocab("permission", "สิทธิ์การเข้าถึง", "/pəˈmɪʃ.ən/", "noun", "grant permission", "Grant only the required permission."),
                        Vocab("secret", "ข้อมูลลับสำหรับระบบ", "/ˈsiː.krət/", "noun", "store a secret", "Never store a secret in source code."),
                        Vocab("rate limit", "ขีดจำกัดอัตราคำขอ", "/reɪt ˈlɪm.ɪt/", "noun", "enforce a rate limit", "The gateway enforces a rate limit."),
                        Vocab("expose", "เปิดเผยโดยไม่ควร", "/ɪkˈspəʊz/", "verb", "expose data", "Logs must not expose personal data.")
                    ],
                    [
                        Quiz("Technical English", "Which rule is safest?", 2, "Secrets may be committed.", "Logs should contain passwords.", "Secrets must be stored outside source code.", "Every user should be an admin."),
                        Quiz("Grammar", "Use ___ for a mandatory security rule.", 1, "might", "must", "could", "would")
                    ])),

            ..BuildAdditionalAiModules()
        ];
    }

    private static IReadOnlyList<CourseModule> BuildSkillModules() =>
        [
            SkillModule(
                "E11",
                "Reading",
                "การอ่าน",
                "อ่านตั้งแต่ข้อความสั้นจนถึง documentation, API reference และ technical paper แบบย่อ",
                LearningCategory.Reading,
                11,
                "B1",
                "reading-strategies",
                "Use skimming, scanning, context clues, and reference words in technical reading",
                "เริ่มจากมองหัวข้อ โครงสร้าง และคำซ้ำเพื่อหา main idea ก่อนอ่านรายละเอียด จากนั้น scan หาชื่อ field, status code และเงื่อนไขที่โจทย์ถาม",
                "Read the endpoint summary first. Scan for the required field and the error response. The reference word “it” points to the request object.",
                Vocab("scan", "กวาดตาหาข้อมูลเฉพาะ", "/skæn/", "verb", "scan for details", "Scan the API reference for the required field."),
                Quiz("Reading", "Scanning is mainly used to…", 1, "translate every word", "find specific information", "memorize the paragraph", "guess without reading")),
            SkillModule(
                "E12",
                "Listening",
                "การฟัง",
                "ฝึกฟังแบบไม่มีข้อความ ตอบ เปิด transcript ทำ dictation และ shadowing",
                LearningCategory.Listening,
                12,
                "B1",
                "listening-workflow",
                "Follow a listen-answer-transcript-shadow-summarize workflow",
                "รอบแรกฟังเพื่อจับความหมายรวมโดยไม่เปิดข้อความ รอบสองตรวจคำตอบด้วย transcript แล้วพูดตามเป็น chunks ก่อนสรุปด้วยคำของตนเอง",
                "First, listen without the transcript. Next, check the key chunks. Then, shadow each sentence. Finally, summarize the message.",
                Vocab("shadow", "พูดตามเสียงเกือบพร้อมกัน", "/ˈʃæd.əʊ/", "verb", "shadow a sentence", "Shadow the sentence at a comfortable speed."),
                Quiz("Listening", "What should you do before opening the transcript?", 2, "read every answer", "translate the page", "listen and answer", "skip the audio")),
            SkillModule(
                "E13",
                "Speaking",
                "การพูด",
                "พัฒนาจาก shadowing สู่การอธิบายกระบวนการและนำเสนอหัวข้อเทคนิค",
                LearningCategory.Speaking,
                13,
                "B1",
                "speaking-organization",
                "Organize a technical explanation with Point, Reason, Example, and Result",
                "ใช้ PREP เป็นโครงช่วยคิด ไม่ต้องเลียนสำเนียงเจ้าของภาษา เป้าหมายคือพูดต่อเนื่องและให้ผู้ฟังตามความคิดได้",
                "I prefer RAG because it can use external information. For example, it can retrieve company documents. As a result, the answer can be more relevant.",
                Vocab("fluency", "ความคล่องต่อเนื่อง", "/ˈfluː.ən.si/", "noun", "improve fluency", "Short daily practice can improve fluency."),
                Quiz("Speaking", "Which phrase introduces an example?", 1, "As a result", "For example", "My point is", "In conclusion")),
            SkillModule(
                "E14",
                "Writing",
                "การเขียน",
                "เขียน message, email, summary, technical explanation, bug report และ incident summary",
                LearningCategory.Writing,
                14,
                "B1",
                "technical-writing",
                "Write a clear technical update with status, problem, cause, and next action",
                "ให้แต่ละย่อหน้ามีหน้าที่ชัด เริ่มจากสถานะ ต่อด้วยปัญหาและสาเหตุ แล้วปิดด้วย next action ที่ตรวจสอบได้",
                "The API is complete. However, the integration test is failing because the seed data is incomplete. I will update the data and rerun the test.",
                Vocab("concise", "กระชับ", "/kənˈsaɪs/", "adjective", "clear and concise", "Keep the status update clear and concise."),
                Quiz("Writing", "Which sentence gives a next action?", 3, "The test failed.", "The API is complete.", "The data was incomplete.", "I will update the data and rerun the test.")),
            SkillModule(
                "E15",
                "Workplace & Technical English",
                "ภาษาอังกฤษในที่ทำงานและงานเทคนิค",
                "ประชุม รายงานความคืบหน้า อธิบาย blocker review code และนำเสนอ architecture",
                LearningCategory.Workplace,
                15,
                "B1",
                "workplace-technical-communication",
                "Report progress, clarify requirements, and explain a blocker professionally",
                "ใช้ประโยคตรงประเด็นและแยก fact ออกจาก assumption หาก requirement ไม่ชัดให้ถามยืนยันก่อนเสนอ estimate",
                "The implementation is complete, but deployment is blocked by a missing permission. Could you confirm who can approve the access request?",
                Vocab("blocker", "สิ่งที่ขัดขวางงาน", "/ˈblɒk.ər/", "noun", "report a blocker", "I need to report one deployment blocker."),
                Quiz("Workplace", "Which question asks for clarification politely?", 2, "Fix this now.", "You are wrong.", "Could you confirm the expected output?", "No requirement.")),
            SkillModule(
                "E16",
                "TOEIC Listening & Reading",
                "TOEIC การฟังและการอ่าน",
                "ฝึก Parts 1–7 แบบจับเวลา พร้อมวิเคราะห์ distractor และจุดอ่อน",
                LearningCategory.Toeic,
                16,
                "B1",
                "toeic-listening-reading",
                "Choose a TOEIC strategy by part and manage time across Parts 1–7",
                "Listening เน้นเดาคำถามและจับ paraphrase ส่วน Reading ให้กำหนดเวลา Part 5–6 เพื่อเหลือเวลาสำหรับ Part 7",
                "The meeting has been postponed until Friday. All participants should review the revised agenda before then.",
                Vocab("postpone", "เลื่อนออกไป", "/pəˈspəʊn/", "verb", "postpone a meeting", "The team postponed the meeting until Friday."),
                Quiz("TOEIC", "The meeting has been ___ until Friday.", 2, "attended", "reviewed", "postponed", "attached")),
            SkillModule(
                "E17",
                "TOEIC Speaking & Writing",
                "TOEIC การพูดและการเขียน",
                "เส้นทางเสริมสำหรับอ่านออกเสียง อธิบายภาพ ตอบคำถาม เขียนอีเมล และแสดงความคิดเห็น",
                LearningCategory.Toeic,
                17,
                "B2",
                "toeic-speaking-writing",
                "Respond to a workplace speaking prompt and write a complete email response",
                "งานพูดต้องตอบตรงและมีรายละเอียดสนับสนุน งานเขียนอีเมลต้องตอบทุกข้อกำหนดพร้อม opening, body และ closing ที่เหมาะสม",
                "Thank you for your message. I can attend the workshop on Tuesday. Could you also confirm whether participants should bring a laptop?",
                Vocab("respond", "ตอบกลับ", "/rɪˈspɒnd/", "verb", "respond to an email", "Please respond to every point in the email."),
                Quiz("TOEIC", "A complete email response should…", 1, "ignore the request", "address every requested point", "use only one word", "omit the closing"))
        ];

    private static CourseModule SkillModule(
        string code,
        string title,
        string titleThai,
        string summary,
        LearningCategory category,
        int sortOrder,
        string cefr,
        string slug,
        string objective,
        string explanation,
        string reading,
        VocabularyItem vocabulary,
        AssessmentQuestion question) =>
        Module(
            code,
            title,
            titleThai,
            summary,
            "Phase 3",
            cefr,
            category,
            sortOrder,
            CompactLesson(slug, objective, explanation, reading, vocabulary, question));

    private static IReadOnlyList<CourseModule> BuildAdditionalAiModules()
    {
        var modules = new[]
        {
            new AiModuleSeed(1, "Evolution of AI", "วิวัฒนาการของ AI", "Past tense, timeline, and change verbs", "evolution", "วิวัฒนาการ", "/ˌiː.vəˈluː.ʃən/", "AI evolved from rule-based systems to data-driven models."),
            new AiModuleSeed(3, "Inference Lifecycle", "วงจรการอนุมาน", "Sequence words, process language, and passive voice", "inference", "การอนุมาน", "/ˈɪn.fər.əns/", "Inference begins when the service receives an input."),
            new AiModuleSeed(4, "Deterministic vs Stochastic", "กำหนดแน่นอนกับเชิงสุ่ม", "Compare, contrast, and probability language", "deterministic", "กำหนดผลแน่นอน", "/dɪˌtɜː.mɪˈnɪs.tɪk/", "A deterministic process returns the same result for the same input."),
            new AiModuleSeed(5, "API-First AI", "AI ที่เริ่มจาก API", "Request/response verbs and endpoint terminology", "endpoint", "จุดเชื่อมต่อ API", "/ˈend.pɔɪnt/", "The endpoint accepts a request and returns a structured response."),
            new AiModuleSeed(7, "Advanced Reasoning", "การให้เหตุผลขั้นสูง", "Cause, effect, justification, and evidence", "evidence", "หลักฐาน", "/ˈev.ɪ.dəns/", "The conclusion should be supported by relevant evidence."),
            new AiModuleSeed(8, "Structured Outputs", "ผลลัพธ์แบบมีโครงสร้าง", "JSON fields, data types, and validation language", "schema", "โครงสร้างข้อมูล", "/ˈskiː.mə/", "The schema defines required fields and data types."),
            new AiModuleSeed(9, "Function & Tool Calling", "การเรียกฟังก์ชันและเครื่องมือ", "Parameters, conditions, calls, returns, and errors", "parameter", "พารามิเตอร์", "/pəˈræm.ɪ.tər/", "Each parameter has a name, type, and purpose."),
            new AiModuleSeed(10, "Managing LLM Failures", "การจัดการความล้มเหลวของ LLM", "Troubleshooting and incident language", "failure", "ความล้มเหลว", "/ˈfeɪ.ljər/", "The report describes the failure, impact, cause, and mitigation."),
            new AiModuleSeed(11, "Token Optimization", "การปรับใช้โทเคนอย่างเหมาะสม", "Quantity, measurement, and trade-off language", "trade-off", "ข้อแลกเปลี่ยน", "/ˈtreɪd.ɒf/", "The team measured the trade-off between cost and answer quality."),
            new AiModuleSeed(12, "Prompt Chains", "สายโซ่พรอมป์", "Sequencing, dependencies, and handoffs", "dependency", "สิ่งที่ขั้นตอนอื่นต้องพึ่งพา", "/dɪˈpen.dən.si/", "The second prompt has a dependency on the first output."),
            new AiModuleSeed(14, "Data Ingestion", "การนำข้อมูลเข้าสู่ระบบ", "Pipeline verbs and passive voice", "ingest", "นำข้อมูลเข้า", "/ɪnˈdʒest/", "The pipeline ingests, validates, and transforms source documents."),
            new AiModuleSeed(15, "Chunking Strategies", "กลยุทธ์การแบ่งข้อมูล", "Comparison, advantages, and limitations", "chunk", "ส่วนข้อความที่แบ่งไว้", "/tʃʌŋk/", "Each chunk should preserve enough context for retrieval."),
            new AiModuleSeed(16, "Embedding Models", "โมเดล Embedding", "Abstract definitions and analogy", "embedding", "เวกเตอร์แทนความหมาย", "/ɪmˈbed.ɪŋ/", "An embedding represents semantic meaning as a vector."),
            new AiModuleSeed(17, "Vector Database", "ฐานข้อมูลเวกเตอร์", "Index, storage, and retrieval vocabulary", "index", "ดัชนี", "/ˈɪn.deks/", "The database indexes vectors for efficient retrieval."),
            new AiModuleSeed(18, "Similarity Search", "การค้นหาความคล้าย", "Scores, ranking, filtering, and relevance", "similarity", "ความคล้ายกัน", "/ˌsɪm.ɪˈlær.ə.ti/", "Similarity search ranks documents by relevance to the query."),
            new AiModuleSeed(19, "RAG Evaluation", "การประเมิน RAG", "Criteria, evidence, metrics, and findings", "metric", "ตัวชี้วัด", "/ˈmet.rɪk/", "Each metric measures one aspect of retrieval or answer quality."),
            new AiModuleSeed(20, "Multi-Agent Systems", "ระบบหลายเอเจนต์", "Roles, delegation, handoffs, and status", "delegate", "มอบหมาย", "/ˈdel.ɪ.ɡeɪt/", "The coordinator delegates a bounded task to a specialist."),
            new AiModuleSeed(21, "Intelligent Routing", "การกำหนดเส้นทางอัจฉริยะ", "Conditions and decision language", "route", "กำหนดเส้นทาง", "/ruːt/", "The router sends each request to the appropriate model."),
            new AiModuleSeed(22, "API Automation", "ระบบอัตโนมัติผ่าน API", "Triggers, actions, events, and schedules", "trigger", "ตัวกระตุ้น", "/ˈtrɪɡ.ər/", "A new event triggers the automation workflow."),
            new AiModuleSeed(24, "Reliability Engineering", "วิศวกรรมความน่าเชื่อถือ", "Availability, latency, recovery, and incident language", "recovery", "การกู้คืน", "/rɪˈkʌv.ər.i/", "The recovery plan restores service after a failure.")
        };

        return modules.Select(seed => Module(
            $"AI{seed.Number:00}",
            seed.Title,
            seed.TitleThai,
            seed.LanguageFocus,
            "AI English",
            seed.Number >= 19 ? "B2" : "B1",
            LearningCategory.AiEnglish,
            100 + seed.Number,
            CompactLesson(
                $"ai-{seed.Number:00}-{Slugify(seed.Title)}",
                $"Explain {seed.Title} clearly in English",
                $"บทนี้ฝึก {seed.LanguageFocus} แล้วนำไปสร้าง technical explanation ที่ตรวจสอบโครงสร้างได้",
                seed.Example,
                Vocab(
                    seed.Keyword,
                    seed.ThaiMeaning,
                    seed.Pronunciation,
                    "technical term",
                    $"{seed.Keyword} in context",
                    seed.Example),
                Quiz(
                    "Technical English",
                    $"Which sentence uses “{seed.Keyword}” in the module context?",
                    1,
                    $"{seed.Keyword} is unrelated to this topic.",
                    seed.Example,
                    $"Never explain the {seed.Keyword}.",
                    $"{seed.Keyword} has no meaning."))))
            .ToList();
    }

    private static string Slugify(string value) =>
        value.ToLowerInvariant().Replace("&", "and").Replace(" ", "-");

    private sealed record AiModuleSeed(
        int Number,
        string Title,
        string TitleThai,
        string LanguageFocus,
        string Keyword,
        string ThaiMeaning,
        string Pronunciation,
        string Example);

    private static CourseModule Module(
        string code,
        string title,
        string titleThai,
        string summary,
        string phase,
        string cefr,
        LearningCategory category,
        int sortOrder,
        Lesson lesson)
    {
        var module = new CourseModule
        {
            Code = code,
            Title = title,
            TitleThai = titleThai,
            Summary = summary,
            Phase = phase,
            CefrLevel = cefr,
            Category = category,
            SortOrder = sortOrder,
            EstimatedMinutes = lesson.EstimatedMinutes
        };
        module.Lessons.Add(lesson);
        return module;
    }

    private static Lesson CompactLesson(
        string slug,
        string objective,
        string explanation,
        string reading,
        VocabularyItem vocabulary,
        AssessmentQuestion question) =>
        LessonData(
            slug,
            objective,
            explanation,
            explanation,
            reading,
            $"Explain this pattern in your own words: {reading}",
            $"Write three new examples based on: {reading}",
            [vocabulary],
            [question]);

    private static Lesson LessonData(
        string slug,
        string objective,
        string explanation,
        string grammar,
        string reading,
        string speaking,
        string writing,
        IReadOnlyList<VocabularyItem> vocabulary,
        IReadOnlyList<AssessmentQuestion> questions)
    {
        var lesson = new Lesson
        {
            Slug = slug,
            Title = objective,
            Objective = objective,
            ThaiExplanation = explanation,
            GrammarFocus = grammar,
            ReadingContent = reading,
            ListeningTranscript = reading,
            SpeakingPrompt = speaking,
            WritingPrompt = writing,
            EstimatedMinutes = 25,
            SortOrder = 1
        };
        lesson.Vocabulary.AddRange(vocabulary);
        lesson.Questions.AddRange(questions);
        return lesson;
    }

    private static VocabularyItem Vocab(
        string word,
        string meaning,
        string pronunciation,
        string wordForm,
        string collocation,
        string example) =>
        new()
        {
            Word = word,
            ThaiMeaning = meaning,
            Pronunciation = pronunciation,
            WordForm = wordForm,
            Collocation = collocation,
            ExampleSentence = example
        };

    private static AssessmentQuestion Quiz(string skill, string prompt, int correctOptionIndex, params string[] options) =>
        new()
        {
            Kind = AssessmentKind.LessonQuiz,
            Skill = skill,
            Prompt = prompt,
            OptionsJson = JsonSerializer.Serialize(options),
            CorrectOptionIndex = correctOptionIndex,
            Explanation = $"The correct answer is “{options[correctOptionIndex]}”. Review the lesson example and try again.",
            SortOrder = 1
        };

    private static IReadOnlyList<AssessmentQuestion> BuildPlacementQuestions()
    {
        var questions = new List<AssessmentQuestion>
        {
            Standalone(AssessmentKind.Placement, "Pronunciation", "Which word begins with the /v/ sound?", 1, "west", "very", "ready", "three"),
            Standalone(AssessmentKind.Placement, "Vocabulary", "Choose the natural phrase.", 2, "do a request", "create at data", "send a response", "make an error handled"),
            Standalone(AssessmentKind.Placement, "Grammar", "Choose the correct sentence.", 1, "The model generate text.", "The model generates text.", "The model generating text.", "The model is generate text."),
            Standalone(AssessmentKind.Placement, "Grammar", "We ___ the update yesterday.", 2, "deploy", "have deploy", "deployed", "will deployed"),
            Standalone(AssessmentKind.Placement, "Reading", "The API rejected the request because the token had expired. Why was the request rejected?", 1, "The payload was too large.", "The token had expired.", "The API was offline.", "The user closed the page.",
                "The API rejected the request because the token had expired."),
            Standalone(AssessmentKind.Placement, "Listening", "Imagine you hear: “Please restart the service after you update the configuration.” What should happen first?", 2, "Restart the service.", "Delete the service.", "Update the configuration.", "Send an invoice."),
            Standalone(AssessmentKind.Placement, "Writing", "Choose the clearest status update.", 3, "API done maybe.", "I work API yesterday now.", "The API has complete.", "The API implementation is complete, but the integration test is failing."),
            Standalone(AssessmentKind.Placement, "Speaking", "Which answer best follows Point → Reason → Example?", 2, "RAG.", "I like it. It is good.", "I prefer RAG because it can use external sources. For example, it can retrieve company documents.", "Because example result."),
            Standalone(AssessmentKind.Placement, "Technical English", "What does an API endpoint usually receive and return?", 1, "A keyboard and a monitor", "A request and a response", "A branch and a commit only", "A lesson and a teacher"),
            Standalone(AssessmentKind.Placement, "Technical English", "Choose the correct passive sentence.", 2, "The input validates.", "The input is validate.", "The input is validated.", "The input validating."),
            Standalone(AssessmentKind.Placement, "TOEIC", "The meeting has been ___ until Friday.", 1, "postpone", "postponed", "postponing", "postpones", toeicPart: 5),
            Standalone(AssessmentKind.Placement, "TOEIC", "Read: “Employees must submit travel receipts within ten days.” What must employees submit?", 0, "Travel receipts", "A job application", "A product sample", "Meeting notes",
                "Employees must submit travel receipts within ten days.", toeicPart: 7)
        };
        for (var i = 0; i < questions.Count; i++)
        {
            questions[i].SortOrder = i + 1;
        }
        return questions;
    }

    private static IReadOnlyList<AssessmentQuestion> BuildToeicQuestions()
    {
        var questions = new List<AssessmentQuestion>
        {
            Standalone(AssessmentKind.ToeicDiagnostic, "TOEIC Listening", "Part 1 — Which sentence best describes the scene: a woman is typing at a desk?", 1, "The chairs are being stacked.", "A woman is working at a computer.", "The office is being painted.", "A package is under the table.", toeicPart: 1),
            Standalone(AssessmentKind.ToeicDiagnostic, "TOEIC Listening", "Part 2 — When will the report be ready?", 2, "In the meeting room.", "Yes, I read it.", "By Thursday afternoon.", "The blue folder.", toeicPart: 2),
            Standalone(AssessmentKind.ToeicDiagnostic, "TOEIC Listening", "Part 3 — “The 10 a.m. train is delayed, so let’s take the 10:30 express.” What will the speakers probably do?", 1, "Cancel the trip.", "Take a later express train.", "Drive to the office.", "Wait for a bus.", toeicPart: 3),
            Standalone(AssessmentKind.ToeicDiagnostic, "TOEIC Listening", "Part 4 — A speaker announces that the museum closes early for maintenance. Why will it close early?", 3, "For a private tour", "Because of bad weather", "For staff training", "For maintenance", toeicPart: 4),
            Standalone(AssessmentKind.ToeicDiagnostic, "TOEIC Reading", "Part 5 — All expense forms must be ___ by a manager.", 0, "approved", "approving", "approval", "approves", toeicPart: 5),
            Standalone(AssessmentKind.ToeicDiagnostic, "TOEIC Reading", "Part 6 — The new software is easier to use. ___, it requires less training time.", 2, "However", "Otherwise", "Therefore", "Although", toeicPart: 6),
            Standalone(AssessmentKind.ToeicDiagnostic, "TOEIC Reading", "Part 7 — Notice: The cafeteria will be closed on Monday for equipment inspection. When will it be closed?", 1, "On Friday", "On Monday", "Every morning", "For one month",
                "The cafeteria will be closed on Monday for equipment inspection.", toeicPart: 7)
        };
        for (var i = 0; i < questions.Count; i++)
        {
            questions[i].SortOrder = i + 1;
        }
        return questions;
    }

    private static IReadOnlyList<AssessmentQuestion> BuildToeicMockQuestions()
    {
        var questions = new List<AssessmentQuestion>(200);

        void Add(
            int part,
            string prompt,
            string correct,
            string distractor1,
            string distractor2,
            string distractor3,
            string? supportingText = null)
        {
            var correctIndex = questions.Count % 4;
            var options = new List<string> { distractor1, distractor2, distractor3 };
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

        var scenes = new[]
        {
            ("a technician checking cables beside a server rack", "A technician is inspecting some equipment.", "The servers are being removed.", "A customer is signing a receipt.", "The lights have been turned off."),
            ("two colleagues reviewing a chart on a screen", "Two people are looking at a presentation.", "The office furniture is being delivered.", "A woman is closing the curtains.", "The screen is being repaired."),
            ("packages arranged near a loading door", "Several boxes have been placed near an entrance.", "A vehicle is crossing a bridge.", "Some shelves are completely empty.", "Workers are painting a wall."),
            ("a chef placing trays on a counter", "Food is being arranged on a counter.", "Customers are paying at a machine.", "The windows overlook a harbor.", "A menu has fallen on the floor."),
            ("passengers waiting under a station sign", "Several people are waiting at a station.", "The platform is being washed.", "A train conductor is selling food.", "The sign is covered by a curtain."),
            ("a landscaper watering plants outside an office", "A worker is watering some plants.", "The building is under construction.", "The path has been blocked by cars.", "Some tools are displayed in a shop.")
        };
        foreach (var scene in scenes)
        {
            Add(
                1,
                $"Which sentence best describes this simulated scene: {scene.Item1}?",
                scene.Item2,
                scene.Item3,
                scene.Item4,
                scene.Item5);
        }

        var days = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" };
        var names = new[] { "Chen", "Garcia", "Khan", "Miller", "Sato" };
        for (var index = 0; index < 25; index++)
        {
            var number = index + 1;
            switch (index % 5)
            {
                case 0:
                    Add(2, $"When will version {number} be released?", $"By {days[index % days.Length]} afternoon.", "At the main entrance.", "The design team did.", "About the release notes.");
                    break;
                case 1:
                    Add(2, $"Where is interview room {number}?", $"On the {(index % 8) + 2}th floor.", "At three o'clock.", "Mr. Khan reserved it.", "Twice a month.");
                    break;
                case 2:
                    Add(2, $"Who approved purchase request {number}?", $"Ms. {names[index % names.Length]} did.", "In the accounting office.", "By electronic transfer.", "The new equipment.");
                    break;
                case 3:
                    Add(2, $"Why is service desk {number} closed?", "Because the system is being upgraded.", "For about two hours.", "At the north entrance.", "A technical assistant.");
                    break;
                default:
                    Add(2, $"How often is inspection {number} performed?", $"Every {(index % 4) + 1} months.", "By the maintenance supervisor.", "In the storage area.", "It passed the inspection.");
                    break;
            }
        }

        var conversations = new[]
        {
            new ConversationSeed("office", "rescheduling a project review", "send an updated calendar invitation", "Our client moved the review to Thursday. I will update everyone's calendar after lunch."),
            new ConversationSeed("hotel", "a guest's early check-in request", "check whether a room is ready", "The guest will arrive before noon. Let me ask housekeeping whether room 804 is ready."),
            new ConversationSeed("train station", "a delayed train and another route", "take the express train from platform six", "The local train is delayed. The express from platform six will leave in ten minutes."),
            new ConversationSeed("restaurant", "a catering order for a workshop", "confirm the number of vegetarian meals", "The workshop order is almost complete, but we still need the final number of vegetarian meals."),
            new ConversationSeed("electronics store", "replacing a defective monitor", "bring the receipt to customer service", "This monitor flickers after a few minutes. Bring the receipt and we can replace it today."),
            new ConversationSeed("medical clinic", "changing an appointment time", "offer an appointment on Wednesday morning", "The doctor will be away Tuesday afternoon. We have an opening Wednesday at nine."),
            new ConversationSeed("warehouse", "an incomplete shipment", "contact the supplier about missing items", "The shipment arrived, but two boxes are missing. I will call the supplier now."),
            new ConversationSeed("conference center", "equipment for a presentation", "reserve an additional microphone", "The panel has four speakers, so one microphone will not be enough. I will reserve another."),
            new ConversationSeed("bank", "documents needed for an application", "email a copy of the missing statement", "Your application needs last month's statement. You can email a scanned copy this afternoon."),
            new ConversationSeed("museum", "tickets for a group visit", "apply the group discount", "There will be eighteen visitors. Groups of fifteen or more receive a discount."),
            new ConversationSeed("airport", "a departure gate change", "walk to gate C12", "The flight no longer leaves from B4. The new departure gate is C12."),
            new ConversationSeed("printing company", "a correction to a brochure", "send a revised proof before printing", "The telephone number on page two is incorrect. I will send a revised proof for approval."),
            new ConversationSeed("software help desk", "a user who cannot access an account", "reset the user's access credentials", "The account was locked after several attempts. I can reset the credentials once I verify the employee ID.")
        };
        foreach (var conversation in conversations)
        {
            Add(3, "Where does this conversation most likely take place?", conversation.Location, "at a sports stadium", "in a classroom", "at a farm", conversation.Dialogue);
            Add(3, "What are the speakers mainly discussing?", conversation.Purpose, "a staff celebration", "a new advertising slogan", "a weather forecast", conversation.Dialogue);
            Add(3, "What will probably happen next?", conversation.NextAction, "the building will be sold", "all appointments will be canceled", "the office will close permanently", conversation.Dialogue);
        }

        var talks = new[]
        {
            new TalkSeed("welcome visitors to a factory tour", "the reception area", "put on safety glasses", "at 9:15", "Welcome to Northfield Manufacturing. Before we enter the production area at 9:15, please collect safety glasses at reception."),
            new TalkSeed("announce a library schedule change", "the city library", "return books through the outside slot", "this Saturday", "The city library will close early this Saturday for electrical maintenance. Books may be returned through the outside slot."),
            new TalkSeed("explain a software training session", "the computer lab", "download the practice files", "tomorrow morning", "Tomorrow morning's software class will be in the computer lab. Please download the practice files before arriving."),
            new TalkSeed("promote a seasonal store discount", "the home goods department", "show a membership card", "through Sunday", "Members receive twenty percent off kitchen items in the home goods department through Sunday. Show your membership card at checkout."),
            new TalkSeed("give instructions before a flight", "gate A18", "have identification ready", "in twenty minutes", "Boarding at gate A18 will begin in twenty minutes. Passengers should have identification and boarding passes ready."),
            new TalkSeed("report a road closure", "River Street", "use the Oak Avenue route", "until Friday", "River Street is closed for resurfacing until Friday. Drivers should use Oak Avenue as an alternate route."),
            new TalkSeed("introduce a company wellness event", "the rooftop garden", "register on the employee portal", "next Wednesday", "A wellness workshop will take place in the rooftop garden next Wednesday. Register through the employee portal."),
            new TalkSeed("describe a museum audio guide", "the information desk", "scan the code on the ticket", "during the visit", "Visitors can access the new audio guide during their visit. Scan the code on your ticket or ask at the information desk."),
            new TalkSeed("announce an office network interruption", "the downtown office", "save cloud-based work in advance", "from 7 to 8 p.m.", "The downtown office network will be unavailable from 7 to 8 p.m. Please save cloud-based work before maintenance begins."),
            new TalkSeed("explain a conference lunch arrangement", "exhibition hall B", "present the meal voucher", "at 12:30", "Lunch will be served at 12:30 in exhibition hall B. Present the voucher inside your name badge holder.")
        };
        foreach (var talk in talks)
        {
            Add(4, "What is the main purpose of the talk?", talk.Purpose, "to conduct a job interview", "to apologize for a billing error", "to request a product refund", talk.Text);
            Add(4, "What location is mentioned?", talk.Location, "the west parking garage", "the executive dining room", "the research warehouse", talk.Text);
            Add(4, $"What should listeners do {talk.TimeReference}?", talk.Action, "submit a resignation letter", "purchase new furniture", "call an international operator", talk.Text);
        }

        var partFive = new[]
        {
            ("All travel claims must be ___ by a supervisor.", "approved", "approve", "approval", "approving"),
            ("The new branch will open ___ the beginning of September.", "at", "on", "by", "from"),
            ("Ms. Rivera completed the report more ___ than expected.", "quickly", "quick", "quickness", "quicker"),
            ("Customers may request a refund ___ thirty days of purchase.", "within", "among", "duringly", "beside"),
            ("The committee has not ___ selected a venue for the conference.", "yet", "still", "ever", "soon"),
            ("Please notify the receptionist ___ your visitor arrives.", "when", "despite", "unless of", "during"),
            ("The warranty covers parts but does not include the cost of ___.", "installation", "install", "installed", "installer"),
            ("Because demand increased, the factory hired ___ technicians.", "additional", "addition", "additionally", "add"),
            ("The proposal was revised ___ the client's comments.", "according to", "except", "between", "instead"),
            ("Neither the manager nor the assistants ___ available this morning.", "were", "was", "be", "has"),
            ("The workshop is designed for employees ___ manage customer accounts.", "who", "which", "whose it", "whom they"),
            ("We will begin the inspection as soon as the engineer ___.", "arrives", "will arrive", "arrived", "arriving"),
            ("The updated software is considerably ___ to use.", "easier", "easy", "easiest", "easily"),
            ("Applicants should submit two references ___ with the form.", "along", "near", "almost", "across"),
            ("The director thanked the team for ___ flexibility.", "their", "they", "them", "theirs is"),
            ("Sales increased ___ the marketing campaign was launched.", "after", "although of", "during that", "beside"),
            ("The package contains ___ materials for new employees.", "orientation", "orient", "oriented", "orienting"),
            ("Only authorized personnel may enter the laboratory ___ protective clothing.", "without", "unless", "between", "toward"),
            ("The consultant recommended that the company ___ its backup policy.", "review", "reviews", "reviewed", "reviewing"),
            ("Our support team is available to answer questions ___ email.", "by", "with which", "at of", "from that"),
            ("The auditorium can comfortably ___ up to 300 guests.", "accommodate", "accommodation", "accommodating", "accommodatedly"),
            ("Invoices received after Friday will be processed the ___ week.", "following", "follow", "followed", "follows"),
            ("The technician explained the procedure clearly and ___.", "patiently", "patient", "patience", "more patient"),
            ("A confirmation message will be sent ___ your registration is complete.", "once", "even though of", "throughout", "meanwhile"),
            ("The company plans to ___ a survey of remote employees.", "conduct", "conductive", "conductor", "conducted"),
            ("This model is similar to the previous one, ___ it uses less energy.", "but", "therefore of", "despite", "so that it"),
            ("The board will consider ___ the budget at its next meeting.", "increasing", "increase", "increased", "to increasing"),
            ("Employees are encouraged to share suggestions ___.", "regularly", "regular", "regularity", "regulate"),
            ("The shipment arrived intact ___ the severe weather.", "despite", "because", "although of", "during that"),
            ("Mr. Okada is responsible for ensuring that orders are filled ___.", "accurately", "accurate", "accuracy", "more accuracy")
        };
        foreach (var item in partFive)
        {
            Add(5, item.Item1, item.Item2, item.Item3, item.Item4, item.Item5);
        }

        var partSix = new[]
        {
            new PartSixSeed(
                "Subject: Building Access\nBeginning next Monday, all employees must [1] their new identification cards at the lobby gate. The change will make entry faster. [2], the security office will offer card testing on Friday. This message explains the new access procedure.",
                "use", "using", "used", "uses",
                "Therefore", "However", "For example of", "Unless",
                "to explain a new access procedure",
                "Card testing is available on Friday."),
            new PartSixSeed(
                "Greenway Café is pleased to [1] a new mobile ordering service. Customers can order before leaving the office. [2], orders will be ready at the pickup counter. The service begins on June 8.",
                "introduce", "introduction", "introduced", "introducing",
                "As a result", "Nevertheless of", "Otherwise that", "Although",
                "to announce a mobile ordering service",
                "The service starts on June 8."),
            new PartSixSeed(
                "Thank you for registering for the design seminar. Your seat has been [1]. Please arrive fifteen minutes early. [2], bring the attached worksheet to the session. Contact us if you need to cancel.",
                "confirmed", "confirm", "confirmation", "confirming",
                "In addition", "Instead of", "Despite", "Because of it",
                "to confirm seminar registration",
                "Participants should arrive early."),
            new PartSixSeed(
                "The west elevator will be unavailable while technicians [1] its control panel. The work is expected to finish by noon. [2], visitors should use the central elevator. We apologize for the inconvenience.",
                "replace", "replacement", "replaced", "replacing",
                "Meanwhile", "Therefore of", "Although it", "Unless",
                "to report elevator maintenance",
                "The work should finish by noon.")
        };
        foreach (var passage in partSix)
        {
            Add(6, "Choose the best option for blank [1].", passage.CorrectOne, passage.OneA, passage.OneB, passage.OneC, passage.Text);
            Add(6, "Choose the best option for blank [2].", passage.CorrectTwo, passage.TwoA, passage.TwoB, passage.TwoC, passage.Text);
            Add(6, "What is the purpose of the text?", passage.Purpose, "to advertise an overseas vacation", "to reject a job application", "to compare insurance plans", passage.Text);
            Add(6, "Which detail is stated?", passage.Detail, "All employees will receive a cash award.", "The event has been permanently canceled.", "Customers must visit another country.", passage.Text);
        }

        var passages = new[]
        {
            new PartSevenSeed("Maya Patel", "to request volunteers for a community event", "August 14", "reply with an available time", "Lakeside Park", "Lunch will be provided.", "From: Maya Patel\nOur company will clean walking paths at Lakeside Park on August 14. Volunteers may choose a morning or afternoon shift. Reply with your available time by Friday. Gloves and lunch will be provided."),
            new PartSevenSeed("Harbor Hotel", "to confirm a room reservation", "October 3", "present identification at check-in", "Harbor Hotel", "Breakfast is included.", "Harbor Hotel confirms your room for October 3. Check-in begins at 3 p.m. Please present identification at reception. Breakfast is included with the reservation."),
            new PartSevenSeed("Leon Wu", "to report a delivery change", "May 22", "use the side entrance", "the side entrance", "Delivery will arrive after 2 p.m.", "From: Leon Wu\nThe furniture delivery scheduled for May 22 will arrive after 2 p.m. Because the front lobby is being renovated, direct the drivers to the side entrance."),
            new PartSevenSeed("Brighton Arts Center", "to announce a photography workshop", "November 9", "register online", "Studio 4", "Participants should bring a camera.", "Brighton Arts Center will hold a photography workshop on November 9 in Studio 4. Register online because seating is limited. Participants should bring a camera, but lighting equipment will be supplied."),
            new PartSevenSeed("Nora Jensen", "to summarize a customer survey", "June 30", "review the attached charts", "the quarterly meeting", "Delivery speed received the lowest rating.", "From: Nora Jensen\nThe customer survey closed on June 30. Product quality received the highest score, while delivery speed received the lowest. Please review the attached charts before the quarterly meeting."),
            new PartSevenSeed("Metro Transit", "to explain a temporary bus route", "Monday", "board at Pine Street", "Pine Street", "The change lasts two weeks.", "Metro Transit notice: Beginning Monday, route 18 will not stop at Central Square because of construction. Passengers should board at Pine Street instead. The change is expected to last two weeks."),
            new PartSevenSeed("Alvarez Medical Group", "to remind patients about an appointment policy", "January 1", "cancel at least 24 hours in advance", "the patient portal", "Late cancellations may incur a fee.", "Alvarez Medical Group: Starting January 1, appointments should be canceled at least 24 hours in advance through the patient portal. A fee may apply to late cancellations."),
            new PartSevenSeed("Orion Software", "to invite users to a product demonstration", "September 12", "submit questions before the event", "the online event room", "A recording will be shared afterward.", "Orion Software will demonstrate its reporting tools online on September 12. Registered users may submit questions in advance. A recording will be shared afterward with all registrants."),
            new PartSevenSeed("Elena Rossi", "to provide instructions for an expense report", "Friday", "upload receipts as PDF files", "the finance portal", "Original paper receipts should be kept for 30 days.", "From: Elena Rossi\nPlease complete the travel expense report in the finance portal by Friday. Upload receipts as PDF files and keep the original paper copies for 30 days.")
        };
        foreach (var passage in passages)
        {
            Add(7, "Who wrote or issued the text?", passage.Writer, "a sports coach", "a television producer", "an unnamed tourist", passage.Text);
            Add(7, "What is the main purpose of the text?", passage.Purpose, "to sell a private vehicle", "to describe a historical battle", "to review a film", passage.Text);
            Add(7, "What date or time is mentioned?", passage.Date, "December 31", "every midnight", "three years ago", passage.Text);
            Add(7, "What should the reader do?", passage.Action, "discard every document", "visit without notice", "send cash by mail", passage.Text);
            Add(7, "What location or channel is mentioned?", passage.Location, "the international terminal", "a mountain resort", "the legal archive", passage.Text);
            Add(7, "Which detail is stated?", passage.Detail, "No further information is available.", "The service is free forever.", "The building will be demolished.", passage.Text);
        }

        if (questions.Count != 200)
        {
            throw new InvalidOperationException(
                $"TOEIC mock seed must contain exactly 200 questions; found {questions.Count}.");
        }

        return questions;
    }

    private sealed record ConversationSeed(
        string Location,
        string Purpose,
        string NextAction,
        string Dialogue);

    private sealed record TalkSeed(
        string Purpose,
        string Location,
        string Action,
        string TimeReference,
        string Text);

    private sealed record PartSixSeed(
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

    private sealed record PartSevenSeed(
        string Writer,
        string Purpose,
        string Date,
        string Action,
        string Location,
        string Detail,
        string Text);

    private static AssessmentQuestion Standalone(
        AssessmentKind kind,
        string skill,
        string prompt,
        int correctOptionIndex,
        string option1,
        string option2,
        string option3,
        string option4,
        string? supportingText = null,
        int? toeicPart = null) =>
        new()
        {
            Kind = kind,
            Skill = skill,
            ToeicPart = toeicPart,
            Prompt = prompt,
            SupportingText = supportingText,
            OptionsJson = JsonSerializer.Serialize(new[] { option1, option2, option3, option4 }),
            CorrectOptionIndex = correctOptionIndex,
            Explanation = $"The correct answer is “{new[] { option1, option2, option3, option4 }[correctOptionIndex]}”."
        };
}
