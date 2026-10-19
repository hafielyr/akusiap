# Kanban board

Task details are in `backlog.md`; rules for agents are in `AGENTS.md`. Cards in **Ready** and **Backlog** are in priority order, top first.
Card line format: `- [ID] Title · milestone · deps: … · (owner, date)` (owner and date only while In Progress, Review or Blocked).

WIP limit: In Progress ≤ 3 cards (parallel agents), one card per agent.

## Ready
<!-- Every dependency is in Review or Done. Agents take the top card. -->
- [ADM-T5] Rule presets · M1 · deps: ADM-T1
- [ADM-T6] Publish and unpublish · M1 · deps: ADM-T4
- [ENT-02] Entitlement query and guard · M1 · deps: ENT-01, AC-01
- [AC-02] Email confirmation and password reset · M1 · deps: AC-01
- [AC-04] Profile, password and role · M1 · deps: AC-01
- [AC-05] Legal pages · M1 · deps: F-06, AC-01
- [AC-03] Google sign-in · M1 · deps: AC-01
- [MEM-02] Sm2Scheduler (domain) · M2 · deps: F-04
- [ADM-T7] Bulk question import · M2 · deps: ADM-T2
- [PAY-01] Payment schema and gateway abstraction · M3 · deps: F-03
- [PAY-02] Plans and prices admin · M3 · deps: ADM-T1, F-07
- [USR-01] User search · M4 · deps: AC-06

## In Progress

## Review
- [ADM-T4] Exam question picker and trial set · M1 · deps: ADM-T3 · (claude-opus-5.5, 2026-10-19)

## Blocked
<!-- Add the reason and what is needed from the owner. -->

## Done
- [F-01] Solution skeleton · M1 · deps: none
- [F-06] Localization and Rupiah · M1 · deps: F-01
- [F-02] Database and dev infrastructure · M1 · deps: F-01
- [F-03] Content and access schema · M1 · deps: F-02
- [F-04] Session and memorization schema · M1 · deps: F-03
- [F-05] Test infrastructure · M1 · deps: F-03
- [F-07] Seed data · M1 · deps: F-03
- [AC-01] Register and sign in with email · M1 · deps: F-03, F-06
- [ENT-01] EntitlementPolicy (domain) · M1 · deps: F-03
- [F-08] CI pipeline · M1 · deps: F-05
- [AC-06] Authorization policies · M1 · deps: AC-01
- [ADM-T1] Admin shell, subjects and topics · M1 · deps: AC-06, F-03
- [ADM-T2] Question editor · M1 · deps: ADM-T1
- [ADM-T3] Exam editor · M1 · deps: ADM-T2

## Backlog

### M1 Access to owner content (Release 1, priority)
- [CAT-01] Exam catalog · M1 · deps: ADM-T6, F-06
- [CAT-02] Exam detail with access CTA · M1 · deps: CAT-01, ENT-02
- [ENT-03] Admin grants, extends and ends access · M1 · deps: ENT-02, AC-06
- [SES-01] Start attempt service · M1 · deps: ENT-02, F-04
- [SES-02] Session setup page · M1 · deps: SES-01
- [SES-03] Quiz player · M1 · deps: SES-01
- [SES-04] Autosave and resume · M1 · deps: SES-03
- [SES-06] Submit, scoring and timeout · M1 · deps: SES-04
- [SES-05] Practice-mode feedback and hints · M1 · deps: SES-04
- [SES-07] Result page and trial CTA · M1 · deps: SES-06
- [SES-08] Answer review · M1 · deps: SES-06
- [ENT-04] My access page · M1 · deps: ENT-02

**M1 exit check**: An admin creates and publishes a paid exam and a free exam. A guest finds the paid exam in the catalog, takes the 30-question trial with explanations and sees the buy CTA. A registered user takes the free exam. The admin grants the paid exam to that user, who then takes the full exam in Simulation mode; it auto-submits at the time limit.

### M2 Learning loop (Release 1)
- [SES-09] Retake wrong answers · M2 · deps: SES-08
- [SES-10] Session history · M2 · deps: SES-06
- [MEM-01] Wrong answers become review cards · M2 · deps: SES-06
- [MEM-03] Flashcard mode · M2 · deps: MEM-01, MEM-02, ENT-02
- [SES-11] Report a question and admin queue · M2 · deps: SES-08, ADM-T2

### M3 Payments (Release 1)
- [CAT-03] Pricing comparison · M3 · deps: PAY-02
- [PAY-03] Checkout · M3 · deps: PAY-01, PAY-02, CAT-02, AC-02
- [PAY-06] Webhook processing and fulfillment · M3 · deps: PAY-01, PAY-03
- [PAY-05] Payment instructions and pending payments · M3 · deps: PAY-03, PAY-01
- [PAY-04] DOKU client · M3 · deps: PAY-01
- [PAY-07] Receipt email · M3 · deps: PAY-06, AC-02
- [PAY-08] Expiry jobs and reminders · M3 · deps: PAY-06, PAY-07
- [PAY-09] Refund requests · M3 · deps: PAY-06
- [PAY-10] Admin transactions · M3 · deps: PAY-06

### M4 Admin ops and launch (Release 1)
- [USR-02] Create user · M4 · deps: USR-01, AC-02
- [USR-03] Deactivate and reactivate · M4 · deps: USR-01
- [OPS-01] Performance budget · M4 · deps: SES-03, CAT-01
- [OPS-02] Production deployment · M4 · deps: F-08, PAY-04

**Release 1 exit check**: In the DOKU sandbox, a guest completes trial → register → QRIS payment → full exam → review → flashcards. The receipt email arrives. A refund within 7 days is auto-approved and access ends.

### M5 Own content (Release 2)
- [OWN-01] Plan limits admin · M5 · deps: PAY-02
- [OWN-02] AllowancePolicy · M5 · deps: OWN-01, ENT-02
- [OWN-03] Own question editor · M5 · deps: OWN-02, ADM-T2
- [OWN-04] Own exam · M5 · deps: OWN-03
- [OWN-05] Allowance widget · M5 · deps: OWN-02
- [OWN-06] Private mixed practice set · M5 · deps: OWN-04, SES-01

### M6 Sharing (Release 2)
- [SHR-01] Share, stop sharing and reset link · M6 · deps: OWN-04
- [SHR-02] Report shared exam and admin takedown · M6 · deps: SHR-01, SES-11
- [SHR-03] Guest takes a shared exam · M6 · deps: SHR-02, SES-03
- [SHR-04] Shared result and register invitation · M6 · deps: SHR-03
- [SHR-05] Enable sharing · M6 · deps: SHR-01, SHR-02, SHR-03, SHR-04

### M7 Spaced repetition (Release 2)
- [SRS-01] Own questions in review · M7 · deps: MEM-03, OWN-03
- [SRS-02] Scheduled reviews · M7 · deps: MEM-02, MEM-03
- [SRS-03] Daily due list · M7 · deps: SRS-02

**Release 2 exit check**: A free learner writes 50 questions, builds 1 exam and shares it. A guest takes it by entering a name, sees the explanations and is invited to register. The admin takes the exam down and the link stops working. The daily due list shows wrong answers scheduled by SM-2.
