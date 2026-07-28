# Deployment runbook

Production-style deployments use three rules:

1. deploy only an immutable image digest;
2. run exactly one explicit migration job before web instances;
3. keep automatic migration and seed disabled in every serving instance.

## Staging

1. Copy `deploy/staging/.env.example` to a secret-managed `.env`.
2. Set `APP_IMAGE` to `ghcr.io/...@sha256:<64 hex>`.
3. Configure PostgreSQL, Redis, SMTP, AI, alerting, and optional Azure Blob secrets.
4. Run the release script:

```powershell
.\scripts\Invoke-StagingRelease.ps1 `
  -AppImage $env:APP_IMAGE `
  -PreviousImage $env:PREVIOUS_APP_IMAGE `
  -RunBackupRestoreDrill `
  -ConfirmDeploy
```

The script pulls the digest, runs `migration` once, starts `web`, waits for
`/health/ready`, runs a load/readiness smoke test, and rolls the image back when
the new image fails. It does not perform a destructive database restore.

## Independent drills

```powershell
.\scripts\Invoke-DatabaseMigration.ps1 `
  -ComposeFile '.\deploy\staging\compose.yaml'

.\scripts\Verify-PostgresBackup.ps1 `
  -ComposeFile '.\deploy\staging\compose.yaml'

.\scripts\Invoke-DatabaseFailureDrill.ps1 `
  -ComposeFile '.\deploy\staging\compose.yaml' `
  -ConfirmDisruption

.\scripts\Test-ReadinessUnderLoad.ps1 `
  -BaseUri 'http://127.0.0.1:8080'
```

`Restore-Postgres.ps1` refuses the primary database names. A production restore
requires a separately approved recovery point and incident procedure.

## Production promotion

1. Require the CI, CodeQL, PostgreSQL, and Playwright checks.
2. Require the signed release workflow to pass, including TOEIC production media.
3. Verify the GHCR signature, provenance, and SBOM attestation.
4. Record the exact image digest and previous image digest.
5. Verify backup restore into an isolated database.
6. Run the one-off migration job.
7. Deploy a canary, check readiness/alerts/cost dashboard, then roll out.
8. Retain the previous digest and approved recovery point.

Credentials never belong in Compose files or source control. Use the deployment
platform secret manager and terminate TLS at a trusted ingress.
