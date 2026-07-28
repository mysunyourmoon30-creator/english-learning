# Deployment runbook

## Staging

1. Copy `deploy/staging/.env.example` to a secret-managed `.env`.
2. Set `APP_IMAGE`, PostgreSQL credentials, `ALLOWED_HOSTS`, SMTP and API credentials.
3. Run `docker compose --env-file .env -f deploy/staging/compose.yaml up -d`.
4. Verify `/health/live`, `/health/ready`, sign-in, one lesson, one AI fallback and the operations dashboard.
5. Run `scripts/Verify-PostgresBackup.ps1` before promoting the image.

## Production promotion

1. Record the immutable staging image digest.
2. Take a PostgreSQL backup and run the restore verification against a temporary database.
3. Disable automatic migration for multi-instance rollout; run the PostgreSQL migration project once as a release job.
4. Deploy one canary instance, verify readiness and error/AI-cost dashboards, then continue.
5. Retain the previous image digest and database backup for rollback.

Never place credentials in Compose files or source control. Use the deployment platform's
secret manager and terminate HTTPS at a trusted reverse proxy or ingress.
