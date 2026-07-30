# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

Requires .NET SDK 10 (pinned in `global.json`, `rollForward: latestFeature`).

Build/run:
- `dotnet restore EnglishMasterAI.sln`
- `dotnet run --project src/EnglishMasterAI.Web/EnglishMasterAI.Web.csproj` (or `./run.ps1`, which prefers a local `.dotnet\dotnet.exe` if present)
- SQLite dev database auto-migrates/seeds on startup at `src/EnglishMasterAI.Web/Data/app.db`

Format/build/test (mirrors CI in `.github/workflows/ci.yml`):
- `dotnet format EnglishMasterAI.sln --verify-no-changes --no-restore`
- `dotnet build EnglishMasterAI.sln --configuration Release`
- `dotnet test tests/EnglishMasterAI.Tests/EnglishMasterAI.Tests.csproj --configuration Release --settings coverlet.runsettings --collect:"XPlat Code Coverage"`
- Single test: `dotnet test tests/EnglishMasterAI.Tests/EnglishMasterAI.Tests.csproj --filter "FullyQualifiedName~ClassName.MethodName"`
- Coverage gate: 60% line / 40% branch, enforced via `scripts/Test-CoverageThreshold.ps1`
- `dotnet list EnglishMasterAI.sln package --vulnerable --include-transitive`

PostgreSQL integration tests (separate workflow, needs a live Postgres):
- `dotnet test tests/EnglishMasterAI.Tests/EnglishMasterAI.Tests.csproj --filter "Category=PostgreSql"`
- Requires `POSTGRES_CONNECTION_STRING` / `POSTGRES_TEST_CONNECTION_STRING` env vars

E2E (Playwright, skips itself unless configured):
- `dotnet test tests/EnglishMasterAI.E2E/EnglishMasterAI.E2E.csproj` — tests use `[E2EFact]`, which auto-skips unless `E2E_BASE_URL` is set to a running instance
- CI boots the app with `AI__Enabled=false` and a scratch SQLite DB before running these

AI eval harness (separate tool, hits a real OpenAI-compatible API — costs money, don't run casually):
- `dotnet run --project tools/EnglishMasterAI.AiEval/EnglishMasterAI.AiEval.csproj -- --dataset evals/ai-feedback-golden.json --report artifacts/ai-eval/report.json`
- Needs `OPENAI_API_KEY`; scheduled weekly in `.github/workflows/ai-eval.yml`, not part of normal CI

PostgreSQL migrations (SQLite migrations live in the Web project; Postgres ones are separate):
```powershell
dotnet ef database update `
  --project src/EnglishMasterAI.Migrations.PostgreSql/EnglishMasterAI.Migrations.PostgreSql.csproj `
  --startup-project src/EnglishMasterAI.Migrations.PostgreSql/EnglishMasterAI.Migrations.PostgreSql.csproj `
  --context ApplicationDbContext --configuration Release
```

Docker/staging: `docker compose --env-file .env up --build -d`; backup/restore via `scripts/Backup-Postgres.ps1`, `scripts/Verify-PostgresBackup.ps1`, `scripts/Restore-Postgres.ps1` (the latter requires `-ConfirmRestore` and refuses known production DB names).

Secrets (AI key, Azure Speech, dev admin) are configured via `dotnet user-secrets set --project src/EnglishMasterAI.Web/EnglishMasterAI.Web.csproj <key> <value>` — never hardcode them.

## Architecture

**Solution layout** (`EnglishMasterAI.sln`):
- `src/EnglishMasterAI.Web` — the app: Blazor Web App (interactive server render mode) + minimal REST API, ASP.NET Core Identity, all business logic
- `src/EnglishMasterAI.Data` — shared EF Core layer: `ApplicationDbContext`, `ApplicationUser`, and domain entities (`Domain/LearningModels.cs`) used by both the Web project (SQLite) and the Postgres migrations project
- `src/EnglishMasterAI.Migrations.PostgreSql` — Postgres-only EF Core migrations assembly, kept separate from the SQLite migrations that live inside `EnglishMasterAI.Web/Data/Migrations`. The DB provider is chosen at startup by `DatabaseOptions.IsPostgreSql`, switching both the connection and the `MigrationsAssembly`.
- `tests/EnglishMasterAI.Tests` — unit + integration tests (xUnit), including a `[Category=PostgreSql]` subset that needs a real database
- `tests/EnglishMasterAI.E2E` — Playwright learner-journey tests, self-skip without `E2E_BASE_URL`
- `tools/EnglishMasterAI.AiEval` — standalone console tool that runs the golden AI-feedback dataset against a live model for quality regression tracking

**Web project internals** (`src/EnglishMasterAI.Web`):
- `Program.cs` is the single composition root — all DI registration, middleware pipeline, health checks, rate limiting, and OpenTelemetry wiring happen here in sequence. Read it top-to-bottom to see how a feature is wired rather than hunting for a separate `Startup`/DI-extension layer.
- `Application/` — service layer (one class per capability: `LearningService`, `AssessmentService`, `WritingFeedbackService`, `SpeakingAnalysisService`, `PronunciationAssessmentService`, `SrsScheduler`, `ContentReviewService`, `OperationsDashboardService`, etc.) plus `Contracts.cs` holding shared DTOs/records returned to Blazor components and API endpoints
- `Api/LearningEndpoints.cs` — the entire REST surface under `/api/v1`, minimal-API style, mapped via `MapLearningApi()`; endpoints resolve the user from `ClaimsPrincipal` and call into `Application/` services
- `Components/Pages/*.razor` — the Blazor UI (Dashboard, Learn, ToeicCenter, SpeakingPractice, WritingPractice, ListeningLab, Onboarding, Placement, Admin pages, etc.) — interactive-server render mode, no WASM
- `Configuration/*Options.cs` — strongly-typed options bound from config sections (`AiOptions`, `SecurityOptions`, `DatabaseOptions`, `PronunciationOptions`, `MultiInstanceOptions`, `ObservabilityOptions`, `AlertingOptions`, `ProxyOptions`, `LegalOptions`, `ToeicMediaOptions`, `AudioStorageOptions`) — this is the map of everything externally configurable
- `Infrastructure/` — cross-cutting concerns: `StartupSecurityValidator` (fails fast on unsafe production config — rejects `SeedAdmin`, wildcard `AllowedHosts`, SQLite, or unconfirmed-email flows unless explicitly overridden), `SecurityHeadersMiddleware`, `GlobalExceptionHandler`, `DistributedRateLimiting` (Redis-backed, only when `MultiInstance:Enabled`), health checks, `LearningTelemetry` (OpenTelemetry ActivitySource/Meter)

**Data/domain model**: entities live in `EnglishMasterAI.Data/Domain/LearningModels.cs` and are exposed via `ApplicationDbContext` (`LearnerProfile`, `CourseModule`, `Lesson`, `VocabularyItem`, `AssessmentQuestion`, `LearningProgress`, `PlacementAttempt`, `ReviewSchedule`, `WritingSubmission`, `SpeakingSubmission`, `ContentRevision`, `AuditFinding`, `LearningActivity`, `LearnerAchievement`, `AiUsageRecord`, `ContentReviewAssignment`). Seeding logic is in `EnglishMasterAI.Web/Data/SeedData.cs`.

**Multi-provider database**: dev defaults to SQLite (`src/EnglishMasterAI.Web/Data/app.db`); staging/production use PostgreSQL. `Database:Provider` / `DatabaseOptions.IsPostgreSql` picks the EF provider and migrations assembly at startup — SQLite migrations are embedded in the Web project's own assembly, Postgres migrations are a dedicated project. Production instances never auto-migrate; the one-off `--migrate-and-seed` CLI arg (checked at the top of `Program.cs`) runs migration+seed then exits, used by the release/CI jobs instead of `Database__ApplyMigrationsOnStartup`.

**AI/audio integration boundary**: `OpenAiGateway` and `PronunciationAssessmentService`/Azure Speech are the only calls to external AI providers. Every AI-backed feature (writing/speaking feedback, TTS listening audio, pronunciation scoring) has an explicit local/transparent fallback when no API key is configured — the system is designed to never fabricate a score or silently degrade; see `README.md` for the "no API key" behavior matrix. `AiUsageService` / `AiPracticeLimiter` track usage and enforce request limits; `OperationsDashboardService` and `/admin/operations` surface AI usage, failures, and fallback status.

**Security posture baked into startup**: `StartupSecurityValidator` runs synchronously in `Program.cs` before the app starts serving and throws on unsafe production configuration (see above). Treat this as the authoritative list of "things production config must never do" rather than re-deriving it from scattered options classes.

**Multi-instance/scale-out**: `MultiInstanceOptions.Enabled` toggles Redis-backed shared state — data protection keys, distributed rate limiting (`DistributedRateLimiting.cs`) — for running more than one Web instance behind a load balancer. Off by default (single-instance, file-backed data protection keys, in-memory rate limiting).

**Content**: TOEIC audio/media is described by `content/toeic-media/manifest.json` and validated structurally by `scripts/Test-ToeicMediaManifest.ps1` (run in CI). Curriculum/roadmap source docs (Thai) live under `roadmap/*.txt` and are reference material, not code.

**Docs worth reading before large changes**: [docs/PRODUCTION_ROADMAP.md](docs/PRODUCTION_ROADMAP.md) tracks production-hardening status/blockers; [docs/TOEIC_MEDIA_GUIDE.md](docs/TOEIC_MEDIA_GUIDE.md) covers the TOEIC media pipeline; [deploy/README.md](deploy/README.md) covers the staging release runbook.

**Test conventions** (`tests/EnglishMasterAI.Tests`): most files are unit tests for one service each (`AssessmentScorerTests`, `SrsSchedulerTests`, `PcmWaveParserTests`); the rest are broader integration-style suites — `ApiContractTests`, `ServiceCoverageTests`, `InfrastructureHardeningTests`, `ProductionReadinessTests`, `AiIntegrationTests`, `AiQualityEvaluationTests`, `BackupRestoreScriptTests`. `PostgreSqlIntegrationTests` is tagged `[Trait("Category", "PostgreSql")]` and needs a live Postgres — it's excluded from the default `dotnet test` run and only executed by `postgresql.yml` via `--filter "Category=PostgreSql"`. `EnglishMasterWebFactory` (`WebApplicationFactory<Program>`) is the shared integration-test fixture: it boots the app against a scratch SQLite DB in a temp directory, disables AI/SeedAdmin/email-confirmation, and swaps in `TestAuthenticationHandler` for a fake authenticated user — reuse or extend it instead of building a new `WebApplicationFactory`.

**Config sections → Options classes** (`appsettings.json` top-level keys, each bound 1:1 by `SectionName` in `Program.cs`): `Database` → `DatabaseOptions`, `AudioStorage` → `AudioStorageOptions`, `MultiInstance` → `MultiInstanceOptions`, `ToeicMedia` → `ToeicMediaOptions`, `AI` → `AiOptions`, `Email` → `EmailOptions`, `Security` → `SecurityOptions`, `Proxy` → `ProxyOptions`, `Legal` → `LegalOptions`, `Pronunciation` → `PronunciationOptions`, `Observability` → `ObservabilityOptions`, `Alerting` → `AlertingOptions`.
