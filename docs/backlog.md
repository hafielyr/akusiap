# Backlog: Releases 1 and 2

Each task is sized for one AI-agent run: one pull request that builds, passes tests and can be checked against its acceptance criteria. The order is set in `kanban.md`; this file holds the detail.

**Card format**: ID · title · Stories (from the user-stories doc) · Depends on · Scope · Acceptance criteria (AC) · Tests.
**Size**: S = a few files; M = one feature slice (domain + handler + page + tests); L = split before starting if possible.
**Definition of Done (every task)**: `dotnet build` with no warnings, `dotnet test` green, new behaviour covered by tests, UI text in `id` and `en` resources, pages usable at 360 px width, any new assumption recorded in `docs/decisions.md`, and the card moved on `kanban.md`.

## Milestones

| Milestone | Goal | Release |
|---|---|---|
| **M1 Access to owner content** | Admin builds and publishes exams. Guests browse and take trials, registered users take free exams, and users with access granted by the admin take full exams | R1 |
| M2 Learning loop | Review, retake wrong answers, history, flashcards, reporting questions | R1 |
| M3 Payments | Buy an exam or package with QRIS, VA or e-wallet. Receipts, reminders, refunds | R1 |
| M4 Admin ops and launch | User admin, transactions, performance, deploy | R1 |
| M5 Own content | Own questions and exams within allowances | R2 |
| M6 Sharing | Share by link with guests, with takedown in place first | R2 |
| M7 Spaced repetition | SM-2 schedule and daily due list | R2 |

---

## E0 Foundation (M1)

### F-01 Solution skeleton · S
- Stories: none · Depends on: none
- Scope: Create `Akusiap.sln` with the 4 src and 3 test projects from `architecture.md`, plus `Directory.Build.props` (net10.0, nullable, `TreatWarningsAsErrors`, implicit usings), `.editorconfig`, `.gitignore` and `global.json`. Web: Razor Pages, `/health` endpoint, mobile-first `_Layout` with header, footer and a WhatsApp support button (`wa.me/{Support:WhatsAppNumber}`, ID-06). Tailwind standalone CLI script `tools/tailwind.ps1` + `.sh`. htmx and Alpine vendored in `wwwroot/lib`.
- AC: `dotnet build` and `dotnet test` pass. `dotnet run --project src/Akusiap.Web` serves `/` and `/health` returns 200. The page has a viewport meta tag and the support button.
- Tests: an integration smoke test that `/health` returns 200.

### F-02 Database and dev infrastructure · S
- Depends on: F-01
- Scope: (No Docker on the dev machine; see D-15.) `AppDbContext` registered with Npgsql and snake_case naming, with the connection strings `ConnectionStrings:Default` (and `ConnectionStrings:Test` for F-05) read from user-secrets in dev and env vars elsewhere; the app fails fast with a clear message when it's missing. A DB health check on `/health`. `IClock` and `SystemClock`. A dev `IEmailSender` stub that writes `.eml` files to `.dev-mail/` (replaces Mailpit). README "Run locally" section explaining how to set the Neon connection string with `dotnet user-secrets`.
- AC: With the secret set, `dotnet run` connects and `/health` reports the database Healthy. Without it, startup fails with a message naming the missing setting.

### F-03 Content and access schema · M
- Depends on: F-02
- Scope: Entities and EF configurations exactly as in `schema.sql` (D-26) for User (Identity, `Guid` key), Role/UserRole (Identity roles), Subject, Topic, Question, QuestionOption, Exam, ExamQuestion, RulePreset, Plan, PlanPrice, Subscription, ExamPurchase and AuditLog. Enums from `architecture.md`, stored as strings. Its CHECK constraints and unique/partial indexes, including case-insensitive User.Email, Exam.ShareToken and (ExamId, QuestionId). Migration `F03_InitialContent`.
- AC: The migration applies to an empty DB. Every `schema.sql` column for these tables exists with the right type and nullability. Money columns are `bigint`.
- Tests: an integration test that applies migrations and round-trips one of each entity.

### F-04 Session and memorization schema · S
- Depends on: F-03
- Scope: Attempt, AttemptItem, AttemptItemOption, ReviewCard, ReviewLog, ContentReport and Notification as in `schema.sql`, with its CHECK constraints and indexes. Migration `F04_Sessions`.
- AC: Same as F-03 for these tables. Composite key on AttemptItemOption.

### F-05 Test infrastructure · M
- Depends on: F-03
- Scope: Shared `PostgresFixture` that creates a uniquely named database on the server from `ConnectionStrings:Test` (user-secrets or env; CI points it at the service container), applies migrations and drops the database on dispose (D-15), and `AppFactory` (WebApplicationFactory with a test clock and fake email). `TestData` builder (`TestData.Exam().Paid(49000).WithQuestions(40, trial: 30)`). Playwright project with an `iPhone SE`-like 360×640 context and a script to install browsers.
- AC: A sample integration test and a sample E2E test (home page loads) pass locally and in CI.

### F-06 Localization and Rupiah · S
- Stories: ID-01, ID-02 · Depends on: F-01
- Scope: Request localization with `id-ID` default and `en`, a culture switcher (cookie, plus `User.PreferredLanguage` once signed in), shared `.resx`, `Money.FormatIdr(long)` → `Rp49.000` and a tag helper `<idr value="...">`.
- AC: The default UI is Indonesian. Switching to English persists across requests. 49000 renders as `Rp49.000` in both cultures.
- Tests: unit tests for `FormatIdr` (0, 1000, 1234567).

### F-07 Seed data · S
- Depends on: F-03
- Scope: An idempotent seeder at startup for roles (Learner, Teacher, Admin); the plans and the default system RulePreset with the values in the `schema.sql` seed section (Free "Gratis" with caps 50/1/50; Learner "Paket Pelajar", Teacher "Paket Pengajar" and Combined "Paket Lengkap" with caps left null until decided, which the app treats as blocked; preset "Latihan standar", AfterEach, IsDefault); and an admin user from config `Seed:AdminEmail/AdminPassword`. In Development, also seed one Subject, topics and a sample paid exam with 40 questions (30 trial) plus one free exam.
- AC: Running twice creates no duplicates. In dev, the admin can sign in.

### F-08 CI pipeline · S
- Depends on: F-05
- Scope: GitHub Actions workflow to restore, build, run unit and integration tests (Docker available), run Playwright tests and upload the trace on failure.
- AC: The workflow file is valid (`actionlint` if available) and the steps mirror the local commands in README.

## E1 Accounts (M1)

### AC-01 Register and sign in with email · M
- Stories: A-01 · Depends on: F-03, F-06
- Scope: Identity Razor pages scaffolded and restyled (Indonesian): register (email, display name, password), sign in, sign out. New users get the `Learner` role. Sign-in is rejected when `IsActive == false`.
- AC: A guest registers and lands on the catalog signed in. A deactivated user sees "Akun dinonaktifkan".
- Tests: integration tests for register, sign in and a deactivated user being blocked.

### AC-02 Email confirmation and password reset · S
- Stories: A-02 · Depends on: AC-01
- Scope: `IEmailSender` implementation (SMTP provider in production; the `.eml` file sender from F-02 in dev). Indonesian templates. Confirm-email and forgot/reset-password flows. Unconfirmed users can practise but must confirm before paying (checked in PAY-03).
- AC: The confirmation email is written to `.dev-mail/` (captured by the fake sender in tests) and its link confirms. A reset link changes the password.

### AC-03 Google sign-in · S
- Stories: A-01 · Depends on: AC-01
- Scope: Google external login (`Authentication:Google:*` config). The account is linked by email when it already exists. Google users have `EmailConfirmed = true`.
- AC: With config present, a "Masuk dengan Google" button appears. Without config, the button is hidden and the app still starts.

### AC-04 Profile, password and role · S
- Stories: A-03, A-04 · Depends on: AC-01
- Scope: Profile page (display name, phone in +62 format, preferred language), change password, and an "Saya juga mengajar" toggle that adds the Teacher role.
- AC: Phone numbers are validated (+62 followed by 8–12 digits). The language change applies immediately.

### AC-05 Legal pages · S
- Stories: A-06 · Depends on: F-06, AC-01
- Scope: Terms, Refund policy (text from the stories doc) and Privacy pages in `id` and `en`. Registration requires accepting the terms. Links appear in the footer.
- AC: Registration fails without the checkbox. All three pages render in both languages.

### AC-06 Authorization policies · S
- Depends on: AC-01
- Scope: Policies `AdminOnly` and `SignedIn`. Areas `/Admin` (AdminOnly) and `/Learn` (SignedIn where needed). 403 and 404 pages in Indonesian.
- AC: A learner gets 403 on `/admin`. An anonymous user is redirected to sign in for protected pages.

## E2 Admin content: owner-created exams (M1, priority)

### ADM-T1 Admin shell, subjects and topics · S
- Stories: ADM-03 (partial) · Depends on: AC-06, F-03
- Scope: Admin layout with navigation. CRUD for Subject and Topic (a topic belongs to a subject).
- AC: Create, edit and list work. A topic in use cannot be deleted (show a message).

### ADM-T2 Question editor · M
- Stories: ADM-01, ADM-03, ADM-31 · Depends on: ADM-T1
- Scope: Question list with filters (subject, topic, status, type, text search) and paging. Create/edit form: type, text, options (add/remove/reorder, mark correct), explanation, key fact (max 200 chars), hint, difficulty 1–5, topic, source note (syllabus reference) and status Draft/Active/Retired. Validation: SingleChoice has 2–6 options and exactly 1 correct; MultiSelect has 2–8 options and ≥1 correct; TrueFalse has exactly the 2 fixed options "Benar"/"Salah" and 1 correct. Retired questions stay in past attempts.
- AC: Every validation rule shows an Indonesian message. An edited question keeps its id. Retiring removes the question from new attempts only.
- Tests: unit tests for `QuestionValidator` per type; an integration test for create and edit.

### ADM-T3 Exam editor · M
- Stories: ADM-06, ADM-08, ADM-33, ADM-09 (exam price), ADM-28 · Depends on: ADM-T2
- Scope: Exam CRUD for `OwnerKind = App`: title, description, subject, IsFree, PriceIdr, AccessDays, OwnQuestionAllowance and default RulePreset. A free exam ignores price and access days.
- AC: A paid exam requires price > 0 and access days > 0. Saving creates an exam with Status Draft.

### ADM-T4 Exam question picker and trial set · M
- Stories: ADM-06, ADM-07 · Depends on: ADM-T3
- Scope: On an exam page, search Active questions and add or remove them (htmx), reorder (SortOrder) and toggle IsTrial. Show counters: total, trial and per topic.
- AC: Adding the same question twice is prevented. The counters update without a full page reload.

### ADM-T5 Rule presets · S
- Stories: ADM-10 · Depends on: ADM-T1
- Scope: CRUD for system presets (`OwnerUserId = null`): TimeLimitMinutes, QuestionCount, HintsEnabled, ShuffleQuestions, ShuffleOptions, MaxAttempts, PassMarkPercent, ShowAnswers and IsDefault. Exactly one system default.
- AC: Setting a new default unsets the old one. A preset used by an exam cannot be deleted.

### ADM-T6 Publish and unpublish · S
- Stories: ADM-11 · Depends on: ADM-T4
- Scope: `PublishExam` validation: ≥1 Active question; for a paid exam, the trial set follows D-08 and price and access days are set. Unpublish is allowed at any time. The status is shown in the exam list.
- AC: Publishing an invalid exam lists every problem. A published exam appears in the catalog (CAT-01).
- Tests: unit tests for publish validation.

### ADM-T7 Bulk question import · L
- Stories: ADM-02 · Depends on: ADM-T2
- Scope: A downloadable xlsx template (columns: type, text, options A–H, correct letters, explanation, key fact, hint, topic, difficulty, source note). Upload xlsx or csv, then parse and validate every row with `QuestionValidator` and topic lookup. A preview shows per-row errors. Confirm saves all rows in one transaction; nothing is saved if any row fails. Limit `MaxImportRows` from config (default 1000).
- AC: A file with one bad row saves nothing and shows the row number and error. A valid file of 500 rows imports in under 10 s locally.
- Tests: parser unit tests (xlsx and csv) and an all-or-nothing integration test.

## E3 Entitlements (M1, priority)

### ENT-01 EntitlementPolicy (domain) · M
- Stories: personas table; G-02, S-01, ADM-28 · Depends on: F-03
- Scope: Implement the rules in `architecture.md` → *Entitlement rules* as a pure function. Input records `ViewerContext` (UserId?, IsAdmin, active purchases with expiry, active subscriptions with plan flags) and `ExamAccessFacts` (Id, OwnerUserId, OwnerKind, Status, IsFree, IsShared, ShareToken, HasTrialQuestions). Return `AccessDecision` with Level, Reason and QuestionScope.
- AC: Every numbered rule has at least one test, including edges: a purchase that expires exactly now (→ no access), an expired subscription, an unpublished exam for its owner versus another user, a taken-down exam for its owner, and a guest on a free exam.
- Tests: a table-driven unit test covering all 7 personas against free, paid, unpublished and shared exams.

### ENT-02 Entitlement query and guard · M
- Depends on: ENT-01, AC-01
- Scope: `IEntitlementService.GetAsync(examId, shareToken?)` loads `ViewerContext` once per request (scoped cache) and calls the policy. `ExamQuestionScope` returns the question ids allowed for the decision (TrialOnly → IsTrial and Active; All → Active). Add a page filter or helper `RequireExamAccess` returning 403 or the CTA page.
- AC: One DB round-trip loads the viewer context per request (checked through EF logging in the test). Retired questions are never returned.
- Tests: integration tests for guest, free user, purchase holder and subscriber against a seeded paid exam.

### ENT-03 Admin grants, extends and ends access · M
- Stories: ADM-18 · Depends on: ENT-02, AC-06
- Scope: Admin page `/admin/akses` with a user lookup by exact email (USR-01 later adds full search and links here). For the found user, list purchases and subscriptions. Actions: grant an exam (creates an ExamPurchase, Status Active, PricePaidIdr 0, AccessExpiresAt = now + AccessDays or a custom date); grant a plan for N days (creates a Subscription, Active); extend the end date; end now (Status Expired). Every action writes AuditLog (actor, entity, changes JSON).
- AC: A granted user immediately gets Full access in ENT-02. Ending access makes the next request fall back to Trial or None. Audit rows exist.
- Tests: integration tests for grant → access, then end → no access.

### ENT-04 My access page · S
- Stories: P-06 · Depends on: ENT-02
- Scope: `/learn/akses` lists the user's active and expired exam purchases and subscriptions with end dates (Asia/Jakarta) and days left, and links to start practice.
- AC: Items are ordered by end date. Expired items are shown in a separate, muted section.

## E4 Catalog and guest trial (M1)

### CAT-01 Exam catalog · M
- Stories: G-01 · Depends on: ADM-T6, F-06
- Scope: `/ujian` lists Published app exams with search (title or description), a subject filter and a badge per card (Gratis / price / "Akses Anda"). Each card shows the description excerpt, active question count, topics, price and access period. Server-rendered with htmx search (debounced) and paging of 20. Each card needs a single query; no N+1.
- AC: Draft, unpublished and taken-down exams never appear. Search works without JS (form GET fallback). The page uses < 50 KB HTML and no images on first load.

### CAT-02 Exam detail with access CTA · S
- Stories: G-01, G-04, G-06 · Depends on: CAT-01, ENT-02
- Scope: `/ujian/{id}` shows the description, topic list with counts and the price. The CTA depends on the AccessDecision: Full → "Mulai latihan"; Trial → "Coba 30 soal gratis" and "Beli ujian / Berlangganan"; None + RegisterToUnlock → "Daftar gratis untuk membuka".
- AC: Each decision level renders its CTA (tested through ENT-02 fixtures).

### CAT-03 Pricing comparison · S
- Stories: G-05 · Depends on: PAY-02 (may use seeded prices before then)
- Scope: `/harga` shows a single exam (from the exam page context, or "mulai dari" the lowest price) next to learner packages for 1, 3, 6 and 12 months with the monthly-equivalent price. Mobile layout stacks the cards.
- AC: Prices come from the DB, formatted as Rupiah. The cheapest monthly option is highlighted.

## E5 Practice sessions (M1 → M2)

### SES-01 Start attempt service · L
- Stories: S-01, S-03, S-08, G-02 · Depends on: ENT-02, F-04
- Scope: `StartAttempt(examId, mode, ruleOverrides, shareToken?)`: checks the entitlement; builds rules from the exam's preset plus allowed overrides (D-12); selects questions from `ExamQuestionScope` (filtered by topics and limited by count); shuffles question order and option order per the rules (store `Position` and `OptionOrderJson`); stores `RulesSnapshotJson`. Guests get a `GuestToken` cookie (random, HttpOnly) stored on the Attempt. Respects `MaxAttempts` for Simulation. Source is `Trial` when scope is TrialOnly.
- AC: A trial attempt contains only IsTrial questions. Two attempts on the same exam have different orders when shuffle is on. Without access, starting is rejected.
- Tests: unit tests for selection and shuffle with a seeded RNG; an integration test for guest trial and full attempts.

### SES-02 Session setup page · S
- Stories: S-02, S-03 · Depends on: SES-01
- Scope: Choose Practice (feedback, hints) or Simulation (timed, no hints) and set question count, topics, time limit, hints and shuffle within bounds. Simulation shows locked rules.
- AC: Out-of-bounds values are rejected server-side with a message.

### SES-03 Quiz player · L
- Stories: S-04, S-05, S-09, S-10 · Depends on: SES-01
- Scope: `/sesi/{attemptId}`: one question per screen, an Alpine component holding the state, radio buttons for SingleChoice and TrueFalse, checkboxes for MultiSelect, previous/next buttons, a question-number grid showing answered, flagged and current, a flag toggle, a countdown timer (display only, D-09) and "n soal tersisa". Thumb-friendly, with tap targets ≥ 44 px. The page JS budget (excluding vendored libs) is ≤ 15 KB.
- AC: The player works at 360×640 without horizontal scroll. Keyboard navigation works. The guest token or the owner is checked on every request.
- Tests: an E2E test that answers 3 questions, flags 1, navigates back and forth and checks the grid state.

### SES-04 Autosave and resume · M
- Stories: S-06 · Depends on: SES-03
- Scope: Each answer or flag change → htmx/fetch POST `/sesi/{id}/jawab` (idempotent upsert of AttemptItem and AttemptItemOption, sets AnsweredAt, TimeSpentSeconds and LastSavedAt). Reopening the attempt restores answers, flags and position. An "In progress" banner on the exam page links to resume.
- AC: Closing the tab mid-session and reopening it restores everything. A save after the deadline returns 409 and the client shows that the session has ended.

### SES-05 Practice-mode feedback and hints · S
- Stories: R-01 · Depends on: SES-04
- Scope: In Practice with `ShowAnswers = AfterEach`, after answering, a "Periksa" button returns a partial with correct/incorrect, the correct options, the explanation and the key fact. Hint button when enabled (sets HintUsed). Simulation never shows these.
- AC: The answer locks after it is checked. HintUsed is recorded.

### SES-06 Submit, scoring and timeout · M
- Stories: S-07 · Depends on: SES-04
- Scope: Domain `Scoring` (D-02). `SubmitAttempt` sets IsCorrect per item, CorrectCount, TotalCount, ScorePercent, Passed (pass mark), TimeSpentSeconds and Status Submitted. The client auto-submits at 0. On the server, any access past the deadline auto-submits, and a Hangfire job every 5 minutes sweeps expired InProgress attempts (Status Expired, scored).
- AC: Submitting twice is idempotent. Unanswered questions count as wrong.
- Tests: unit tests for scoring per type; an integration test for deadline auto-submit using the test clock.

### SES-07 Result page and trial CTA · S
- Stories: R-03, G-04 · Depends on: SES-06
- Scope: Result page with the score, pass/fail, time and a per-topic table (correct/total, %). A trial attempt shows "Beli ujian ini" and "Berlangganan" buttons with prices (to checkout, or to sign-in then checkout for guests).
- AC: The topic breakdown sums to the total. The trial CTA appears only for Source Trial.

### SES-08 Answer review · S
- Stories: R-02, G-03, M-03 · Depends on: SES-06
- Scope: Review all items after submit (unless `ShowAnswers = Never`): the selected options, the correct options, the explanation and the key fact. Filter to all, wrong or flagged.
- AC: The review uses the stored option order. Retired questions still render.

## E6 Learning loop (M2)

### SES-09 Retake wrong answers · S
- Stories: R-04 · Depends on: SES-08
- Scope: "Ulangi yang salah" on result and review pages starts a new attempt with Source WrongOnly containing only incorrect questions from that attempt (entitlement checked again; a trial stays within trial scope).
- AC: The new attempt contains exactly the wrong questions.

### SES-10 Session history · S
- Stories: H-01, H-02 · Depends on: SES-06
- Scope: `/learn/riwayat` lists past attempts (date, exam, mode, score, time) with paging, each opening its result and review.
- AC: Only the user's own attempts are listed. In-progress attempts show "Lanjutkan".

### SES-11 Report a question and admin queue · M
- Stories: R-07, ADM-04 · Depends on: SES-08, ADM-T2
- Scope: A "Laporkan soal" button in review and in the practice-mode feedback opens a reason form (signed-in or guest) and creates a ContentReport. Admin queue lists Open reports grouped by question with a count, a link to the editor and actions Fixed or Rejected (ResolvedBy, ResolvedAt).
- AC: Duplicate reports from the same user on the same question are merged. The queue shows newest first.

### MEM-01 Wrong answers become review cards · S
- Stories: M-04 · Depends on: SES-06
- Scope: On submit (not Trial guests), each wrong question upserts a ReviewCard (UserId, QuestionId) with State Learning and DueAt = now.
- AC: No duplicate cards. Correct answers do not create cards.

### MEM-02 Sm2Scheduler (domain) · S
- Stories: M-02 (and the base for M-07) · Depends on: F-04
- Scope: Pure SM-2: given the card state and a rating (Forgot, Hard, Good, Easy) → new IntervalDays, EaseFactor (min 1.3), Repetitions, Lapses, State and DueAt. Memorized when the interval is ≥ 21 days.
- AC/Tests: Table tests for the rating sequences in the SM-2 reference.

### MEM-03 Flashcard mode · M
- Stories: M-01, M-02, M-03, M-05 · Depends on: MEM-01, MEM-02, ENT-02
- Scope: `/learn/kartu`: pick an exam with access (or "Kartu saya"), then a set of 10 cards (due first, then wrong, then new). Each card shows the question, then on flip shows the answer, explanation and key fact. Four rating buttons write a ReviewLog and update the card via Sm2Scheduler. Each card loads as an htmx partial.
- AC: A set ends after 10 cards with a summary. Questions from exams the user can no longer access are excluded.

## E7 Payments and purchasing (M3)

### PAY-01 Payment schema and gateway abstraction · M
- Depends on: F-03
- Scope: Payment, PaymentWebhookEvent (unique Gateway+GatewayEventId), Refund, PromoCode (table only) and SavedPaymentMethod (table only), with the columns, constraints and indexes in `schema.sql` (Gateway `DOKU`). Migration `PAY01_Payments`, which also adds the foreign key from `subscriptions.saved_payment_method_id`. `IPaymentGateway`: CreateCharge(order, channel) → instructions (QR string, VA number, deeplink, expiry); ParseWebhook(request) → event; VerifySignature; Refund. `FakePaymentGateway` with a dev page to simulate "paid".
- AC: The fake gateway can complete a payment end to end in dev. `OrderNumber` is unique and human-readable (`AKS-20261006-XXXX`).

### PAY-02 Plans and prices admin · S
- Stories: ADM-09 · Depends on: ADM-T1, F-07
- Scope: Admin CRUD for PlanPrice per Plan and DurationMonths (1, 3, 6, 12) with PriceIdr and IsActive.
- AC: Only one active price per plan and duration.

### PAY-03 Checkout · M
- Stories: P-01, P-02, P-03, ID-18 · Depends on: PAY-01, PAY-02, CAT-02, AC-02
- Scope: `/checkout?exam={id}` or `?planPrice={id}` shows the summary: what you get, the access end date (computed with D-03 and D-04), the price incl. tax, the refund policy excerpt and a checkbox. Channel list with QRIS first, then VA banks, then e-wallets, filtered by admin channel settings (enabled, minimum amount; stored in a `PaymentChannelSetting` table or config). Requires a confirmed email. Creates Payment (Pending) and ExamPurchase or Subscription (AwaitingPayment); at most one Pending payment per item.
- AC: A disabled channel or one below its minimum is hidden. An unconfirmed user is prompted to confirm the email first.

### PAY-04 DOKU client · M
- Stories: ID-10, ID-11, ID-12, P-04 · Depends on: PAY-01
- Scope: Implement `IPaymentGateway` with DOKU (sandbox in dev; D-10): QRIS, bank transfer VA, e-wallets (deeplink) and credit card, matching `Payment.Method`. Store DOKU's `invoice_number` and `Request-Id` on the payment. Verify the signature of every DOKU HTTP notification as DOKU documents it before trusting it. Credentials (`Doku:ClientId`, `Doku:SecretKey`) from user-secrets/env only. Typed HttpClient with retries. Pick the DOKU product (Checkout or Direct API) in this card and record it as a decision.
- AC: Against the sandbox (manual, behind an env flag), a QRIS and a VA charge return instructions, and a sample notification passes signature verification. Unit tests use recorded JSON fixtures.

### PAY-05 Payment instructions and pending payments · M
- Stories: ID-10, ID-11, ID-12, ID-13 · Depends on: PAY-03, PAY-01
- Scope: `/bayar/{orderNumber}`: QRIS shows a QR image (generated server-side as SVG) on desktop and a "Buka aplikasi" deeplink on mobile; VA shows the number with a copy button, per-bank step-by-step instructions (accordion, `id`/`en`) and a countdown to ExpiresAt. htmx polls the status every 5 s and redirects to success when Paid. `/learn/pembayaran` lists Pending payments with "Lihat instruksi".
- AC: The countdown reaches 0 and shows Expired. Returning later shows the same instructions.

### PAY-06 Webhook processing and fulfillment · L
- Stories: ID-14, ID-19, P-01, P-02 · Depends on: PAY-01, PAY-03
- Scope: POST `/webhooks/doku` stores the raw payload in PaymentWebhookEvent (SignatureValid flag) and returns 200 fast. Processing (in-request, in a transaction, with a unique constraint as the guard) moves Payment to Paid, Expired or Failed. A success notification for a payment already Expired or Canceled sets `IsLatePayment` for admin follow-up (how to fulfil it is an owner decision). On Paid: ExamPurchase Active with AccessExpiresAt (D-04), or Subscription Active with the period (D-03). Duplicate events are no-ops. Invalid signatures are stored but never processed.
- AC: Sending the same event twice gives one fulfillment. Access is available on the next request after Paid.
- Tests: integration tests for paid → access, duplicate event, bad signature and expired.

### PAY-07 Receipt email · S
- Stories: P-05 · Depends on: PAY-06, AC-02
- Scope: On Paid, enqueue a Hangfire job that sends an Indonesian receipt (order number, item, amount, method, paid at, access end) and records a Notification (Email, Sent or Failed).
- AC: The receipt is captured by the fake email sender in the integration test with the fake gateway.

### PAY-08 Expiry jobs and reminders · M
- Stories: P-07, D-06 · Depends on: PAY-06, PAY-07
- Scope: Hangfire recurring jobs. Nightly: mark ExamPurchase and Subscription Expired past their end. Daily 09:00 WIB: email reminders 7 days and 1 day before a package ends (Reminder7dSentAt, Reminder1dSentAt guards) with a renew link. Every 15 minutes: expire Pending payments past ExpiresAt.
- AC: Reminders are sent once each, tested with the test clock.

### PAY-09 Refund requests · L
- Stories: P-10, ADM-30 · Depends on: PAY-06
- Scope: Domain `RefundPolicy` encodes the table in the stories doc: first subscription payment ≤ 7 days; renewal ≤ 48 h and no practice since; single exam ≤ 7 days and < 20% of non-trial questions answered; plus the always-refund reasons; one first-payment refund per account. `/learn/pembayaran/{id}/refund` shows eligibility with the reason before submitting. Eligible requests are auto-approved: call gateway Refund, set Payment Refunded, end access (purchase or subscription Refunded). Refunds follow the `schema.sql` statuses: DOKU refunds straight to the payer app where the method allows (`DirectToPayerApp`), otherwise via a DOKU refund link (`RefundLink`, status AwaitingDetails until the payer fills it in). Others go to Requested for admin review (an admin page to approve or reject with a reason).
- AC: Every policy row has unit tests. Access ends on approval.

### PAY-10 Admin transactions · S
- Stories: ADM-20, ADM-23 · Depends on: PAY-06
- Scope: Admin list of payments filterable by status, method, date and user, with a detail page (payment, webhook events, refunds). "Failed" view with the reason and retry count. CSV export. Payments are never hard-deleted (no delete action; enforced in DbContext).
- AC: Filters combine. The export matches the filters.

## E8 Admin users (M4)

### USR-01 User search · S
- Stories: ADM-14 · Depends on: AC-06
- Scope: Admin user list with search by email or name and filters by role, active flag and subscription status, plus a user detail page (profile, roles, access from ENT-03, attempts count).
- AC: Paging works. Filters combine.

### USR-02 Create user · S
- Stories: ADM-15 · Depends on: USR-01, AC-02
- Scope: Admin creates a user with an email, name and roles. The user receives a "set your password" email.
- AC: The user can set a password from the link and sign in.

### USR-03 Deactivate and reactivate · S
- Stories: ADM-16 · Depends on: USR-01
- Scope: Toggle IsActive and DeactivatedAt and sign the user out everywhere (security stamp). An AuditLog entry is written.
- AC: A deactivated user is signed out on the next request and cannot sign in. Records stay intact.

## E9 Launch readiness (M4)

### OPS-01 Performance budget · M
- Stories: ID-05, S-10 · Depends on: SES-03, CAT-01
- Scope: Response compression (Brotli), static file caching with fingerprinting, Tailwind purge, no web fonts (system font stack), lazy images. Add a Playwright check for transferred bytes on the catalog and player.
- AC: First load of the catalog and player is ≤ 150 KB transferred (gzip/br). Lighthouse mobile performance is ≥ 90 locally.

### OPS-02 Production deployment · M
- Depends on: F-08, PAY-04
- Scope (D-16): `Dockerfile` using the `noble-chiseled-extra` ASP.NET runtime image (the plain chiseled image has no ICU, which breaks `id-ID` formatting). GitHub Actions on `main`: build, test, push the image to GHCR, build an EF migrations bundle, then SSH to the VPS to run the bundle against Neon and `docker compose pull && up -d`. `deploy/` holds a cloud-init file for the Hetzner VPS (non-root deploy user, SSH keys only, ufw allowing 22/80/443, unattended-upgrades, Docker), `compose.yaml` (app + Caddy) and a `Caddyfile` (automatic HTTPS, forwarded headers). Secrets (Neon, DOKU, Google, SMTP) come from a root-only `.env` on the server, filled from GitHub secrets; nothing is committed. Protect the Hangfire dashboard, add structured logging, and document a rollback (redeploy the previous image tag).
- AC: The image builds in CI (nothing is built locally). A fresh VPS created with the cloud-init file serves the app over HTTPS after one deploy run. The container starts with only env vars, `/health` reports DB status, and a rollback to the previous tag works.

---

# Release 2

## E10 Own content (M5)

### OWN-01 Plan limits admin · S
- Stories: ADM-29 · Depends on: PAY-02
- Scope: Admin edits Plan.MaxOwnQuestions, MaxOwnExams and MaxImportRows (fair-use cap for paid plans; 50 and 1 for Free).
- AC: Changes apply without a deploy (no caching beyond the request).

### OWN-02 AllowancePolicy · M
- Stories: C-03, C-04, C-12 · Depends on: OWN-01, ENT-02
- Scope: Domain `AllowancePolicy` → { MaxQuestions, MaxExams, UsedQuestions, UsedExams, CanCreate, CanEdit } using D-11 and D-07 (read-only when no entitlement covers the current usage).
- AC/Tests: Unit tests for free, exam buyer, subscriber, expired subscriber over the cap (read-only) and admin (unlimited).

### OWN-03 Own question editor · M
- Stories: C-01, C-05 · Depends on: OWN-02, ADM-T2
- Scope: Reuse the question form component for learners at `/learn/soal-saya` with OwnerUserId set and topics from a personal "Umum" topic or an existing subject topic. Enforce the allowance on create and edit. Delete is blocked when the question is used in a submitted attempt (retire instead).
- AC: The 51st question for a free user is rejected with an upgrade CTA.

### OWN-04 Own exam · M
- Stories: C-02 · Depends on: OWN-03
- Scope: A learner creates an exam (`OwnerKind = Learner`) from own questions only, with a title, a description and a personal rule preset (OwnerUserId). The status toggles between Draft and Published, where published only means "ready to share". Entitlement rule 2 (Owner) lets the learner practise it with the session engine.
- AC: The free user's 2nd exam is rejected. The owner can start practice on their own exam.

### OWN-05 Allowance widget · S
- Stories: C-03 · Depends on: OWN-02
- Scope: A usage bar ("23/50 soal, 1/1 ujian") on the own-content pages and the profile, with an upgrade link to `/harga`.
- AC: The numbers match OWN-02.

### OWN-06 Private mixed practice set · M
- Stories: C-08 · Depends on: OWN-04, SES-01
- Scope: Buyers and subscribers pick own questions plus questions from exams they have Full access to (by topic or exam) → an Attempt with Source CustomMix and ExamId null. Re-check entitlement for every app question.
- AC: App questions without Full access are excluded. A CustomMix attempt can never be shared.

## E11 Sharing (M6), with ADM-27 shipped before the feature flag turns on

### SHR-01 Share, stop sharing and reset link · S
- Stories: C-06, C-07 · Depends on: OWN-04
- Scope: The owner toggles IsShared. `ShareToken` is 22 chars URL-safe random. Reset generates a new token (the old link becomes 404). Sharing is only allowed when every question belongs to the owner. Everything is behind the feature flag `Features:Sharing` (default off).
- AC: A reset link invalidates the old one. An exam containing a non-owned question cannot be shared.

### SHR-02 Report shared exam and admin takedown · M
- Stories: ADM-27 · Depends on: SHR-01, SES-11
- Scope: A "Laporkan ujian ini" form on the shared exam landing page creates a ContentReport with ExamId. The admin queue gains an exam tab with a Take down action (Exam.Status TakenDown, report Status TakenDown) and Restore.
- AC: A taken-down exam's link shows "Ujian tidak tersedia" and attempts cannot start (entitlement rule 3).

### SHR-03 Guest takes a shared exam · M
- Stories: G-07 · Depends on: SHR-02, SES-03
- Scope: `/s/{token}` is a landing page with the title, question count and owner display name, a name input and the notice "Nama dan skor Anda akan terlihat oleh pembuat ujian". Starting creates an Attempt with Source Shared, GuestName and GuestToken. Explanations are shown after submit (per the decision in the doc). Signed-in users skip the name.
- AC: The flag off gives 404. Without a name, the attempt cannot start.

### SHR-04 Shared result and register invitation · S
- Stories: G-08 · Depends on: SHR-03
- Scope: The result and review pages for a shared attempt add a register CTA. After registering in the same browser, the guest attempt is linked to the new user via GuestToken (also covers trial attempts, part of G-09).
- AC: After registration, the attempt appears in the user's history.

### SHR-05 Enable sharing · S
- Depends on: SHR-01..04
- Scope: Turn `Features:Sharing` on by default. Add an E2E test: owner shares → guest takes → admin takes down → link blocked.
- AC: The E2E test passes.

## E12 Spaced repetition (M7)

### SRS-01 Own questions in review · S
- Stories: M-06 · Depends on: MEM-03, OWN-03
- Scope: Own questions get ReviewCards when answered wrong and appear in "Kartu saya" in flashcard mode.
- AC: Own questions appear in sets alongside app questions.

### SRS-02 Scheduled reviews · S
- Stories: M-07 · Depends on: MEM-02, MEM-03
- Scope: Flashcard sets prioritise cards with DueAt ≤ now ordered by DueAt, then new cards. Each card shows the next due date after rating.
- AC: A card rated Easy is not due again until its interval passes (test clock).

### SRS-03 Daily due list · S
- Stories: M-08 · Depends on: SRS-02
- Scope: A `/learn/hari-ini` dashboard shows the due count per exam and in total, with "Mulai 10 kartu" per exam and for all. It is the home page for signed-in learners.
- AC: Counts match the due cards. Exams without access are excluded.

## E13 Exam positioning (M8): LatihanSoal for CPNS, BUMN, Kedinasan and UKMPPD

### BRD-01 Rebrand to LatihanSoal and blue redesign · M
- Stories: owner request (2026-10-21) · Depends on: SHR-05
- Scope: Rename everything users and operators see to LatihanSoal (`LatihanSoal.id`): name, logo, page titles, emails, sender, order numbers (`LSI-`), docs. Code namespaces stay `Akusiap.*`. Blue study-app design system (brand scale, yellow accent, cards, chips) and a redesigned layout, home page (exam categories CPNS/BUMN/Kedinasan/Kedokteran, features, "Segera hadir"), catalog, exam page, learn hub, Today and account pages.
- AC: No user-visible "Akusiap" remains. Every page fits 360 px. The catalog and player stay within the 150 KB first-load budget (OPS-01). The home page only promises built features; drills and CAT tryouts are marked "Segera hadir".

### DRL-01 Daily drill with points and streak · M
- Stories: owner request (gamified daily drills) · Depends on: SRS-03, SES-06
- Scope: `/learn/hari-ini` gets "Latihan harian": a 10-question set built from the learner's accessible exams (due review cards first, then new questions), a daily goal (one completed drill), XP per correct answer and a streak counted in Jakarta days. Needs a new table for drill progress (streak, XP, last drill date) in this card's migration; record it as an addition to `schema.sql` (like D-68).
- AC: The streak grows by one for each Jakarta day with a completed drill and resets after a missed day (test clock). XP equals correct answers. Questions without full access are excluded.

### DRL-02 Badges and weekly leaderboard · M
- Stories: owner request · Depends on: DRL-01
- Scope: Milestone badges (streak 7/30, XP levels) on the profile, and a weekly XP leaderboard per exam category.
- Needs from the owner: whether learners appear by display name on a public leaderboard (privacy), or only see their own rank.
- AC: Badges are awarded once. The leaderboard resets every Monday 00:00 WIB.

### TRY-01 CAT tryout screen · M
- Stories: owner request (CAT tryouts that mimic the official system) · Depends on: SES-06, SES-07
- Scope: A "Tryout CAT" mode for Simulation attempts: a full-screen layout like the official CAT screen with the numbered question grid always visible, a countdown in the header, "Ragu-ragu" marks (flag), no feedback or explanations until the end, a final confirmation, and auto-submit at zero. Exams opt in with a rule preset setting.
- AC: During a tryout no answer feedback is shown. The grid shows answered, unanswered and ragu-ragu states. Time running out submits the attempt (test clock).

### TRY-02 Subtest scoring (TWK/TIU/TKP) · L
- Stories: owner request · Depends on: TRY-01
- Scope: Score tryouts per subtest with a passing grade per subtest (for example SKD: TWK, TIU, TKP) and show the breakdown on the result page.
- Needs from the owner: the scoring rules and passing grades to use, and approval for the schema change, because TKP gives 1–5 points per option and `schema.sql` only has right/wrong options. Scoring must not be guessed.
- AC: Each subtest score and pass/fail matches the agreed rules; overall pass needs every subtest to pass.

### GAM-01 Leaderboards, teacher classes and learning journey · L
- Stories: C-09, T-09 to T-14, M-02 (motivation), owner request (2026-10-22: compete on score and time; help teachers; gamify each learner's journey) · Depends on: SES-07, OWN-04, MEM-03
- Scope:
  - Public leaderboards for catalog exams and class leaderboards: first finished Simulation per person, by score then time.
  - The viewer's rank on the result page, and a name opt-out in the profile.
  - The owner's participants list for their own exams.
  - Teacher classes: invite code and link, assign an exam with a window and locked rules, students start it as a Simulation, results per student, remove a student. Entitlement rule 4b gives class members access.
  - Learning journey: XP, levels, streak, milestone badges and rank badges, on `/learn/perjalanan` and on the Today page.
- AC:
  - A retake never changes a person's rank.
  - Equal scores rank by time.
  - Opted-out names are masked for others.
  - A removed student loses access and can't rejoin.
  - A badge is awarded only once.
  - The catalog and player stay within the OPS-01 budget.
