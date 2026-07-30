# EnglishMaster AI

> Production hardening status, blockers, and executable rollout steps are tracked in
> [docs/PRODUCTION_ROADMAP.md](docs/PRODUCTION_ROADMAP.md). Production web instances
> never migrate automatically; use the one-off `--migrate-and-seed` job.

ระบบเรียนภาษาอังกฤษสำหรับ AI Engineer สร้างด้วย .NET 10 LTS, Blazor Web App,
ASP.NET Core Identity และ EF Core รองรับ SQLite สำหรับ development และ PostgreSQL
สำหรับ staging/production

## ความสามารถหลัก

- หลักสูตร English Core E01–E17 และ AI English AI01–AI24
- Placement Test, personalized roadmap และ TOEIC Parts 1–7/Mock 200 ข้อ
- TOEIC Listening Parts 1–4 เล่นเสียงโดยไม่เปิด transcript ก่อนตอบ
- Listening/Shadowing พร้อม reference audio ที่สร้างและแคชจาก TTS
- Speaking recording เป็น PCM WAV และ acoustic pronunciation assessment ผ่าน Azure Speech
- Writing/Speaking feedback แบบ structured AI พร้อม transparent local fallback
- Vocabulary spaced repetition, achievement, timezone-aware streak และ weekly progress
- Content revision/audit และ reviewer sign-off สองบทบาทสำหรับ AI01–AI24
- Structured logging, OpenTelemetry, health checks, alert webhook และ operations dashboard
- REST API ภายใต้ `/api/v1` พร้อม authentication และ rate limiting

เสียงจาก TTS จะแสดงข้อความกำกับว่าเป็นเสียงที่สร้างโดย AI ระบบไม่อ้างคะแนน
pronunciation จาก transcript และไม่เก็บ raw learner audio โดยค่าเริ่มต้น

## เริ่มระบบในเครื่อง

ต้องใช้ .NET SDK 10 ตาม `global.json`

```powershell
Set-Location -LiteralPath 'D:\Project\C#\english-learning'
.\run.ps1
```

`run.ps1` จะใช้ `.dotnet\dotnet.exe` หากมี SDK แบบ local ในโครงการ ไม่เช่นนั้นจะใช้
`dotnet` จาก PATH

รันด้วยคำสั่งตรง:

```powershell
dotnet restore '.\EnglishMasterAI.sln'
dotnet run --project '.\src\EnglishMasterAI.Web\EnglishMasterAI.Web.csproj'
```

SQLite migrations และ seed data จะทำงานเมื่อแอปเริ่ม โดยฐานข้อมูล development อยู่ที่
`src/EnglishMasterAI.Web/Data/app.db`

## ตั้งค่า AI และ pronunciation

เก็บ credentials ใน User Secrets หรือ secret manager เท่านั้น:

```powershell
dotnet user-secrets set --project '.\src\EnglishMasterAI.Web\EnglishMasterAI.Web.csproj' 'AI:ApiKey' '<openai-api-key>'
dotnet user-secrets set --project '.\src\EnglishMasterAI.Web\EnglishMasterAI.Web.csproj' 'Pronunciation:Enabled' 'true'
dotnet user-secrets set --project '.\src\EnglishMasterAI.Web\EnglishMasterAI.Web.csproj' 'Pronunciation:AzureSpeechKey' '<azure-speech-key>'
dotnet user-secrets set --project '.\src\EnglishMasterAI.Web\EnglishMasterAI.Web.csproj' 'Pronunciation:AzureSpeechRegion' '<azure-region>'
```

เมื่อไม่มี API key:

- Writing/Speaking ใช้ local rubric ที่อธิบายข้อจำกัดชัดเจน
- Listening ใช้เสียงสังเคราะห์ของ browser
- ระบบไม่สร้าง acoustic pronunciation score ปลอม

## Development admin

สร้าง admin เฉพาะ Development ผ่าน User Secrets:

```powershell
dotnet user-secrets set --project '.\src\EnglishMasterAI.Web\EnglishMasterAI.Web.csproj' 'SeedAdmin:Enabled' 'true'
dotnet user-secrets set --project '.\src\EnglishMasterAI.Web\EnglishMasterAI.Web.csproj' 'SeedAdmin:Email' 'admin@localhost'
dotnet user-secrets set --project '.\src\EnglishMasterAI.Web\EnglishMasterAI.Web.csproj' 'SeedAdmin:Password' '<strong-password>'
```

Production validator จะปฏิเสธ `SeedAdmin`, wildcard `AllowedHosts`, SQLite โดยไม่ได้
explicit override และการบังคับยืนยันอีเมลที่ไม่มี SMTP

## PostgreSQL และ migrations

SQLite migrations อยู่ใน Web project ส่วน PostgreSQL migrations แยกอยู่ใน
`EnglishMasterAI.Migrations.PostgreSql`

```powershell
$env:POSTGRES_CONNECTION_STRING = 'Host=localhost;Port=5432;Database=englishmaster;Username=englishmaster;Password=<password>'
dotnet tool restore
dotnet ef database update `
  --project '.\src\EnglishMasterAI.Migrations.PostgreSql\EnglishMasterAI.Migrations.PostgreSql.csproj' `
  --startup-project '.\src\EnglishMasterAI.Migrations.PostgreSql\EnglishMasterAI.Migrations.PostgreSql.csproj' `
  --context ApplicationDbContext `
  --configuration Release
```

## Docker และ staging

คัดลอก `.env.example` เป็น `.env`, เปลี่ยน credentials แล้วรัน:

```powershell
docker compose --env-file '.\.env' up --build -d
docker compose ps
```

Staging template และ release runbook อยู่ใน `deploy/staging` และ `deploy/README.md`
container ทำงานด้วย non-root user และมี liveness/readiness health checks

สำรองและทดสอบ restore:

```powershell
.\scripts\Backup-Postgres.ps1
.\scripts\Verify-PostgresBackup.ps1
```

`Restore-Postgres.ps1` ต้องระบุ `-ConfirmRestore` และปฏิเสธชื่อฐานข้อมูล production
ที่กำหนดไว้ เพื่อป้องกันการเขียนทับโดยไม่ได้ตั้งใจ

## ตรวจคุณภาพ

```powershell
dotnet format '.\EnglishMasterAI.sln' --verify-no-changes --no-restore
dotnet build '.\EnglishMasterAI.sln' --configuration Release
dotnet test '.\EnglishMasterAI.sln' --configuration Release --settings '.\coverlet.runsettings' --collect:'XPlat Code Coverage'
dotnet list '.\EnglishMasterAI.sln' package --vulnerable --include-transitive
```

GitHub Actions รัน format, build, tests, coverage, vulnerability scan, Docker build,
PostgreSQL migration/backup-restore test และ CodeQL ทุก push/PR ส่วน Dependabot ตรวจ
NuGet, Actions และ Docker ทุกสัปดาห์

## Health และ operations

- `GET /health/live` — process liveness
- `GET /health/ready` — database readiness
- `GET /healthz` — รายละเอียด health แบบ JSON
- `/admin/operations` — AI usage/failure/fallback และสถานะ integration
- `/admin/content-reviews` — reviewer workflow สำหรับ AI01–AI24

ตั้ง `Observability__OtlpEndpoint` เพื่อส่ง traces/metrics ไป OTLP collector และตั้ง
`Alerting__WebhookUrl` เป็น HTTPS endpoint เพื่อรับ AI/database/SMTP failure alerts
ตั้ง rate ล่าสุดจากผู้ให้บริการใน `AI__InputTokenUsdPerMillion`,
`AI__OutputTokenUsdPerMillion`, `AI__SpeechUsdPerMillionCharacters` และ
`AI__TranscriptionUsdPerMinute` เพื่อให้ dashboard คำนวณค่าใช้จ่ายประมาณการ

## สถานะ production

โค้ดและ automation พร้อมสำหรับ staging แต่ก่อน production จริงยังต้องจัดหา
PostgreSQL host, SMTP, OpenAI/Azure Speech keys, OTLP backend, alert webhook,
HTTPS ingress และให้ผู้เชี่ยวชาญภาษากับ AI ตรวจและกดอนุมัติบทเรียน AI01–AI24

ดูแผนและเกณฑ์ส่งมอบทั้งหมดใน `docs/PRODUCTION_ROADMAP.md`

## สัญญาอนุญาต

ซอฟต์แวร์ในโครงการนี้เผยแพร่ภายใต้ Apache License 2.0 ดูตัวบทเต็มใน `LICENSE`

เนื้อหาหลักสูตรและข้อสอบที่เขียนขึ้นสำหรับโครงการนี้ **สงวนลิขสิทธิ์แยกต่างหาก**
ไม่ได้อยู่ภายใต้ Apache License ครอบคลุมทั้ง `roadmap/`, `content/curriculum/`,
`content/toeic-media/` และ `evals/` ผู้ที่นำโค้ดไปใช้ต่อต้องจัดหาเนื้อหาหลักสูตร
ของตนเอง

รายละเอียดขอบเขตและสัญญาอนุญาตของฟอนต์กับไลบรารีที่แนบมาอยู่ใน `NOTICE`
