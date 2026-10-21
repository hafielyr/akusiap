# Deployment guide

How LatihanSoal (code name Akusiap) goes to production at `https://latihansoal.id` (decision D-16, card OPS-02).

> **Status (2026-10-19).** The one-time setup in sections 2–5 can be done today. The deployment automation (the `Dockerfile`, the deploy workflow, and `deploy/cloud-init.yaml`, `deploy/compose.yaml` and `deploy/Caddyfile`) is built in **OPS-02**, which follows PAY-04. Until then, sections 6–8 describe how deploys will work. The examples below show what OPS-02 will commit; treat them as the reference, not as files you need to write by hand.

## 1. Overview

```
Phone browser
   │ HTTPS
   ▼
Cloudflare (DNS, proxy, TLS)                   GitHub Actions
   │ HTTPS (Full strict)                          │ build + test (CI), then image → GHCR
   ▼                                              │ deploy over SSH (manual approval)
Hetzner Cloud VPS, Singapore  ◄──────────────────┘
  ├─ Caddy  (:80/:443, TLS, reverse proxy)
  └─ app    (ASP.NET Core container, :8080)
        │ TLS
        ▼
Neon PostgreSQL 17, AWS Singapore (paid plan, no scale-to-zero, point-in-time restore)
```

Rules that never change:
- **Nothing is built on a laptop.** Images are built in GitHub Actions and pulled from GHCR (`ghcr.io/hafielyr/akusiap`). The image keeps the repository name; renaming the GitHub repository (and so the image) to `latihansoal` is the owner's call (D-92).
- **Secrets never enter git.** They live in GitHub secrets and in a root-only `.env` on the server.
- **The database changes only through EF migrations** (D-30). Never run `schema.sql` against a real database. Production has `Database__MigrateOnStartup=false`; the deploy runs the migrations bundle instead.

## 2. Domain and Cloudflare (one time)
1. Add the domain to Cloudflare and switch the registrar's nameservers to the two Cloudflare nameservers.
2. **DNS**: an `A` record for `@` (and `www` if wanted) pointing to the VPS IPv4, with **Proxied** (orange cloud) on.
3. **SSL/TLS → Overview**: mode **Full (strict)**.
4. **SSL/TLS → Origin Server → Create certificate** (RSA, hostnames `example.id, *.example.id`, 15 years). Save the certificate and the private key. Caddy uses them in section 4, which avoids Let's Encrypt challenges failing behind the Cloudflare proxy. *(OPS-02 to confirm this choice in a decision record.)*
5. **SSL/TLS → Edge Certificates**: turn on **Always Use HTTPS**, and set minimum TLS to 1.2.

## 3. Neon production database (one time)
1. In the Neon console, create a **separate project** `latihansoal-prod` in **AWS Asia Pacific (Singapore)**. Never point production at the development project.
2. Pick the paid plan (D-16). On the compute, **turn off scale-to-zero**. Set point-in-time restore to at least 7 days.
3. Create a role for the app (for example `latihansoal_app`), owner of database `latihansoal`.
4. Copy the **.NET** connection string (`Host=…;Database=latihansoal;Username=latihansoal_app;Password=…;SSL Mode=Require`). It goes into GitHub secrets and the server `.env` (sections 4 and 5).

## 4. Hetzner VPS (one time)
1. In the Hetzner Cloud Console, create a server:
   - location **Singapore**, image **Ubuntu 24.04**, 2 vCPU with 4 GB RAM (D-16);
   - add your SSH public key;
   - under *Cloud config*, paste `deploy/cloud-init.yaml` (OPS-02).
2. Create a **Hetzner firewall** for the server. Allow inbound TCP 22 from your own IP only, and 80/443 (ideally only from Cloudflare's IP ranges, https://www.cloudflare.com/ips/).
3. What cloud-init does (until OPS-02 ships it, do this by hand over SSH):
   - creates user `deploy` with your SSH key and no password, and disables root and password SSH login;
   - turns on `ufw` allowing 22, 80 and 443, and `unattended-upgrades` for security patches;
   - installs Docker Engine and the compose plugin, and adds `deploy` to the `docker` group;
   - creates `/opt/latihansoal` (owner `deploy`) and `/opt/latihansoal/keys` for the app's data-protection keys.
4. Put the Cloudflare origin certificate and key at `/opt/latihansoal/certs/origin.pem` and `/opt/latihansoal/certs/origin-key.pem` (`chmod 600` on the key).
5. Log the server in to GHCR with a GitHub token that can only read packages (fine-grained token or classic PAT with `read:packages`):
   ```
   echo <token> | docker login ghcr.io -u hafielyr --password-stdin
   ```

### Server files (`/opt/latihansoal`, committed by OPS-02 under `deploy/`)

`compose.yaml`
```yaml
services:
  app:
    image: ghcr.io/hafielyr/akusiap:${APP_TAG}
    env_file: .env
    restart: unless-stopped
    volumes:
      - ./keys:/keys            # ASP.NET data-protection keys: keeps users signed in across deploys
  caddy:
    image: caddy:2
    restart: unless-stopped
    ports: ["80:80", "443:443"]
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - ./certs:/etc/caddy/certs:ro
      - caddy_data:/data
volumes:
  caddy_data:
```

`Caddyfile`
```
example.id, www.example.id {
    tls /etc/caddy/certs/origin.pem /etc/caddy/certs/origin-key.pem
    encode zstd gzip
    reverse_proxy app:8080 {
        # Cloudflare sends the real visitor IP in CF-Connecting-IP.
        header_up X-Forwarded-For {http.request.header.CF-Connecting-IP}
    }
}
```

`.env` (owner root, `chmod 600`, written by the deploy workflow from GitHub secrets)
```
APP_TAG=<git sha deployed>
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_HTTP_PORTS=8080
ConnectionStrings__Default=Host=…;Database=latihansoal;Username=latihansoal_app;Password=…;SSL Mode=Require
Database__MigrateOnStartup=false
Seed__AdminEmail=…
Seed__AdminPassword=…
Support__WhatsAppNumber=62…
Site__BaseUrl=https://latihansoal.id          # links in account emails (AC-02, required)
Features__Sharing=true                        # shared exam links (SHR-05); false hides sharing and makes /s/... 404
Email__From=LatihanSoal <no-reply@latihansoal.id>
Email__Smtp__Host=smtp-relay.brevo.com        # or email-smtp.<region>.amazonaws.com for SES
Email__Smtp__Port=587
Email__Smtp__UserName=…
Email__Smtp__Password=…
# Background jobs (Hangfire on the same database; dashboard at /admin/jobs for admins). Jobs__Enabled=false turns them off.
#   Recurring (UTC cron): sweep-expired-attempts */5 min, expire-pending-payments */15 min, expire-access 00:05 WIB,
#   renewal-reminders 09:00 WIB (needs Site__BaseUrl and SMTP; skipped without a base URL).
# Payment channels shown at checkout (PAY-03); omit to use the defaults (all enabled, no minimum):
# Payments__Channels__0__Code=QRIS  Payments__Channels__0__Method=QRIS  Payments__Channels__0__Label=QRIS  Payments__Channels__0__MinAmountIdr=0
# Payments__Channels__1__Code=VA-BCA  …__Method=VirtualAccount  …__Channel=BCA  …__Label=BCA Virtual Account  …__Enabled=true
Authentication__Google__ClientId=…            # optional (AC-03); the Google button shows only when both are set
Authentication__Google__ClientSecret=…
# Added by later cards:
# Doku__ClientId / Doku__SecretKey                                          (PAY-04)
```

**App changes OPS-02 must make so this works:**
- store data-protection keys in `/keys`, otherwise every deploy signs everyone out;
- honour forwarded headers from Caddy (scheme and client IP);
- protect the Hangfire dashboard;
- add structured logging.

## 5. GitHub (one time)
1. **Settings → Environments → New environment `production`**: add yourself as a *required reviewer*, so every deploy waits for your approval.
2. Add the environment secrets:

   | Secret | Value |
   |---|---|
   | `DEPLOY_HOST` | VPS IPv4 |
   | `DEPLOY_USER` | `deploy` |
   | `DEPLOY_SSH_KEY` | Private key of a key pair used only for deploys (its public key goes in `~deploy/.ssh/authorized_keys`) |
   | `DEPLOY_KNOWN_HOSTS` | Output of `ssh-keyscan <VPS IP>` |
   | `PROD_CONNECTION_STRING` | Neon production connection string (used by the migrations bundle and written to `.env`) |
   | `SEED_ADMIN_EMAIL`, `SEED_ADMIN_PASSWORD` | First admin account |
   | `SUPPORT_WHATSAPP_NUMBER` | Support number, `62…` |
   | `SITE_BASE_URL`, `EMAIL_FROM`, `SMTP_HOST`, `SMTP_PORT`, `SMTP_USERNAME`, `SMTP_PASSWORD` | Account emails (AC-02). Without `SMTP_HOST` the app runs but drops emails with a warning |
   | `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET` | Google sign-in (AC-03), optional. In Google Cloud Console create an OAuth client of type "Web application" with the authorized redirect URI `https://<your domain>/signin-google` (needs the forwarded-headers change from OPS-02 so the app sees https) |
   | Later: `DOKU_CLIENT_ID`/`DOKU_SECRET_KEY` | Added with PAY-04 |
3. Protect `main` so that only PRs that passed CI can be merged (see `docs/ci.md`).

## 6. Deploying (after OPS-02)
1. Merge a PR into `main`. CI runs (`docs/ci.md`).
2. The **Deploy** workflow starts on `main`:
   1. builds the image and pushes it to GHCR as `ghcr.io/hafielyr/akusiap:<git sha>`;
   2. builds an **EF Core migrations bundle** (`efbundle`) for that commit;
   3. **waits for your approval** (the `production` environment), then over SSH:
      - writes `.env` from the secrets;
      - runs the bundle against Neon;
      - sets `APP_TAG=<git sha>`;
      - runs `docker compose pull && docker compose up -d`;
   4. checks `https://<domain>/health` until it reports `{"status":"Healthy",…}`.
3. You can also run **Actions → Deploy → Run workflow** by hand, for example to redeploy or roll back.

**Migrations run before the new version starts**, while the old version is still serving. Every migration must therefore work with the previous app version too: add columns as nullable or with defaults first, and remove things in a later release (expand, then contract).

## 7. After a deploy
- `https://<domain>/health` shows `"database":"Healthy"`.
- The home page loads in Indonesian on a phone; sign in as the admin.
- On the server: `docker compose -f /opt/latihansoal/compose.yaml ps` shows both containers *Up*, and `docker compose logs --tail 100 app` has no errors.

## 8. Rollback
- **App**: run **Actions → Deploy → Run workflow** with the previous tag (the git sha of the last good deploy, listed under GHCR package versions). By hand on the server: set `APP_TAG=<previous sha>` in `.env`, then `docker compose up -d`.
- **Database**: migrations are not rolled back automatically. Because migrations are backward compatible (section 6), the old app version keeps working on the new schema. If data itself must be restored, use Neon **point-in-time restore**: restore to a new branch first, check it, then switch over. Never restore over production blindly.

## 9. Routine operations

| Task | How |
|---|---|
| View logs | `docker compose -f /opt/latihansoal/compose.yaml logs -f app` |
| Restart the app | `docker compose -f /opt/latihansoal/compose.yaml restart app` |
| Free disk space | `docker image prune -f` (old image versions) |
| OS updates | Automatic (`unattended-upgrades`); reboot during a quiet hour when `/var/run/reboot-required` exists |
| Database backups | Neon point-in-time restore (paid plan); check the retention window once a quarter |
| Rotate a secret | Update the GitHub secret, then redeploy so `.env` is rewritten |

## 10. Before the first public launch
- [ ] OPS-02 is done: `Dockerfile`, deploy workflow, `deploy/` files, data-protection keys volume, forwarded headers
- [ ] Legal pages live (AC-05); email confirmation and reset work with the production SMTP sender (AC-02)
- [ ] DOKU production keys set and a real small payment tested (PAY-04 to PAY-06)
- [ ] A strong admin password, and the `Seed__AdminPassword` secret removed or rotated after first sign-in
- [ ] Neon production on the paid plan, scale-to-zero off, point-in-time restore on
- [ ] Cloudflare Full (strict), the origin certificate installed, and the Hetzner firewall limited to Cloudflare IPs on 80/443
- [ ] `main` protected by the CI check; the `production` environment requires approval
