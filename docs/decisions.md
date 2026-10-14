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
