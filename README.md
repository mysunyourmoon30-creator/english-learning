# EnglishMaster AI

ระบบเรียนภาษาอังกฤษสำหรับ AI Engineer ตามแผนในโฟลเดอร์ `roadmap` สร้างด้วย .NET 9 Blazor Web App, ASP.NET Core Identity, EF Core และ SQLite

## ความสามารถปัจจุบัน

- Onboarding, Placement Test และ Personalized Roadmap
- English Core E01–E17 พร้อม Lesson Player, Quiz และ Progress
- AI English AI01–AI24 พร้อม Reading, Listening, Speaking และ Writing prompt
- Vocabulary Review แบบ spaced repetition
- Listening/Shadowing และ browser speech synthesis
- Speaking Recording, transcription และ rubric feedback
- Writing Feedback แบบ structured AI พร้อม local fallback ที่อธิบายกฎได้
- TOEIC Quick Diagnostic Parts 1–7
- TOEIC Practice Simulation 200 ข้อ จับเวลา 120 นาที
- Lesson Editor, Content Revision, Audit Workflow และ Rollback
- REST API ภายใต้ `/api/v1` โดยไม่ส่งเฉลยก่อน submit
- Rate limiting, security headers, health checks และ Problem Details
- ดาวน์โหลดหรือลบข้อมูลการเรียนส่วนบุคคลได้จากหน้า Account

## เริ่มระบบ

เปิด PowerShell ที่โฟลเดอร์โครงการ:

```powershell
Set-Location -LiteralPath 'D:\Project\C#\english-learning'
dotnet restore '.\EnglishMasterAI.sln'
dotnet run --project '.\src\EnglishMasterAI.Web\EnglishMasterAI.Web.csproj'
```

เปิด URL ที่แสดงหลัง `Now listening on:` แล้วสมัคร learner account ได้ทันทีใน Development

Migration และข้อมูลหลักสูตรที่ขาดจะถูก apply/seed โดยอัตโนมัติเมื่อแอปเริ่มทำงาน ข้อมูลและ progress เดิมจะไม่ถูกลบ

## Development admin

รหัส admin ไม่ถูกเก็บใน repository แล้ว สำหรับฐานข้อมูลใหม่ให้ตั้งค่าผ่าน .NET User Secrets:

```powershell
dotnet user-secrets set --project '.\src\EnglishMasterAI.Web\EnglishMasterAI.Web.csproj' 'SeedAdmin:Enabled' 'true'
dotnet user-secrets set --project '.\src\EnglishMasterAI.Web\EnglishMasterAI.Web.csproj' 'SeedAdmin:Email' 'admin@localhost'
dotnet user-secrets set --project '.\src\EnglishMasterAI.Web\EnglishMasterAI.Web.csproj' 'SeedAdmin:Password' '<choose-a-strong-password>'
```

Password ต้องมีอย่างน้อย 12 ตัวอักษร และประกอบด้วยตัวพิมพ์ใหญ่ ตัวพิมพ์เล็ก ตัวเลข และอักขระพิเศษ

หากอัปเกรดจาก MVP รุ่นก่อน บัญชี admin ที่อยู่ในฐานข้อมูลเดิมยังใช้งานได้ตามเดิม

## เปิด AI Writing และ Speaking

ระบบทำงานได้โดยไม่มี API key:

- Writing ใช้ transparent local rules
- Speaking ใช้ browser transcript บน Chrome/Edge และ local rubric

หากต้องการ server transcription และ structured AI feedback ให้ตั้ง OpenAI API key ผ่าน User Secrets:

```powershell
dotnet user-secrets set --project '.\src\EnglishMasterAI.Web\EnglishMasterAI.Web.csproj' 'AI:ApiKey' '<your-api-key>'
```

หรือใช้ environment variable:

```powershell
$env:AI__ApiKey = '<your-api-key>'
```

ค่าตั้งต้นใช้:

- Feedback: `gpt-5.6-luna`
- Transcription: `gpt-4o-mini-transcribe`
- ไม่เก็บ response ที่ผู้ให้บริการด้วย `store: false`
- จำกัด AI practice ต่อผู้ใช้ตาม `Security:AiRequestsPerMinute`

สามารถเปลี่ยน model ผ่าน `AI__FeedbackModel` และ `AI__TranscriptionModel`

## Production configuration

ดูตัวอย่างใน `appsettings.Production.example.json` และส่งค่าลับผ่าน environment variables หรือ secret manager เท่านั้น

ค่าที่จำเป็นสำหรับ Production:

- กำหนด `AllowedHosts` เป็น hostname จริง ห้ามใช้ `*`
- ตั้ง SMTP และ `Email__Enabled=true` เพื่อยืนยันอีเมลและ reset password
- ตั้ง `DataProtection__KeysPath` เป็น persistent/shared storage
- ใช้ HTTPS ที่ reverse proxy หรือ host
- ปิด `SeedAdmin`
- สำรองฐานข้อมูลก่อน deploy migration

SQLite เหมาะกับ local และ single-instance deployment หากต้องรองรับหลาย instance หรือปริมาณเขียนสูง ควรย้ายไป PostgreSQL/SQL Server พร้อมสร้าง provider-specific migrations และ shared Data Protection keys ก่อนเปิด production

## ทดสอบ

```powershell
dotnet test '.\EnglishMasterAI.sln' --configuration Release
```

ชุดทดสอบครอบคลุม scorer, SRS, API contract, authentication, security headers, health check, migration/seed, TOEIC 200 ข้อ, AI fallback, speaking privacy behavior และ content revision

## Health และ API

- `GET /healthz` — ตรวจ application และ database
- `GET /api/v1/health` — lightweight service status
- `GET /api/v1/catalog/modules`
- `GET /api/v1/assessment/placement/questions`
- `POST /api/v1/assessment/placement/submit`
- `GET /api/v1/assessment/toeic/questions`
- `POST /api/v1/assessment/toeic/submit`
- `GET /api/v1/assessment/toeic/mock/questions`
- `POST /api/v1/assessment/toeic/mock/submit`

ทุก API ยกเว้น health ต้องผ่าน authentication และ rate limit

## ข้อจำกัดที่ระบุไว้ชัดเจน

- TOEIC Listening ใน Practice Simulation ใช้ transcript จำลอง ยังไม่ใช่ชุดเสียงสอบจริง
- Speaking rubric ไม่เดาคะแนน pronunciation จาก transcript
- AI feedback เป็นผู้ช่วยตรวจเบื้องต้น ผู้เรียนควรทบทวนคำแนะนำก่อนนำไปใช้
- เนื้อหา AI01–AI24 เป็นบทเรียนเริ่มต้นที่พร้อมขยายผ่าน Content Studio ไม่ใช่หลักสูตรเชิงลึกเต็มบททุก module
