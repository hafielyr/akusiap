# Decisions and assumptions

Agents apply the **Default** below. The owner can override any entry by editing **Status** to `Overridden` and writing the new rule. Agents add new entries at the bottom when a task forces a choice the stories don't settle, and set Status to `Assumed`.

| ID | Question | Default | Status |
|---|---|---|---|
| D-01 | Tech stack | See `architecture.md` (.NET 10, Razor Pages + htmx/Alpine, PostgreSQL, Hangfire, Midtrans) | Decided |
| D-02 | Scoring of multi-select questions | All or nothing: correct only when the selected set equals the correct set | Assumed |
| D-03 | Buying a package while one is active | Extends from the current `CurrentPeriodEnd` (stacks); it does not restart today | Assumed |
| D-04 | Buying an exam already owned and active | Extends `AccessExpiresAt` by `AccessDays` from the current expiry | Assumed |
| D-05 | Guests on a free exam | No trial. Show "Register free to unlock" | Assumed |
| D-06 | ADM-21 (retry failed renewals) and ADM-22 (remove access after grace) in Release 1 | Packages are prepaid and auto-renew is Later (P-08, P-09, ID-17). In R1, access ends at `CurrentPeriodEnd` through entitlement checks plus a nightly job that marks the subscription Expired. ADM-21 retries move to the auto-renew release | Assumed |
| D-07 | Own questions after access ends | View and practise are allowed; create and edit are blocked (read-only) | Assumed |
| D-08 | Trial questions per paid exam | Admin marks `ExamQuestion.IsTrial`. Publishing a paid exam requires 20–40 trial questions, or all of them if the exam has fewer than 30 | Assumed |
| D-09 | Attempt timer authority | The server computes the deadline as `StartedAt + TimeLimitMinutes` from `RulesSnapshotJson` (no extra column). The client timer is display only. Answers saved after expiry are rejected, and the attempt is auto-submitted on next access or by a Hangfire sweep | Assumed |
| D-10 | Payment gateway for launch | Midtrans only. Xendit can be added later through `IPaymentGateway` | Assumed |
| D-11 | Allowance when a user holds several entitlements | Take the maximum of the Free plan, the active subscription plan caps and the active purchased exams' `OwnQuestionAllowance` (not the sum) | Assumed |
| D-12 | Who may set exam rules in a session | The learner may change rules only within the exam's default preset bounds: question count ≤ the exam size, time limit ≥ the preset's or unlimited in Practice. Simulation mode always uses the preset as is | Assumed |
| D-13 | htmx major version | Pin htmx 2.0.x (2.0.11). htmx 4.0 is out, but 2.x is stable and better known to coding agents. Revisit after Release 2 | Decided |
| D-14 | Solution file format | `Akusiap.slnx` (the .NET 10 default) with central package versions in `Directory.Packages.props`. Never put a `Version` on a `PackageReference` | Decided |
| D-15 | Database for development and tests (no Docker or local Postgres allowed on the dev PC) | Hosted PostgreSQL 17 on Neon (free tier, AWS Singapore). Dev connection string comes from user-secrets. Integration tests create a uniquely named database on that server per run and drop it after. CI (GitHub Actions) uses a `postgres:17` service container. Dev email is written to `.eml` files instead of Mailpit | Assumed |
| D-16 | Production hosting | Hetzner Cloud VPS in Singapore (2 vCPU, 2–4 GB) running the app container with Docker + Caddy (automatic HTTPS), Cloudflare in front (DNS, proxy). Database: Neon Postgres in AWS Singapore on a paid plan at launch (no scale-to-zero, point-in-time restore). Images are built in GitHub Actions, pushed to GHCR and deployed over SSH. Nothing is built locally | Decided |
| D-17 | Dev email sender and production fallback (F-02) | The dev `IEmailSender` uses `SmtpClient` in pickup-directory mode, which writes a full MIME `.eml` to `Email:PickupDirectory` (default `.dev-mail`, relative to the Web content root), so there is no extra mail library. It is registered in Development only; other environments get no `IEmailSender` until AC-02 adds the SMTP provider | Assumed |
| D-18 | `/health` response and EF tooling (F-02) | `/health` returns JSON `{"status":…,"checks":{"database":…}}` (503 when unhealthy) using the built-in EF Core `DbContext` check. `dotnet-ef` is pinned as a local tool in `dotnet-tools.json` (`dotnet tool restore`), and `Microsoft.EntityFrameworkCore.Design` is a private design-time reference in Akusiap.Web | Assumed |
| D-19 | Database for integration tests before F-05 | Until F-05 adds `ConnectionStrings:Test` and the per-run database, integration tests start the app with `ConnectionStrings:Default`. The only test that touches the database is the `/health` check, which runs no writes | Assumed |
