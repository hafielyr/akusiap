# Agent guide: Akusiap Quiz Practice App

Read before every task:
1. `docs/architecture.md`: stack, layout, enums, entitlement rules. Follow it exactly.
2. `docs/decisions.md`: defaults for open questions.
3. `docs/kanban.md`: what to work on.
4. `docs/backlog.md`: the full card for your task.

Requirements come from `Quiz Practice App — User Stories.docx` (story IDs) and `schema.sql` (database schema, D-26; it supersedes `ERD.jpg`). If the backlog card and those disagree, the stories and `schema.sql` win; record the conflict in `docs/decisions.md`.

## Workflow for one task

1. **Pick**: Take the top card in `## Ready` on `docs/kanban.md`. Move it to `## In Progress` with `(your-agent-name, YYYY-MM-DD)`. If In Progress already has 3 cards, stop.
2. **Branch**: `git switch -c task/<ID>-<short-slug>` (for example `task/ENT-01-entitlement-policy`).
3. **Build**: Implement only the card's scope. Write tests first for domain rules. Keep changes inside the folders named in `architecture.md`.
4. **Verify**: Run `dotnet build -warnaserror` and `dotnet test`, plus the card's E2E test if it has one. Check every AC bullet yourself.
5. **Record**: When you had to make a choice the docs don't cover, add a `D-xx` row to `docs/decisions.md` with Status `Assumed`.
6. **Hand off**: Move the card to `## Review`. Then, for every card in `## Backlog` whose deps are now all in Review or Done, move it to the end of `## Ready`, keeping backlog priority order. Commit as `<ID>: <title>` and open a PR listing each AC bullet with ✅ or ❌.
7. **Blocked**: If you can't finish (missing credentials, a conflicting requirement or a failing dependency), move the card to `## Blocked` with one line saying what is needed. Don't guess at payment, legal or pricing rules.

Only a human or a review agent moves a card from Review to Done, after the PR merges.

## Conventions

- C#: file-scoped namespaces, records for requests and DTOs, `async` all the way, `CancellationToken` on every handler, no static state.
- Handlers live in `Application/<Feature>/<UseCase>.cs`; pages in `Web/Pages/...` or `Web/Areas/Admin/Pages/...`; htmx partials are named `_<Name>Partial.cshtml`.
- Never read `DateTime.UtcNow` directly; inject `IClock`. Store money as `long` Rupiah.
- Get every access check from `IEntitlementService`. Never query purchases or subscriptions from a page.
- Put UI strings in `src/Akusiap.Web/Resources/Localization/SharedResource.resx` (Indonesian, the neutral/default) and `SharedResource.en.resx`, with dotted keys like `Catalog.SearchPlaceholder`. In views use `@T["Key"]` (injected in `_ViewImports`). Format money with `<idr value="..." />` or `Money.FormatIdr`. Keep code, identifiers, commits and docs in English.
- Migrations: one per card that changes the schema, named `<ID>_<Change>`. Never edit an applied migration.
- Tests: unit tests for domain, integration tests (per-run test database, D-15) for handlers and pages, Playwright for flows named in the AC. Use `TestData` builders; no shared mutable fixtures between tests. Support code lives in `tests/Akusiap.TestSupport` (D-29): put DB tests in `[Collection(UsesDatabase.Name)]`, take `PostgresFixture` in the constructor, create `new AppFactory(database)` per test (test clock and fake email), and only rely on rows the test created. Schema tests that need an empty database use `TestDatabase.CreateMigratedAsync()`. E2E tests use `[Collection(UsesMobileSite.Name)]` and `site.NewPhoneAsync()`.
- Secrets come from user-secrets or env vars only. Never commit keys (DOKU, Xendit, Google, SMTP).
- Pages must work at 360 px width and keep JS minimal: Alpine for local state, htmx for server round-trips, no SPA frameworks.

## Commands

```
dotnet user-secrets set "ConnectionStrings:Default" "<neon>" --project src/Akusiap.Web   # once (D-15)
dotnet build -warnaserror
dotnet test
dotnet run --project src/Akusiap.Web
dotnet ef migrations add <Name> -p src/Akusiap.Infrastructure -s src/Akusiap.Web
dotnet user-secrets set "ConnectionStrings:Test" "<neon>" --project src/Akusiap.Web      # optional; tests fall back to Default (D-19)
tools/playwright-install.sh                           # once: Chromium for E2E (or: powershell -File tools/playwright-install.ps1)
tools/tailwind.sh                                     # rebuild CSS after changing Tailwind classes
```
