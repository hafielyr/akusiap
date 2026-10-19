# Akusiap: Quiz Practice App

Exam practice web app for Indonesian learners. Requirements: `Quiz Practice App — User Stories.docx` and `schema.sql` (database schema; `ERD.jpg` is an older diagram). Technical design: `docs/architecture.md`. Work tracking: `docs/kanban.md`. Agents: read `AGENTS.md` first.

## Prerequisites

- .NET SDK 10.0.401+ (pinned in `global.json`)
- Nothing else locally. The database is a hosted PostgreSQL (see "Run locally"). The Tailwind CLI is downloaded on first use.

## Run locally

The app needs a PostgreSQL connection string; there is no local database (D-15). Create a free Neon project (region AWS Singapore), copy the connection string in the **.NET** format (`Host=...;Database=...;Username=...;Password=...;SSL Mode=Require`) and store it once with user-secrets. It never goes into the repo:

```
dotnet user-secrets set "ConnectionStrings:Default" "Host=...;Database=neondb;Username=...;Password=...;SSL Mode=Require" --project src/Akusiap.Web
dotnet run --project src/Akusiap.Web --launch-profile http   # http://localhost:5240
```

In Development the app applies pending migrations at startup and seeds roles, plans, the default rule preset and sample content (a paid exam with 40 questions, 30 of them trial, and a free exam). To also get an admin account, store its credentials once:

```
dotnet user-secrets set "Seed:AdminEmail" "admin@example.com" --project src/Akusiap.Web
dotnet user-secrets set "Seed:AdminPassword" "<8+ chars with upper, lower, digit and symbol>" --project src/Akusiap.Web
```

`schema.sql` is the design reference. Never run it against the app database: EF migrations build the database, and a database created from `schema.sql` cannot be migrated (D-30).

Outside development, set the environment variable `ConnectionStrings__Default` instead. Without the setting the app stops at startup with a message naming it. Integration tests will use a separate `ConnectionStrings:Test` from F-05 on.

`/health` returns `{"status":"Healthy","checks":{"database":"Healthy"}}` when the app is up and the database answers (503 otherwise).

Emails are not sent in development: each one is written as an `.eml` file to `src/Akusiap.Web/.dev-mail/` (git-ignored). Open them with any mail client.

EF Core tools are pinned in `dotnet-tools.json`:

```
dotnet tool restore
dotnet ef database update -p src/Akusiap.Infrastructure -s src/Akusiap.Web                # apply migrations to your dev database
dotnet ef migrations add <ID>_<Change> -p src/Akusiap.Infrastructure -s src/Akusiap.Web   # e.g. F04_Sessions; lands next to the existing ones
```

## Styles

CSS comes from `src/Akusiap.Web/Styles/app.css` through the Tailwind standalone CLI. The compiled `wwwroot/css/app.css` is committed so the app runs without the CLI. Rebuild it after changing classes in Razor views:

```
tools/tailwind.sh            # Git Bash / Linux / macOS
pwsh tools/tailwind.ps1      # PowerShell
tools/tailwind.sh --watch    # rebuild on change
```

## Test

Database tests create a throwaway database (`akusiap_test_<guid>`) on the server from `ConnectionStrings:Test`, or `ConnectionStrings:Default` when Test is not set, and drop it afterwards. E2E tests drive a headless Chromium at 360×640 against the app on a real port; install the browser once:

```
tools/playwright-install.sh                                       # Git Bash / Linux / macOS
powershell -ExecutionPolicy Bypass -File tools/playwright-install.ps1   # Windows PowerShell
dotnet build -warnaserror
dotnet test
```

## Front-end libraries

Vendored in `src/Akusiap.Web/wwwroot/lib` (no CDN at runtime): htmx 2.0.11 (0BSD) and Alpine.js 3.17.4 (MIT).
