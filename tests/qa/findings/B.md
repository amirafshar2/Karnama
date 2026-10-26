# QA findings — agent B (S02 orders, S10 reports & scores)

Scope: docs/PROMPT3.md §2 items 2 and 10, API level.
Scenarios: `tests/qa/scenarios/02-orders.mjs`, `tests/qa/scenarios/10-reports.mjs`.
How it was run: `bash tests/qa/qa-api.sh 5092 karnama_qa_b`, then `QA_API=http://localhost:5092 node tests/qa/run.mjs S02 S10`.
I ran both scenarios together on a fresh database twice, and S10 alone on a fresh database once more. The results were the same every time: **S02 14/18 steps, S10 9/10 steps, all 9 invariants green.**
Every failing step is a product bug or a spec gap listed below. None of them is a flaky scenario.

Crowd members used (so other scenarios can avoid them):
- S02: `lead[2]`, `office[0]`, the two suggested photographers (`office[8]`, `office[9]` on a fresh DB), `kiosk[0]`, and `client[2,3,4,5,27,28]`.
- S10: `lead[0]`, `office[11,13,14]`, and `client[6,7]`. S10 works on Tehran day +2 … +7, so its report range does not include S02's day 0.

## Coverage

| id | what | result | evidence |
|---|---|---|---|
| 2.2.a | Client reads the catalog (`/services/options`) and the dynamic form (`/services/{id}/form`) | pass | 6 services; single-post form has a required `topic` and a 3-option `mood` select |
| 2.2.b | Invalid form data → 422: required field missing, whitespace only, select value not in the options, bad date, wrong JSON type, text over 500 chars | pass | all 6 return 422; no order was created |
| 2.2.c | Clients order with a valid form | pass | `submitted`, `source=client`; priority is forced to `normal`; price is hidden; unknown keys are dropped; managers get the `order.new` notice |
| 2.2.d | Editor accepts → 403; client accepts → 403; client B reads A's order → 404 | pass | |
| 2.2.e | Admin accepts → one WorkItem per stage, deadline = accept + 48h SLA; double accept with a stale or fresh version → 409 | pass | still one work item; portal quota shows 2 used |
| 2.2.f | Suggestions (`/work/{id}/suggestions`) → assign | pass | worker gets 403; sorted by role and then load; after one assignment the next suggestion moves to a less busy person; assign with a stale version → 409; assignee is notified; portal never sees the assignee |
| 2.2.f2 | Assigning work to a staff account that has no `work.run` (the kiosk) → 422 | **FAIL (bug B3)** | 200, assigned to «کیوسک دفتر (QA)» |
| 2.2.g | Timer: start → pause (DELETE) → resume → start on a 2nd item auto-pauses the 1st | pass | timer on someone else's item → 404; invariant `one-open-timer-per-user` OK; report details give 45 min (25+20) on item 1 and 10 min on item 2 |
| 2.2.h | Resumable upload: part 1 (120 kB); a real socket killed after 60 of 180 kB; status still says 120000; wrong offset (rewind/skip) → 409; another user → 404; resume → complete; append after complete → 409 | pass | downloaded bytes have the same SHA-256; type sniffed as `image/png`; client → 404 before the file is shared |
| 2.2.i | Submit → reviewer rejects with a note → fix → approve → client review | pass | worker → `clientReview`/`done` directly → 403; revisionCount 1; note is an internal comment; worker gets `work.changes`; deliverable becomes client-visible; portal stepLabel is `yourApproval` |
| 2.2.j | Client A approves (5★); client B asks «اصلاح لازم است» | pass | B approving A's item → 404; no note → 422; stars 9 → 422; A's order is `completed`; B's note reaches the card and the portal thread; managers get `portal.changes`; fix → approve → completed |
| 2.2.j2 | Approving an already-done item again → 409; the stars stay | **FAIL (bug B2)** | 204, and `clientStars` changes from 5 to 1 |
| 2.2.k | Agency-managed PublishJob: create, agency publishes with a link | pass | wrong handle → 422; past time → 422; hashtags cleaned; client confirming → 403; `javascript:` link → 422; published → cancel → 409 |
| 2.2.k2 | A worker unrelated to the client cannot mark a post published | **FAIL (bug B4)** | the editor can't list the calendar (403) but `POST /publishing/{id}/published` → 200 `published` |
| 2.2.l | Client-managed page: tick before the time → still scheduled; +31 min, `publish-tick` → `due` + exactly one `publish.reminder`; a second tick sends no duplicate; client D can't confirm C's job (404); client C «منتشر کردم» → `publishedByClient`; D +2h → `overdue` + `publish.overdue` to managers | pass | |
| 2.2.m | Order cancel path | **FAIL (gap B1)** | admin cancel works (work items cancelled, quota slot returned, client sees `cancelled`; cancel twice → 409; accept after cancel → 409; reject needs a reason, client sees it). **The client cannot cancel**: there's no portal route, and `POST /orders/{id}/cancel` → 403 |
| 2.2.n | Quota used up → new order is «خارج از پکیج» at base price; a service outside the package → outside | pass | used never goes over total; invariant `quota-never-negative` OK; order list has `outsidePackage=true` (see note N1 about the admin warning) |
| 2.2.o | Invariants; clock-out stops the timer; no timer on a finished item (409) | pass | |
| 2.10.a–d | Designed data on day +2 … +7 (see "Proof" below) | pass | |
| 2.10.e | Range report for the team (`/reports/work`) and one worker (`?user=`, `/work/{userId}`) | pass | X: done 2, timer 90 min, est 180, actual 90, useful% in 0–100, by-service counts; details have 2 shifts, 2 timers and 2 done; a daily point for each day; to<from → 422; >92 days → 422 |
| 2.10.f | Permissions | pass | worker sees only their own row; another worker's details → 404; export → 403; client → 403 |
| 2.10.g | Excel export | pass | 200; content-type `…spreadsheetml.sheet`; `attachment; filename=work-report-….xlsx`; starts with `PK\3\4`; unzipped: `[Content_Types].xml` and `xl/workbook.xml` present; sheet has Persian headers and rows for X, Y, Z. **PDF** is the browser's print-to-PDF of the web page. There is no server PDF endpoint, so it was not checked at API level (it belongs to the visual/web pass) |
| 2.10.h | Scores + «چرا این امتیاز» | pass | everyone has 7 indicators (quality, speed, onTime, volume, discipline, manager, client), each with a value text, a hint and a weight; X has 2 samples, which is under 5 → `enough=false`, `total=null`; the worker sees only themselves with all 7 explanations; another person's trend → 404; own trend has 6 points |
| 2.10.i | Late because the client approved late → not counted against the worker | pass | X1 handed in before its deadline but approved 3 days later → `onTime=true`; X2 (worker started after the deadline) → `onTime=false` (control case); X's onTime indicator is 50; Y's stage 1 (client sat on it for 4 days) → on time, 100 |
| 2.10.j | Next stage that unlocked late because of client silence → must not count against Z | **FAIL (bug B5)** | Z had the item for 30 min, yet `onTime=false` (deadline `…05:30Z` < delivered `…11:00Z`) and Z's onTime indicator is 0 |

Totals: 28 steps, 23 pass, 5 fail. The 5 failures are 4 product bugs (B2–B5) and 1 spec gap (B1).

## Product bugs

### B5 — Later stages keep the deadline set at accept time, so client delay is counted against the next worker (medium, fairness rule PROMPT2 §11 / §N8 test list)
- **Where:**
  - `src/Karnama.Application/Work/OrderService.cs:190-197` (`AcceptCoreAsync`) sets every stage's deadline once: the sum of the SLAs from the accept time.
  - `src/Karnama.Domain/Work/WorkItem.cs:80` (`Unlock`) and its callers (`WorkService.cs:146` and `PortalService.cs:147`) never move the deadline.
  - `PerformanceService.cs:243,285` compares the first hand-in with that fixed deadline.
- **Repro (S10 2.10.a–d, 2.10.j):**
  1. Accept a «کمپین اینفلوئنسر» order at t0. Stage 1 needs client approval; the deadlines are t0+48h and t0+96h.
  2. Stage 1 is handed in at t0+40 min and reaches `clientReview`.
  3. The client approves at t0+101h. Stage 2 unlocks; its deadline has already passed.
  4. Z moves stage 2 to inProgress → inReview 30 minutes later, and the manager moves it to done.
  5. `GET /reports/work/{Z}` gives `onTime=false`, and `/reports/scores` gives Z `onTime` 0 ("۰ از ۱ قبل از ددلاین").
- **Expected (PROMPT2 §11 «کارهایی که به‌خاطر تأخیر مشتری عقب افتاده‌اند حساب نشوند»):** time spent waiting for the client on an earlier stage must not count. One possible fix is to recompute a Waiting stage's deadline on `Unlock` (now + its SLA, capped by the order deadline only if that is still in the future). Another is to exclude client-wait time from on-time.
- **Note:** the direct case (the same item waits in `clientReview`) is handled correctly, because `FirstSubmittedAt` is used (2.10.i passes).

### B2 — Approving an already-approved item returns 204 and lets the client change its stars (low–medium)
- **Where:**
  - `src/Karnama.Application/Portal/PortalService.cs:132-134`: `MoveTo(Done)` on a Done item is a no-op success (`WorkItem.cs:91`, `if (target == Status) return Success`), and then `RateByClient` overwrites `ClientStars`.
  - The audit entry, the comment and the manager notification are also written again.
- **Repro (S02 2.2.j2):**
  1. Client A approves with `{approve:true, stars:5}`; the item is done.
  2. Client A sends `POST /api/v1/portal/approvals/{id} {approve:true, stars:1}` → 204.
  3. `GET /work/{id}` gives `clientStars = 1`.
- **Expected:** 409 «این کار بسته شده است» (only `clientReview` items can be decided). Stars are fixed at approval time and feed the worker's score, so the client should not be able to change them afterwards.

### B3 — Work can be assigned to any active staff account, even one without `work.run` (low)
- **Where:** `src/Karnama.Application/Work/WorkService.cs:167`. It checks only `Kind == Staff && IsActive`.
- **Repro (S02 2.2.f2):** `POST /work/{id}/assign {userId: <kiosk account>}` → 200; the card is now assigned to «کیوسک دفتر (QA)».
- **Impact:** a kiosk tablet, a marketer or a blog writer can't see or run the item (`work.view`/`work.run` assigned). The work is silently stuck.
- **Related:** `SuggestAsync` (`WorkService.cs:188`) also lists people without `work.run`, for example the 5 marketers and «سارا (مدیر نمونه)», after the role holders. Suggesting a marketer is harmless, but it is noise.
- **Expected:** 422 «این همکار نمی‌تواند کار انجام دهد». Suggestions should be limited to people who have `work.run`.

### B4 — Any worker can mark any agency publish job "published" (low, authorization scope)
- **Where:** `src/Karnama.Application/Publishing/PublishService.cs:118`. `me.Has(Permissions.WorkRun, PermissionScope.Assigned)` is treated as "may confirm every job". There is no check that the worker is assigned to that client or order.
- **Repro (S02 2.2.k2):**
  1. The manager schedules an agency-managed job for client A.
  2. An editor with no work for client A is refused the calendar (`GET /publishing` → 403).
  3. But `POST /api/v1/publishing/{id}/published {postUrl}` from the same editor → 200 `published`.
- **Expected:** 403/404 unless the worker has a work item on that order or client, or let only `orders.manage` confirm. PROMPT §6.5 allows «ادمین/کارگر»; the assigned scope should still apply.

## Spec gaps / mismatches

### B1 — The client cannot cancel its own order (gap vs PROMPT3 §2.2 "خطا + لغو", and the task brief "client cancels an order")
- **Where:** `src/Karnama.Api/Endpoints/WorkEndpoints.cs:28`. Cancel lives only in the `orders.manage` group, and `PortalEndpoints` (`WorkEndpoints.cs:96-106`) has no cancel route.
- **Repro (S02 2.2.m):** a portal user sends `POST /api/v1/orders/{own order}/cancel` → 403.
- **Also:** docs/PROMPT.md:109 lists `Cancelled` as an order state but does not say who cancels. Decide whether a client may cancel at least a `submitted` (not yet accepted) order from the portal. If not, the PROMPT3 scenario should say "admin cancels at the client's request". The admin cancel path works fully (quota slot returned, work items cancelled).

### N1 — "Outside package" order has no admin warning (question, PROMPT §6.4: «سهمیه تمام شد → سفارش جدید «خارج از پکیج» با هشدار برای ادمین»)
- `OrderService.AcceptCoreAsync` (`OrderService.cs:199`) only flags the order (`OutsidePackage=true`, `Price=BasePrice`) and writes an audit entry.
- No notification is sent.
- The client's `order.new` notice is created before the quota check, so it can't say "extra" either.
- If the board/order list badge counts as the "warning", this is fine. Otherwise a `orders.outsidePackage` notice to `orders.manage` is missing. Not asserted as a failing step.

### N2 — PDF report
- There is no API endpoint for it: PROMPT2 §9 wants «خروجی Excel و PDF (با لوگو و رنگ برند)».
- PDF is the web page's print stylesheet, so it needs the visual/web pass (logo, brand colour, RTL table). It is not covered here.

## Scenario assumptions that turned out wrong (fixed in the scenarios, not product bugs)
- Access tokens live 15 minutes **on the QA clock**, so `qa.advance()` past 15 minutes makes every actor's next call return 401. Both scenarios re-sign everyone in after each clock move (`advance()` helper). Other agents will hit the same thing.
- A timer needs an open shift (`attendance.notIn` 409, unless `AutoClockInOnTimer`), so workers clock in from the office IP first.
- The QA clock keeps ticking in real time after `setClock`, so deadline checks use a tolerance.
- Client portal orders ignore `autoAccept` and `priority` by design (asserted as such).

## After fixes

Re-run on a fresh database (Release build): `bash tests/qa/qa-api.sh 5092 karnama_qa_b`, then `QA_API=http://localhost:5092 node tests/qa/run.mjs S02 S10`.
Result: **S02 18/18, S10 10/10, all 9 invariants green (28/28 steps).**

| id | fix | where |
|---|---|---|
| B5 | When a waiting stage unlocks later than the plan expected, its deadline moves forward by that delay. "Planned start" is the latest deadline of the earlier stages (or the accept time). So the next worker always gets the full window the plan gave the stage, and client delay doesn't count against them (PROMPT2 §11). Unlocking early never pulls a deadline in. 2.10.j now gives Z `onTime=true` | `WorkItem.Unlock(now, plannedStart)`, `WorkService.PlannedStart`, both unlock paths (`WorkService.AfterStatusChangeAsync`, `PortalService.DecideAsync`) |
| B2 | The client can decide only an item in `clientReview`. A done or cancelled item → 409 «این کار بسته شده است», and the stars stay. Another open state → 403, as before | `PortalService.DecideAsync` |
| B3 | Assign needs `work.run` (assigned scope or wider) → otherwise 422 «این همکار نمی‌تواند کار انجام دهد». Suggestions list only these people (no marketers, no kiosk) | `WorkService.AssignAsync`, `SuggestAsync` |
| B4 | Without `orders.manage`, a worker can mark a post published only if it belongs to an order where they have a stage assigned. A job without an order → managers only (403 otherwise) | `PublishService.MarkPublishedAsync` |
| B1 | New `POST /api/v1/portal/orders/{id}/cancel`. A client can cancel its own order only while it is `submitted`. Another client → 404. After acceptance → 409 «… برای لغو به آژانس پیام بدهید». Cancelled/closed → 409. Managers get `order.cancelledByClient`. The portal order sheet has a «لغو سفارش» button with an inline confirmation. Scenario 2.2.m now uses the route (client cancel → twice 409 → other client 404 → accept 409), then shows the accepted-order refusal and the admin cancel | `Order.CancelByClient`, `PortalService.CancelOrderAsync`, `PortalEndpoints`, `portal-order.tsx`, `fa-portal.ts` |
| N1 | When an accepted order falls outside the package, every `orders.manage` holder gets an `orders.outsidePackage` notice (event key `outside:{orderId}`, so one per order). Checked in 2.2.n | `OrderService.AcceptCoreAsync` |

Integration tests: `WorkTests` (outside-package notice, assign/suggest only `work.run`, late unlock gives the full window back), `PortalTests` (late client approval keeps the next stage fair + a decision is final, client cancel before/after acceptance), `NotifyTests` (only a worker on the order may mark an agency post published).
