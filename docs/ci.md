# CI pipeline

Every change is built and tested on GitHub before it reaches `main`. The workflow is `.github/workflows/ci.yml` (card F-08).

## When it runs

| Trigger | Runs on |
|---|---|
| Pull request (opened, new commits pushed) | The PR's merge result |
| Push to `main` (after a merge) | `main` |
| Manual: **Actions → CI → Run workflow** | The branch you pick |

If you push again while a run for the same branch or PR is still going, the older run is cancelled.

## What it does

One job, **Build and test**, on `ubuntu-24.04`, with a 30-minute limit. Its steps mirror the local commands in README:

| # | Step | Local equivalent | Fails when |
|---|---|---|---|
| 1 | Check out, set up .NET from `global.json`, cache NuGet packages | – | – |
| 2 | `dotnet restore` | `dotnet restore` | A package can't be resolved |
| 3 | `dotnet build -warnaserror` | `dotnet build -warnaserror` | Any compiler or analyzer warning |
| 4 | Rebuild CSS and compare with `wwwroot/css/app.css` | `tools/tailwind.sh` | Razor classes changed but the compiled CSS wasn't rebuilt and committed |
| 5 | `tools/playwright-install.sh --with-deps` | `tools/playwright-install.sh` (once) | – |
| 6 | `dotnet test` (unit, integration, E2E) | `dotnet test` | Any test fails |
| 7 | Upload test results (`.trx`) | – | Always uploaded |
| 8 | Upload Playwright traces | – | Uploaded only when the job failed |

### The database in CI
CI never touches Neon. A `postgres:17` service container runs next to the job, and the job sets:

```
ConnectionStrings__Test = Host=localhost;Port=5432;Username=akusiap;Password=akusiap-ci;Database=postgres
```

The tests create their own `akusiap_test_<guid>` databases on that server and drop them afterwards (D-15, D-29), exactly as they do against Neon on a dev machine. These credentials only exist inside the throwaway container, so they are not secrets. The workflow needs no repository secrets at all.

### Permissions
The workflow token is read-only (`contents: read`). CI can't push, publish or deploy. Deployment is a separate workflow (OPS-02, see `docs/deployment.md`).

## Reading the results
- **On the PR:** the **CI / Build and test** check shows ✅ or ❌. Click **Details** for the log of each step.
- **Artifacts** (bottom of the run summary page, kept for 14 days):
  - `test-results`: one `.trx` file per test project. Open it in Visual Studio, or read the failing test's message in the step log.
  - `playwright-traces` (failed runs only): one `.zip` per E2E test. Replay it step by step, with screenshots, DOM and network:
    ```
    powershell -ExecutionPolicy Bypass -File tests/Akusiap.E2E/bin/Debug/net10.0/playwright.ps1 show-trace <file>.zip
    ```
    Or drag the file onto https://trace.playwright.dev (it runs in your browser; the trace isn't uploaded).

To record traces locally too, set `PLAYWRIGHT_TRACES_DIR` before `dotnet test`:
```
PLAYWRIGHT_TRACES_DIR=TestResults/playwright dotnet test tests/Akusiap.E2E
```

## Common failures

| Symptom | Fix |
|---|---|
| Build step: `error CA…` or `IDE…` | A warning that's treated as an error. Fix it locally with `dotnet build -warnaserror`. |
| "wwwroot/css/app.css is stale" | Run `tools/tailwind.sh` and commit `src/Akusiap.Web/wwwroot/css/app.css`. |
| A DB test fails only in CI | CI uses a fresh PostgreSQL 17 with no Neon data. The test may rely on rows it didn't create; tests may only use rows they create (AGENTS.md). |
| E2E test fails only in CI | Download `playwright-traces` and replay the trace. CI runs headless Chromium on Linux, so check fonts, timing and the 360×640 layout. |
| Run cancelled | A newer push to the same branch replaced it. |

## Keeping `main` green
Recommended setting (repository owner, one time): **Settings → Branches → Add branch ruleset** for `main` → *Require status checks to pass* → add **Build and test**. PRs then can't be merged until CI passes.

## Changing the pipeline
- Validate the workflow before pushing: `tools/bin/actionlint/actionlint.exe .github/workflows/ci.yml` (download it from https://github.com/rhysd/actionlint/releases; `tools/bin` is git-ignored).
- Keep the steps aligned with the README commands, so "works locally" means "works in CI".
- Action versions are pinned to major tags (`actions/checkout@v7`, `actions/setup-dotnet@v6`, `actions/cache@v6`, `actions/upload-artifact@v7`).
