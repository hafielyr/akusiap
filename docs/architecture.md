# Architecture

Source of truth for requirements: `Quiz Practice App — User Stories.docx` (story IDs such as G-01 and ADM-07) and `schema.sql` (tables, columns, constraints; D-26). `ERD.jpg` is an older diagram kept for reference; where they differ, `schema.sql` wins. This file holds the technical decisions that agents must follow.

## Stack

| Layer | Choice |
|---|---|
| Runtime | .NET 10 (LTS), ASP.NET Core |
| UI | Razor Pages + htmx + Alpine.js, Tailwind CSS via the standalone CLI (no Node toolchain). htmx and Alpine are vendored in `wwwroot/lib` |
| Data | PostgreSQL 17 (Neon in dev, D-15), EF Core 10 (Npgsql), code-first migrations. Use JSONB for JSON columns (`payload`, `rules_snapshot`, `option_order`, `changes`) |
| Auth | ASP.NET Core Identity (`User` : `IdentityUser<Guid>`), Google login. Roles: `Learner`, `Teacher`, `Admin` |
| Jobs | Hangfire with PostgreSQL storage. Dashboard at `/admin/jobs` (Admin only) |
| Payments | DOKU behind `IPaymentGateway` (D-10); Xendit added later for international payments through the same interface. `FakePaymentGateway` for dev and tests |
| Email | `IEmailSender` → Brevo or SES in production; `.eml` files in `.dev-mail/` during development (D-15) |
| i18n | `.resx` localization. `id-ID` is the default culture, `en` is optional. Money is formatted as `Rp49.000` |
| Import | ClosedXML (xlsx) + CsvHelper |
| Tests | xUnit, WebApplicationFactory, a per-run test database on the hosted Postgres (CI: service container), Playwright for .NET (360×640 viewport) |
| Hosting | Hetzner VPS (Singapore) running the Docker image behind Caddy, Neon Postgres (Singapore), Cloudflare in front. CI and image builds on GitHub Actions (D-16) |

All libraries are open source. Hangfire core is LGPL-3.0, which is fine as a NuGet reference. Do not add commercial libraries (for example Duende IdentityServer or Telerik) without a decision record.

## Solution layout

```
Akusiap.slnx
src/
  Akusiap.Domain/          entities, enums, pure domain services (EntitlementPolicy, Scoring, Sm2Scheduler, RefundPolicy, AllowancePolicy)
  Akusiap.Application/     use cases grouped by feature folder; interfaces (IAppDbContext, IPaymentGateway, IEmailSender, IClock)
  Akusiap.Infrastructure/  AppDbContext, EF configurations, migrations, DOKU client, email, Hangfire jobs, importers
  Akusiap.Web/             Razor Pages (Areas: Admin, Account, Learn), htmx partials, Alpine components, webhook endpoints
tests/
  Akusiap.UnitTests/         domain and application tests (no DB)
  Akusiap.IntegrationTests/  per-run test database + WebApplicationFactory
  Akusiap.E2E/               Playwright, mobile viewport (360×640)
  Akusiap.TestSupport/       shared test code: TestDatabase, PostgresFixture, AppFactory, TestClock, FakeEmailSender, TestData (D-29)
```

Rules:
- Domain references nothing. Application references Domain. Infrastructure references Application. Web references Application and Infrastructure (Infrastructure only for composition).
- Use one feature folder per area, for example `Application/Sessions/StartAttempt.cs` with a request record, a handler and a validator. Do not add MediatR; handlers are plain classes registered in DI.
- Read time through `IClock` only, never `DateTime.UtcNow`, so tests can control expiry.
- Store all datetimes in UTC (`timestamptz`) and convert to `Asia/Jakarta` for display.
- Store money as `long` Rupiah (column suffix `Idr`), tax-inclusive. Never use `decimal` for prices.
- Get every access decision from `EntitlementPolicy` (below). Pages and handlers never check purchases directly.

## Enums (from schema.sql)

| Entity.Field | Values |
|---|---|
| Exam.OwnerKind | App, Teacher, Learner |
| Exam.Status | Draft, Published, Unpublished, TakenDown |
| Question.QuestionType | SingleChoice, MultiSelect, TrueFalse |
| Question.Status | Draft, Active, Retired |
| RulePreset.ShowAnswers | AfterEach, AfterSubmit, Never |
| ExamPurchase.Status | AwaitingPayment, Active, Expired, Abandoned, Refunded |
| Subscription.Status | AwaitingPayment, Active, Expired, Abandoned, Refunded |
| Subscription.RenewalMode | Manual, AutoDebit |
| SavedPaymentMethod.Type | CreditCard, DirectDebit |
| Attempt.Source | Exam, Trial, Shared, WrongOnly, Review, CustomMix |
| Attempt.Mode | Practice, Simulation, Flashcard |
| Attempt.Status | InProgress, Submitted, Expired |
| Payment.Gateway | DOKU (Xendit later) |
| Payment.Status | Pending, Paid, Expired, Canceled, Failed, Refunded |
| Payment.Method | QRIS, VirtualAccount, EWallet, CreditCard |
| Refund.Status | Requested, Approved, Rejected, AwaitingDetails, SentToDoku, Completed, Failed |
| Refund.RefundRoute | DirectToPayerApp, RefundLink |
| ReviewCard.State | New, Learning, Review, Memorized |
| ReviewLog.Rating | Forgot, Hard, Good, Easy |
| ContentReport.Status | Open, Fixed, TakenDown, Rejected |
| Notification.Channel | Email, InApp |
| Notification.TemplateCode | Receipt, Renewal7d, Renewal1d, RefundLink, ReviewDue |
| Notification.Status | Queued, Sent, Delivered, Read, Failed |
| Plan.Code | Free, Learner, Teacher, Combined |

## Entitlement rules (priority: access to owner-created exams)

`EntitlementPolicy.Evaluate(ViewerContext viewer, ExamAccessFacts exam, string? shareToken, DateTime now) → AccessDecision { Level, Reason, Scope, RequiresGuestName, CanStart }` (`src/Akusiap.Domain/Access/EntitlementPolicy.cs`, D-32)

`Level`: `None`, `Trial`, `Full`, `Owner`, `Admin`, `Shared`. `Scope`: `TrialOnly` or `All` (`None` when Level is None). `Reason` (`AccessReason`) tells pages which message or call to action to show.

Evaluate in this order; the first match wins:

1. The viewer is an Admin → `Admin`, all questions.
2. The viewer owns the exam (`Exam.OwnerUserId == viewer.UserId`) → `Owner`, all questions, at any status except TakenDown (TakenDown: view only, cannot start).
3. `Exam.Status == TakenDown` → `None` ("taken down").
4. A share token is supplied, `Exam.IsShared`, the token matches `ShareToken` and `OwnerKind == Learner` → `Shared`, all questions. Guests must enter a name.
5. `Exam.Status != Published` → `None` (not in catalog).
6. `Exam.IsFree`: registered → `Full`; guest → `None` with reason `RegisterToUnlock`.
7. The viewer has an `ExamPurchase` for this exam with `Status == Active && AccessExpiresAt > now` → `Full`.
8. The viewer has a `Subscription` with `Status == Active && CurrentPeriodEnd > now` whose `Plan.IncludesAllExams` → `Full`.
9. A paid app exam with trial questions → `Trial`, `TrialOnly` (guests and registered users, no attempt limit).
10. Otherwise → `None`.

Deactivated users (`IsActive == false`) cannot sign in, so they never reach evaluation. Teacher class assignments (Release 3) will add a rule between 7 and 8.

## Assumptions (change via `docs/decisions.md`)

See `docs/decisions.md`. Agents follow the recorded default unless a later decision overrides it.
