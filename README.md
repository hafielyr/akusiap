# Akusiap: Quiz Practice App

Exam practice web app for Indonesian learners. Requirements: `Quiz Practice App — User Stories.docx` and `ERD.jpg`. Technical design: `docs/architecture.md`. Work tracking: `docs/kanban.md`. Agents: read `AGENTS.md` first.

## Prerequisites

- .NET SDK 10.0.401+ (pinned in `global.json`)
- Nothing else locally. The database is a hosted PostgreSQL (see F-02). The Tailwind CLI is downloaded on first use.

## Run locally

```
dotnet run --project src/Akusiap.Web --launch-profile http   # http://localhost:5240
```

`/health` returns `Healthy` when the app is up.

## Styles

CSS comes from `src/Akusiap.Web/Styles/app.css` through the Tailwind standalone CLI. The compiled `wwwroot/css/app.css` is committed so the app runs without the CLI. Rebuild it after changing classes in Razor views:

```
tools/tailwind.sh            # Git Bash / Linux / macOS
pwsh tools/tailwind.ps1      # PowerShell
tools/tailwind.sh --watch    # rebuild on change
```

## Test

```
dotnet build -warnaserror
dotnet test
```

## Front-end libraries

Vendored in `src/Akusiap.Web/wwwroot/lib` (no CDN at runtime): htmx 2.0.11 (0BSD) and Alpine.js 3.17.4 (MIT).
