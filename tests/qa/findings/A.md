# QA findings A — S01 morning, S07 leave, S09 evening

Scope: docs/PROMPT3.md §2 items 1, 7, 9 (API level). Scenarios: `tests/qa/scenarios/01-morning.mjs`, `07-leave.mjs`, `09-evening.mjs`.
Environment: `bash tests/qa/qa-api.sh 5091 karnama_qa_a`, QA clock set to Monday 26 Oct 2026 (Tehran), demo data + 81-person crowd.
Runs: S01+S07+S09 together on 3 fresh databases (same failures each time, except 2.1.t, which is a race). S07 and S09 were also run alone on fresh databases.
Last report: `tests/qa/reports/run-2026-10-26T10-33-14-073Z.json`.

**Totals (last full run):** 49/59 steps pass. 10 fail, and all 10 are product issues; none is a wrong scenario assumption. 2.1.t is a race, so it fails in about 2 of 3 runs. The final `hours-match-events` invariant is RED.

## Notes on the setup (assumptions checked, not bugs)
- The QA crowd needs no extra schedule. The seeded agency default schedule (`UserId = null`: Sat–Wed 09:00–17:00, Thu 09:00–13:00, grace 10 min, break 60) applies to everyone, so lateness is computed without setting anything up (2.1.a).
- Demo clock-in rule is `OfficeNetwork | RotatingQr | Geofence` (AttendanceSeed.cs:28). The seeded field rule `Geofence & Selfie` has no roles or users attached, so field crowd members also use the default rule. QA field people share the «تدوینگر» role, so a role-based field policy could not target them anyway.
- JWTs are validated against the QA clock. A token issued before a clock jump is rejected (401), so the scenarios sign in again after every `qa.setClock`.
- The QA clock keeps ticking after `set`. Minute values are truncated, so "16:00" early leave can read 59. The scenarios accept ±1 minute.
- Validation errors are **422** (not 400), and the scenarios accept both.
- PROMPT3 expects a home clock-in to be «نیازمند بررسی». Actual: it is accepted, yellow, `needsReview = true`, and every method failed. This matches the expectation (2.1.h).
- A reused or forged kiosk QR is **flagged, not refused**: the shift opens, red, needsReview, «QR تکراری» / «QR جعلی». This follows the design rule "a failed verification never blocks" (AttendanceService.cs header). The `kiosk-qr-used-once` invariant stays green (2.1.d/e).

## (a) Coverage

| id | what | result | evidence |
|---|---|---|---|
| 2.1.a | default schedule applies to the crowd; today is a workday | pass | setup.schedules[userId=null], /me.schedule 09:00 |
| 2.1.b | kiosk QR `k1.step.n.mac`; worker → 403; kiosk from home IP → 403 | pass | |
| 2.1.c | 10 office staff clock in at 08:55 with a fresh QR: green, not late, RotatingQr passed; kiosk code changes after use | pass | |
| 2.1.d | QR reused by a second person: red + needsReview; kiosk-qr-used-once still OK | pass | flagged, not refused |
| 2.1.e | forged QR (bad MAC) from home: red + review | pass | |
| 2.1.f | replay with the same idempotency key: same shift, 1 shift today | pass | |
| 2.1.j | field worker with a real GPS fix inside 150 m from mobile data: green via Geofence | pass | |
| 2.1.g | second clock-in with a new key → 409 | pass | `attendance.alreadyIn` |
| 2.1.h | from home, no QR/GPS: yellow + needsReview | pass | |
| 2.1.i | mock GPS: red + needsReview, reason «مکان جعلی» | pass | |
| 2.1.k | absence job at 09:20: late and absent people → `detected`; present people none; second run = 0 | pass | |
| 2.1.k2 | absent worker notified **and admin notified** (PROMPT §6.7.2) | **fail** | Bug B2 |
| 2.1.l | late at 09:25 / 09:40 → lateMinutes 25 / 40; absence → `becameLate` with the same minutes | pass | |
| 2.1.m | absent A: short reason → 422; reason with 09–11 → `reasonSubmitted` → manager excuses | pass | |
| 2.1.n | absent B: reason → manager rejects → `unexcused` | pass | |
| 2.1.o | worker decides absence → 403; someone else's reason → 404; decide a became-late absence → 409 | pass | |
| 2.1.p | live board: in / absent / late minutes; review queue holds reuse, home, mock but not the green ones | pass | |
| 2.1.q | manager accepts the home shift, rejects the mock one (stays red); worker review → 403 | pass | |
| 2.1.s | monthly report counts a late arrival once (40) | **fail** | Bug B1 (got 80) |
| 2.1.t | 5 parallel clock-ins with different keys: one 200, rest 409 | **flaky fail** | Bug B4 (some are 500) |
| 2.1.t2 | 4 parallel sends with the same key: no 5xx, one shift | **fail** | Bug B4 (200,500,500,500) |
| 2.1.u | the kiosk tablet account is not flagged absent | **fail** | Bug B6 |
| 2.1.v | approved clock-in correction 09:25 → 08:58 clears lateness | **fail** | Bug B5 (stays 25) |
| 2.1.r | invariants one-open-shift, kiosk-qr-used-once | pass | |
| 2.7.a | leave types listed: annual 26 d, sick needs a document, mission | pass | |
| 2.7.b | balance before: 26 / 0 / 26 | pass | |
| 2.7.c | 2-day annual request → pending; manager sees it and gets `leave.request`; pending not counted | pass | |
| 2.7.d | approve → worker gets `leave.decided`; balance 2 used / 24 left | pass | |
| 2.7.e | hourly 10–12 → rejected with a note; balance unchanged | pass | |
| 2.7.f | approved 2 h hourly leave = 120/420 = 0.29 day | pass | |
| 2.7.g | requester cancels pending; cancel twice → 409; other worker → 403; decide a cancelled one → 409 | pass | |
| 2.7.h | admin registers a mission (forUserId) → approved at once, byManager, worker notified, no annual days used | pass | |
| 2.7.i | overlapping request → 409 | pass | |
| 2.7.j | short reason / end < start / hourly > 12 h → 422 | pass | |
| 2.7.k | leave wholly in the past: accepted as **pending** (not refused) | pass (documented) | Observation O1 |
| 2.7.l | worker approve → 403; worker files for a colleague → 403; worker lists only own | pass | |
| 2.7.m | manager approves own → 403; owner approves; decide twice → 409 | pass | |
| 2.7.n | team calendar: day+2/+3 `leave`, day+4 not; tomorrow `hourlyLeave` | pass | |
| 2.7.q | sick leave without a document → pending, `missingDocument = true` | pass | |
| 2.7.o | owner cancels a future approved leave → balance back to 26 | pass | |
| 2.7.p | absence job (tomorrow 09:30) skips the person on an approved mission; control person flagged | pass | |
| 2.7.r | leave registered after the absence was detected stops it becoming unexcused | **fail** | Bug B3 |
| 2.9.a | 4 workers clock in at 08:50 from office network: green | pass | |
| 2.9.b | timer on an assigned card; someone else's card → 404 | pass | |
| 2.9.c | break start stops timer; break twice → 409; timer on break → 409; live = `break`; end → 30 min; end again → 409; break without shift → 409 | pass | |
| 2.9.d | early leave 16:00 with note → earlyLeaveMinutes 60 (±1), reason stored | pass | |
| 2.9.e | clock-out 17:05 stops the timer; worked today 465 min | pass | |
| 2.9.f | clock-out replay with the same key is a no-op; new clock-out with no shift → 409 | pass | |
| 2.9.g | forgotten: still open at 20:00; closed by the job at 21:30, at 17:00, needsReview | pass | |
| 2.9.h | forgotten clock-out warns worker + admin (PROMPT §6.6) | **fail** | Bug B2 |
| 2.9.i | invariant hours-match-events after auto-close | **fail** | Bug B7 |
| 2.9.j | correction validation: none / out < in / future / short reason → 422 | pass | |
| 2.9.k | correction 18:30 → pending; worker sees own only; manager sees pending; worker or requester decide → 403 | pass | |
| 2.9.l | approve → clock-out 18:30, review cleared, worked 580; decide twice → 409 | pass | |
| 2.9.m | rejected correction leaves the shift at 16:00 | pass | |
| 2.9.n | correction for a whole missed day (yesterday) creates a 480-min shift | pass | (but see B7) |
| 2.9.q | clock-in-only correction for a past day while clocked in | **fail** | Bug B4 (500) |
| 2.9.o | invariants after the evening | **fail** | Bug B7 |
| 2.9.p | live board: evening crew `out` | pass | |

## (b) Product bugs / mismatches

### B1 — Monthly report double-counts lateness for anyone first detected absent (High: payroll)
- **Repro (2.1.s).** The 09:20 absence job flags a worker. The worker clocks in at 09:40, so `shift.lateMinutes = 40` and `absence.status = becameLate, lateMinutes = 40`. Then `GET /api/v1/leave/report?from=2026-10-26&to=2026-10-26` returns `lateMinutes: 80` and `lateCount: 1`.
- **Location.** `src/Karnama.Application/Attendance/LeaveService.cs:288`: `sh.Sum(s => s.LateMinutes) + ab.Sum(a => a.LateMinutes)`. When an absence turns into lateness, the same minutes are already on the shift (AttendanceService.cs ClockInAsync computes `late` and `BecomeLate` stores it again). The Excel export (`ReportExcelAsync`) has the same problem. Any job interval shorter than the late arrival hits this, which is the normal case (the job runs every 5 minutes).
- **Fix idea.** Count only shift lateness. Use absence lateness only when no shift exists for that day.

### B2 — Required notifications are missing (Medium: PROMPT.md §6.6 and §6.7.2)
- **Absence → admin (2.1.k2).** §6.7.2 says «اعلان به کارگر ... و به ادمین». `DetectAsync` notifies only `[uid]`. Location: `LeaveService.cs:219`. After `POST /api/v1/qa/jobs/absences`, the manager inbox has no absence notice naming the worker. The manager only gets their own «ورود امروز ثبت نشد».
- **Forgotten clock-out (2.9.h).** §6.6 says «اگر شیفت تا پایان برنامه + X ساعت باز ماند، سیستم به کارگر و ادمین هشدار می‌دهد؛ در پایان روز ... بسته می‌شود». `CloseForgottenAsync` (`AttendanceAdminService.cs:274-296`) skips the warning step. It closes silently at end + X and sends no notification to anyone. After the 21:30 run, the worker's inbox holds only `absence.detected` entries.

### B3 — Leave registered after an absence was detected does not resolve the absence (Medium: payroll)
- **Repro (2.7.r).** At 10:00 the job flags m2 (`detected`). The manager `POST /api/v1/leave/requests {leaveTypeId: sick, from: today 00:00, to: tomorrow 00:00, forUserId: m2}` and gets `approved`. 48 h later the job runs. The report for that day shows `paidLeaveMinutes > 0` **and** `unexcusedAbsences: 1`.
- **Location.** `LeaveService.RequestAsync` (`LeaveService.cs:65+`) and `DecideAsync` leave existing absences alone. `DetectAsync` expiry (`LeaveService.cs:225-226`) does not check approved leave. §6.7.5 («ادمین برای کارگر مرخصی ثبت کند (مثلاً تلفنی خبر داده)») is exactly this case.
- **Fix idea.** When a leave is approved, mark covered `detected`/`reasonSubmitted` absences excused, or skip them when they expire.

### B4 — A unique-index race returns HTTP 500 instead of 409 or a replay (Medium)
- **Double tap with different keys (2.1.t, race).** 5 parallel `POST /api/v1/attendance/clock-in` with different keys gave statuses such as `200,500,500,500,500` or `200,409,409,409,500`. The DB index `IX_shifts_one_open` (AttendanceConfigurations.cs:40) keeps the data correct (one shift), but the losing requests get a `23505` that surfaces as 500.
- **Offline-queue retry with the same key (2.1.t2).** 4 parallel sends with one idempotency key gave `200,500,500,500` instead of 200 replays. `DuplicateAsync` (AttendanceService.cs:79, 109, 137) is check-then-insert with no catch for the `(UserId, IdempotencyKey)` unique index (AttendanceConfigurations.cs:53) or `IX_shifts_one_open`.
- **Correction (2.9.q).** The worker is clocked in tonight and asks for a clock-in-only correction for Saturday (`{day: 2026-10-24, clockIn: 09:00, reason}`). Manager `POST /api/v1/attendance/corrections/{id}/decide {approve:true}` returns 500 `errors.unexpected`. `AttendanceAdminService.DecideAsync` (lines 126-131) creates a second **open** shift via `Shift.Open(...)` and `Correct(inAt, null)`, which hits `IX_shifts_one_open`. The correction stays pending with no message for the manager. A clock-in-only correction for a past day should create a shift that needs a clock-out (review), not an open one. When the worker is not clocked in, it leaves an open shift dated in the past, and that blocks today's clock-in (409) until the forgotten job closes it.
- **Location.** `src/Karnama.Api/Infrastructure/GlobalExceptionHandler.cs:16-39` maps only `ConcurrencyConflictException`. `DbUpdateException` with SqlState 23505 falls through to 500.

### B5 — An approved clock-in correction does not recompute lateness (Medium)
- **Repro (2.1.v).** A worker clocked in at 09:25 (`lateMinutes 25`) asks `POST /api/v1/attendance/corrections {day, clockIn: 08:58, reason}`, and the manager approves. Result: `clockInAt` = 08:58 but `lateMinutes` stays 25 (the report too, plus the B1 doubling from the became-late absence).
- **Location.** `src/Karnama.Domain/Attendance/Shift.cs:106` `Correct()` changes `ClockInAt` and leaves `LateMinutes` as it was (and `EarlyLeaveMinutes` likewise for a corrected clock-out). The schedule-based recompute only happens in `AttendanceService.ClockInAsync/ClockOutAsync`.

### B6 — Non-attendance accounts (the kiosk tablet) are flagged absent and notified every day (Low)
- **Repro (2.1.u).** After the 09:20 absence job, `GET /api/v1/leave/absences` includes the «کیوسک دفتر (QA)» account. Its role holds only `attendance.kiosk`, not `attendance.self`, so it cannot even submit a reason. The absence auto-expires to `unexcused` and appears in the monthly report.
- **Location.** `LeaveService.cs:208` selects every active `Staff` user. Filtering to users holding `attendance.self` (or having a schedule) would fix it. Owners are flagged as well, which may or may not be wanted.

### B7 — Shifts closed by the system or created by a correction have no clock-in/clock-out events, so invariant `hours-match-events` is RED (Medium: append-only audit)
- **Repro (2.9.i/2.9.o, final invariants).** Every shift auto-closed by `forgotten-shifts` lacks a `ClockOut` event. A shift created from an approved correction for a missed day has only a `Correction` event and no `ClockIn`/`ClockOut`. Standalone S09 gives 2 violations; the full run gives 21 (S01's open shifts were auto-closed at night).
- **Location.** `AttendanceAdminService.CloseForgottenAsync` (`AttendanceAdminService.cs:288`, `shift.AutoClose(end)`, no `AttendanceEvents.Add`). `DecideAsync` (`AttendanceAdminService.cs:126-135`) adds only a `Correction` event. PROMPT §6.6 wants the event log to be the evidence for every change. Either append system events (ClockOut, actor null, note "auto-closed") or widen the invariant to accept a `Correction` event. That is a product decision, but as written the code and the invariant (QaService.cs, "hours-match-events") disagree.

### Observations (not counted as bugs, decide with product)
- **O1 (2.7.k).** A self-request for leave wholly in the past (`from = today-7`) is accepted as `pending`, and nothing refuses back-dated requests (`LeaveRequest.Create`). §6.7.1 calls it «درخواست از قبل», and after-the-fact absences have their own reason flow (§6.7.3). Decide whether past requests should be refused or sent to the absence flow.
- **O2.** A daily leave counts calendar days (`LeaveService.LeaveMinutes`). A Wed→Sat leave uses 3 days although Friday is off. Official holidays from the `Holiday` table inside the range are counted too. Confirm the rule with the customer.
- **O3.** The kiosk code is single use per displayed value. Two people scanning the same on-screen code a second apart makes the second one red «QR تکراری» (a fraud flag on an honest worker). Consider a softer flag, or refreshing the kiosk display on the `kiosk_code_uses` change feed.

## After fixes (B1–B7)

Two fresh runs (`bash tests/qa/qa-api.sh 5091 karnama_qa_a`, Release build, then `node tests/qa/run.mjs S01 S07 S09`): **59/59 steps pass both times, every invariant green** (including `hours-match-events`), and the API log has no 500. No scenario needed changing. Last report: `tests/qa/reports/run-2026-10-26T11-02-58-388Z.json`.

| bug | fix | where |
|---|---|---|
| B1 | The report counts absence lateness only for a day with no shift. A became-late absence carries the same minutes as that day's shift. The Excel export reads the same rows. | `LeaveService.ReportAsync` |
| B2 | Absence detection sends the approvers (`attendance.approve`) one `absence.team` notice per run that names the people (a whole team missing is one message). A forgotten clock-out sends `attendance.forgotten` to the worker (important) and to the approvers, naming the worker. Event key `forgot:{shiftId}`, so it is sent once. | `LeaveService.DetectAsync`, `AttendanceAdminService.CloseForgottenAsync` |
| B3 | An approved leave or mission (manager decision, or registered by a manager) excuses the open absences it covers, and an automatic «غیرموجه» too. A manager's own decision stays. The expiry step excuses a covered absence and does not mark it unexcused. Detection also skips a leave that covers the expected start. | `LeaveService.RequestAsync/DecideAsync/DetectAsync`, `Absence.ExcuseByLeave` |
| B4 | `DbUpdateException` + `PostgresException 23505` → 409 ProblemDetails (`errors.duplicate`). Clock-in/out/break save handles a race: a loser with the same idempotency key gets the winner's result (replay, 200), and any other loser gets the normal 409. A correction that would open a second shift → 409 `corrections.openShift` (the correction stays pending). | `GlobalExceptionHandler`, `AttendanceService.SaveOrReplayAsync`, `AttendanceAdminService.DecideAsync` |
| B5 | An approved correction recomputes `LateMinutes` (new clock-in) and `EarlyLeaveMinutes` (new clock-out) from the schedule. A became-late absence gets the corrected minutes too. | `Shift.Correct`, `AttendanceAdminService.DecideAsync` |
| B6 | Detection considers only active staff holding `attendance.self`. The kiosk tablet is never absent. | `LeaveService.DetectAsync` |
| B7 | An auto-closed shift gets a system `ClockOut` event (actor null, key `auto-out-{shiftId}`). A shift created or closed by a correction gets `ClockIn`/`ClockOut` events (actor = approver, keys `corr-in-/corr-out-{correctionId}`), next to the `Correction` event. | `AttendanceAdminService` |

Timing note (§6.6): the warning and the close happen in the same job run, at scheduled end + X hours. The shift is closed at the scheduled end, as before. PROMPT says "warn, then close at the end of the day". The scenario (2.9.g) expects the close at end + X, so that timing was kept.

Integration tests: `tests/Karnama.IntegrationTests/AttendanceRulesTests.cs` (3 tests; its fixture checks JWTs against the fake clock so the test can move through a whole day).
Suites: unit tests 81/81 pass. Integration tests 101/103 pass. The 2 failures are in `DeviceTests` (`FreshPersonAsync` deletes `attendance_events` → «attendance_events is append-only»). They come from the demo history events added in 542ac77, not from these fixes.
