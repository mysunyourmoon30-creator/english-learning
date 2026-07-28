# EnglishMaster AI — Production Roadmap

อัปเดตล่าสุด: 2026-07-28

## สถานะโดยสรุป

งานระดับ source code และ automation ตามแผนดำเนินการแล้วทั้งหมด ส่วนการเปิด staging,
การทดสอบ PostgreSQL restore บน infrastructure จริง และ human sign-off ต้องใช้
credentials/บุคลากรภายนอก repository

| Phase | สถานะ | หลักฐาน/สิ่งที่เหลือ |
|---|---|---|
| Security baseline | เสร็จ | .NET 10, SQLitePCLRaw 2.1.12, NuGet scan ไม่พบช่องโหว่ |
| CI/security automation | เสร็จ | CI, coverage, CodeQL, Dependabot |
| PostgreSQL/deployment | โค้ดเสร็จ | migrations, Docker, staging, backup/restore; รันจริงเมื่อ Docker/PostgreSQL พร้อม |
| Observability | เสร็จ | JSON logs, OTel, alerts, health, operations dashboard |
| Learning engagement | เสร็จ | achievements, streak, weekly progress |
| Content quality workflow | ระบบเสร็จ | รอผู้เชี่ยวชาญตรวจและอนุมัติ AI01–AI24 |
| Audio/pronunciation | ระบบเสร็จ | รอ OpenAI/Azure credentials เพื่อสร้างเสียงและประเมินเสียงจริง |
| Production release | รอ external setup | staging sign-off, secrets, HTTPS, SMTP, OTLP และ deployment approval |

## Phase 0 — Security baseline

- [x] ย้าย target framework เป็น .NET 10 LTS
- [x] อัปเดต ASP.NET Core/EF Core เป็น 10.0.10
- [x] pin `SQLitePCLRaw.bundle_e_sqlite3` เป็น 2.1.12 ซึ่งไม่อยู่ใน advisory
- [x] รัน `dotnet list package --vulnerable --include-transitive`
- [x] เพิ่ม Dependabot และ CodeQL
- [x] สำรอง SQLite ก่อน migration และไม่ commit database/secrets

Definition of Done: Release build ผ่าน, scan ไม่พบ vulnerable package และไม่มี
credentials ใน source control

## Phase 1 — Continuous Integration

- [x] Restore, format, build และ test ทุก push/PR
- [x] สร้าง Cobertura coverage และอัปโหลดเป็น artifact
- [x] ทำให้ workflow ล้มเหลวเมื่อ NuGet พบ vulnerable package
- [x] CodeQL สำหรับ C#
- [x] Dependabot สำหรับ NuGet, GitHub Actions และ Docker
- [x] build container ใน CI

งานตั้งค่าหลัง push: เปิด branch protection ให้บังคับ CI, CodeQL และ PostgreSQL
workflow ผ่านก่อน merge

## Phase 2 — Production data and deployment

- [x] แยก `EnglishMasterAI.Data` ออกจาก Web project
- [x] รองรับ `Database:Provider` เป็น SQLite/PostgreSQL
- [x] แยก provider-specific migrations คนละ assembly
- [x] multi-stage .NET 10 Dockerfile, non-root user และ health check
- [x] local Compose และ staging Compose template
- [x] PostgreSQL backup, guarded restore และ automated restore verification
- [x] PostgreSQL CI service apply migration และ restore ไปฐานใหม่

งาน external: เปิด Docker/PostgreSQL, รัน staging workflow และกำหนด retention/
off-site encryption policy ของ backup

## Phase 3 — Observability and operations

- [x] structured JSON logging นอก Development
- [x] OpenTelemetry traces/metrics สำหรับ ASP.NET Core และ outbound HTTP
- [x] custom AI request/failure/fallback/latency และ learning activity metrics
- [x] เก็บ AI usage metadata โดยไม่เก็บ prompt/audio
- [x] operations dashboard สำหรับ request, token, failure และ fallback
- [x] HTTPS webhook alert พร้อม cooldown
- [x] database health monitor และ liveness/readiness endpoints

งาน external: ต่อ OTLP collector/dashboard และ alert receiver จริง แล้วทดสอบ
notification routing/on-call ownership

## Phase 4 — Learning quality and engagement

- [x] achievement badges
- [x] timezone-aware streak จากกิจกรรมจริง
- [x] weekly 7-day progress
- [x] idempotent activity keys ป้องกันคะแนนซ้ำ
- [x] review assignment สองบทบาทสำหรับ AI01–AI24
- [x] ห้าม publish AI lesson หาก review ไม่ครบหรือมี High/Critical finding

งาน external: ผู้เชี่ยวชาญภาษาอังกฤษและ AI ต้องตรวจเนื้อหาและกด sign-off จริง
ระบบไม่สามารถรับรองแทนมนุษย์ได้

## Phase 5 — Audio, pronunciation and AI quality

- [x] สร้าง/แคช TTS reference audio พร้อม disclosure ว่าเป็นเสียง AI
- [x] TOEIC Listening Parts 1–4 เล่นเสียงและไม่ส่ง transcript ใน question API
- [x] ซ่อนข้อความตัวเลือกของ Parts 1–2 ที่ผู้เรียนต้องฟัง
- [x] browser speech fallback เมื่อไม่มี TTS key
- [x] บันทึกเสียงผู้เรียนเป็น PCM WAV ใน memory
- [x] Azure Speech acoustic pronunciation assessment
- [x] แสดง pronunciation score เฉพาะเมื่อ acoustic provider สำเร็จ
- [x] ไม่ persist raw learner audio โดยค่าเริ่มต้น
- [x] AI golden tests สำหรับ schema bounds, prompt-injection sample และ fallback behavior

งาน external: จัดหา OpenAI/Azure Speech credentials, ทดสอบเสียงกับผู้เรียนจริง และ
กำหนดงบประมาณ/voice acceptance criteria

## Phase 6 — Staging และ production release

- [ ] เปิด staging ด้วย immutable image digest
- [ ] ตรวจ `/health/live`, `/health/ready`, sign-in, lesson, audio และ AI fallback
- [ ] รัน PostgreSQL backup/restore verification
- [ ] ตรวจ dashboard/alerts และทำ failure drill
- [ ] ให้ผู้เชี่ยวชาญ sign-off AI01–AI24
- [ ] ตั้ง HTTPS, SMTP, secrets และ persistent Data Protection keys
- [ ] บันทึก deployment approval และ rollback image/database

## Credentials และบริการที่ต้องจัดหาภายนอก

- OpenAI API key สำหรับ structured feedback, transcription และ TTS
- Azure Speech key/region สำหรับ acoustic pronunciation scoring
- PostgreSQL staging/production
- SMTP credentials
- OTLP collector/observability backend
- HTTPS alert webhook
- ผู้เชี่ยวชาญภาษาอังกฤษและ AI สำหรับ content sign-off
