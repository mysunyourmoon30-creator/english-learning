# EnglishMaster AI — Production Hardening Roadmap

อัปเดตล่าสุด: 2026-07-28

## สถานะสรุป

| งาน | สถานะ | หลักฐาน |
|---|---|---|
| One-off database migration | เสร็จและทดสอบระดับโค้ด | web process ถูกห้าม migrate/seed นอก Development; ใช้ `--migrate-and-seed` เท่านั้น |
| Test coverage gate | ผ่าน | 59 tests; line 66.97%, branch 50.36%; CI บังคับขั้นต่ำ 60%/40% |
| PostgreSQL integration | พร้อมใน CI | migration job รันซ้ำ 2 รอบ, provider integration test และ backup/restore |
| Playwright learner journey | ผ่านในเครื่องและพร้อมใน CI | Chromium ทดสอบสมัครสมาชิก → เปิดบทเรียน → TOEIC audio UI สำเร็จ |
| TOEIC production media gate | ระบบเสร็จ; รอ content จริง | release ถูกบล็อกจนมี human audio 100 ชิ้น, Part 1 images 6 ภาพ, license และ expert approval |
| Scheduled live AI evaluation | พร้อม; รอ secret | fixed dataset, model-as-judge, prompt-injection/hallucination/cost gates |
| Supply-chain hardening | เสร็จใน repo | Actions pin ด้วย SHA, Trivy, SPDX SBOM, attestations, Cosign, immutable digest |
| GitHub repository settings | รอการยืนยันก่อนเปลี่ยน settings | ตรวจพบว่า `main` ยังไม่มี classic branch protection และ Dependency graph/Dependabot ยังปิดอยู่ |
| Multi-instance foundation | เสร็จ | Redis rate limit, Redis Data Protection keys, Redis readiness, Azure Blob audio store |
| Real staging drill | ถูกบล็อกด้วยเครื่อง | Docker Desktop เปิดแล้วแต่แจ้ง `Virtualization support not detected`; engine stopped |

Coverage ไม่นับ generated Razor markup (`*.razor`), generated `obj` และ EF migrations
เพราะ UI ถูกตรวจด้วย Playwright และ migration ถูกตรวจด้วย PostgreSQL workflow แยกต่างหาก

## 1. Safe production migrations

- [x] `appsettings.Production.example.json` ปิด
  `ApplyMigrationsOnStartup` และ `SeedOnStartup`
- [x] startup validator ปฏิเสธ automatic migration/seed นอก Development
- [x] เพิ่ม explicit one-off mode:
  `dotnet EnglishMasterAI.Web.dll --migrate-and-seed`
- [x] Compose เพิ่ม service `migration`; web รอ
  `condition: service_completed_successfully`
- [x] PostgreSQL CI รัน migration/seed job สองครั้งเพื่อยืนยัน idempotency
- [x] เพิ่ม `scripts/Invoke-DatabaseMigration.ps1`

กฎ deploy: migration job ต้องสำเร็จก่อนเริ่ม web instance ใหม่ทุกครั้ง ห้ามเปิด
startup migration เพื่อแก้ปัญหาเฉพาะหน้า

## 2. Coverage and tests

- [x] Authentication boundary: unauthenticated API = 401 และ Playwright สมัครสมาชิกจริง
- [x] Assessment/Learning/Review/Personal data/Content review workflows
- [x] PostgreSQL provider integration
- [x] SMTP, AI provider และ operational alert failures
- [x] PCM WAV parsing: header, truncated chunk, duration limit
- [x] backup/restore destructive guards
- [x] Playwright registration, lesson navigation และ TOEIC audio UI
- [x] CI threshold: line 60%, branch 40%

คำสั่งตรวจ:

```powershell
dotnet test '.\tests\EnglishMasterAI.Tests\EnglishMasterAI.Tests.csproj' `
  --configuration Release `
  --settings '.\coverlet.runsettings' `
  --collect:'XPlat Code Coverage' `
  --results-directory '.\artifacts\coverage'

$report = Get-ChildItem '.\artifacts\coverage' -Recurse `
  -Filter 'coverage.cobertura.xml' |
  Select-Object -First 1 -ExpandProperty FullName

powershell -ExecutionPolicy Bypass `
  -File '.\scripts\Test-CoverageThreshold.ps1' `
  -CoverageFile $report
```

## 3. Staging acceptance

### งานที่ทำใน repo แล้ว

- [x] PostgreSQL + Redis Compose health checks
- [x] one-off migration service
- [x] guarded backup and isolated restore test
- [x] immutable image deployment script
- [x] automatic image rollback เมื่อ readiness/load smoke ล้มเหลว
- [x] readiness-under-load smoke test
- [x] controlled database pause/recovery drill พร้อม explicit confirmation
- [x] AI/SMTP/database failure paths มี automated tests และ alert delivery tests

### ผลการทดลองบนเครื่องนี้

วันที่ 2026-07-28 เปิด Docker Desktop สำเร็จ แต่หน้า Dashboard แสดง:

> Virtualization support not detected

Docker engine จึงไม่เริ่มและไม่สามารถรัน PostgreSQL/Redis containers จริงได้
งานด้าน BIOS/virtualization และการ sign in ต้องให้เจ้าของเครื่องดำเนินการเอง

### รันต่อหลัง engine พร้อม

```powershell
docker info
docker compose --env-file '.\.env' up --build --detach
docker compose ps

.\scripts\Verify-PostgresBackup.ps1
.\scripts\Invoke-DatabaseFailureDrill.ps1 -ConfirmDisruption
.\scripts\Test-ReadinessUnderLoad.ps1 -BaseUri 'http://127.0.0.1:8080'
```

สำหรับ staging image ที่ release แล้ว:

```powershell
.\scripts\Invoke-StagingRelease.ps1 `
  -AppImage 'ghcr.io/mysunyourmoon30-creator/english-learning@sha256:<digest>' `
  -PreviousImage 'ghcr.io/mysunyourmoon30-creator/english-learning@sha256:<previous-digest>' `
  -RunBackupRestoreDrill `
  -ConfirmDeploy
```

Database rollback ไม่ทำอัตโนมัติ เพราะอาจทำให้ข้อมูลหลัง backup สูญหาย ต้องใช้
forward-compatible migration เป็นค่าเริ่มต้น และ restore เฉพาะหลัง incident owner
อนุมัติ recovery point แล้วเท่านั้น

## 4. TOEIC production content

- [x] runtime catalog อ้างอิง item ด้วย SHA-256 content key
- [x] production ปิด AI-generated TOEIC fallback
- [x] production validator บังคับ approved human media mode
- [x] Azure Blob-backed shared audio storage และ CDN configuration
- [x] Part 1 UI รองรับ licensed HTTPS image
- [x] release gate ตรวจ:
  - human recordings 100 ชิ้นสำหรับ Parts 1–4
  - สำเนียง US, UK, Australian และ Canadian
  - Part 1 images 6 ภาพ
  - license ID/evidence, SHA-256, approver และ approval timestamp
  - normalization -18 ถึง -14 LUFS และ true peak ไม่เกิน -1 dBTP
  - expert clarity approval
- [ ] จัดหาไฟล์เสียงมนุษย์และรูปภาพที่มีสิทธิ์ใช้จริง
- [ ] ให้ผู้เชี่ยวชาญตรวจและลงชื่ออนุมัติทุก asset

ห้ามคัดลอกข้อสอบ เสียง หรือภาพของ ETS/TOEIC โดยไม่ได้รับอนุญาต Release workflow
จะล้มเหลวในขั้น `Test-ToeicMediaManifest.ps1 -Mode Production` จนกว่า catalog
จะครบตามเกณฑ์

## 5. Live AI evaluation

- [x] dataset คงที่ใน `evals/ai-feedback-golden.json`
- [x] Responses API structured output และ `store:false`
- [x] ตรวจ grammar, usefulness, hallucination safety และ prompt injection
- [x] เก็บ input/output tokens และบังคับ estimated cost ceiling
- [x] scheduled/workflow-dispatch เท่านั้น ไม่รันทุก push
- [ ] ตั้ง GitHub secret `OPENAI_API_KEY`
- [ ] ตรวจ report จริงอย่างน้อยหนึ่งรอบก่อนเปลี่ยน model/prompt

## 6. Supply chain and GitHub

- [x] GitHub Actions ทุกตัว pin ด้วย immutable commit SHA
- [x] NuGet vulnerability scan
- [x] Trivy HIGH/CRITICAL container gate
- [x] SPDX JSON SBOM
- [x] build provenance และ SBOM attestations
- [x] keyless Cosign signature
- [x] deployment artifact บันทึก `APP_IMAGE=...@sha256:...`
- [x] script สำหรับ branch protection, required checks, secret scanning,
  push protection และ Dependabot security updates
- [ ] authenticate GitHub CLI หรือเชื่อม GitHub connector
- [ ] รัน `scripts/Configure-GitHubSecurity.ps1`
- [ ] ตรวจว่า required checks ปรากฏครบหลัง workflow รันครั้งแรก

## 7. Multi-instance

- [x] Redis fixed-window gate ใช้ atomic Lua operation
- [x] Redis-backed shared Data Protection keys
- [x] Redis persistence ใน Compose และ readiness check
- [x] Azure Blob audio object store พร้อม idempotent concurrent write
- [x] production validator ห้าม multi-instance + local audio filesystem
- [x] production validator ห้าม Azure Blob configuration ที่ไม่มี connection string
- [x] normal web process ไม่แก้ schema/database seed

Production ต้องใช้ Redis ที่เปิด persistence และ Azure Blob container ที่ provision
ล่วงหน้า `CreateContainerIfMissing` ควรเป็น `false`

## External prerequisites ก่อน production

- Virtualization/WSL2 หรือ Linux container host ที่ใช้งานได้
- GitHub authentication และสิทธิ์ admin ของ repository
- PostgreSQL, Redis และ Azure Blob production services
- HTTPS ingress, SMTP, OTLP backend และ alert webhook
- OpenAI/Azure Speech secrets จาก secret manager
- licensed TOEIC media และ human expert sign-off
- backup retention, off-site encryption, RPO/RTO และ incident owner ที่อนุมัติแล้ว
