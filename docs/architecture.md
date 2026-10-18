# Architecture

Source of truth for requirements: `Quiz Practice App — User Stories.docx` (story IDs such as G-01 and ADM-07) and `ERD.jpg` (tables and columns). This file holds the technical decisions that agents must follow.

## Stack

| Layer | Choice |
|---|---|
| Runtime | .NET 10 (LTS), ASP.NET Core |
| UI | Razor Pages + htmx + Alpine.js, Tailwind CSS via the standalone CLI (no Node toolchain). htmx and Alpine are vendored in `wwwroot/lib` |
| Data | PostgreSQL 17 (Neon in dev, D-15), EF Core 10 (Npgsql), code-first migrations. Use JSONB for `*Json` columns |
| Auth | ASP.NET Core Identity (`User` : `IdentityUser<Guid>`), Google login. Roles: `Learner`, `Teacher`, `Admin` |
| Jobs | Hangfire with PostgreSQL storage. Dashboard at `/admin/jobs` (Admin only) |
| Payments | Midtrans Core API behind `IPaymentGateway`; `FakePaymentGateway` for dev and tests |
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
  Akusiap.Infrastructure/  AppDbContext, EF configurations, migrations, Midtrans client, email, Hangfire jobs, importers
  Akusiap.Web/             Razor Pages (Areas: Admin, Account, Learn), htmx partials, Alpine components, webhook endpoints
tests/
  Akusiap.UnitTests/         domain and application tests (no DB)
  Akusiap.IntegrationTests/  per-run test database + WebApplicationFactory
  Akusiap.E2E/               Playwright, mobile viewport
```

Rules:
- Domain references nothing. Application references Domain. Infrastructure references Application. Web references Application and Infrastructure (Infrastructure only for composition).
- Use one feature folder per area, for example `Application/Sessions/StartAttempt.cs` with a request record, a handler and a validator. Do not add MediatR; handlers are plain classes registered in DI.
- Read time through `IClock` only, never `DateTime.UtcNow`, so tests can control expiry.
- Store all datetimes in UTC (`timestamptz`) and convert to `Asia/Jakarta` for display.
- Store money as `long` Rupiah (column suffix `Idr`), tax-inclusive. Never use `decimal` for prices.
- Get every access decision from `EntitlementPolicy` (below). Pages and handlers never check purchases directly.

## Enums (from ERD)

| Entity.Field | Values |
|---|---|
| Exam.OwnerKind | App, Teacher, Learner |
| Exam.Status | Draft, Published, Unpublished, TakenDown |
| Question.QuestionType | SingleChoice, MultiSelect, TrueFalse |
| Question.Status | Draft, Active, Retired |
| RulePreset.ShowAnswers | AfterEach, AfterSubmit, Never |
| ExamPurchase.Status | PendingPayment, Active, Expired, Refunded |
| Subscription.Status | PendingPayment, Active, Expired, Canceled |
| Subscription.RenewalMode | Manual, AutoDebit |
| Attempt.Source | Exam, Trial, Shared, WrongOnly, Review, CustomMix |
| Attempt.Mode | Practice, Simulation, Flashcard |
| Attempt.Status | InProgress, Submitted, Expired |
| Payment.Status | Pending, Paid, Expired, Failed, Refunded |
| Payment.Method | QRIS, VirtualAccount, EWallet, RetailOutlet, PayLater, Card |
| Refund.Status | Requested, Approved, Rejected, Processed |
| ReviewCard.State | New, Learning, Review, Memorized |
| ReviewLog.Rating | Forgot, Hard, Good, Easy |
| ContentReport.Status | Open, Fixed, TakenDown, Rejected |
| Notification.Channel | WhatsApp, Email, InApp |
| Plan.Code | Free, Learner, Teacher, Combined |

## Entitlement rules (priority: access to owner-created exams)

`EntitlementPolicy.Evaluate(ViewerContext viewer, ExamAccessFacts exam, DateTime now) → AccessDecision { Level, Reason, QuestionScope }`

`Level`: `None`, `Trial`, `Full`, `Owner`, `Admin`, `Shared`. `QuestionScope`: `TrialOnly` or `All`.

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
