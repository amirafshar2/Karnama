# QA findings D — S06 packages, S11 public site, S12 admin, S13 CCTV, S14 help

How these were run: an isolated QA API, started with `bash tests/qa/qa-api.sh 5094 karnama_qa_d` (database `karnama_qa_d`), then
`QA_API=http://localhost:5094 node tests/qa/run.mjs S06 S11 S12 S13 S14`.
Each run used a fresh database, and every run was repeated once on the same database. Results were identical every time:
**4/5 scenarios and 56/58 steps pass, and all database invariants are green.** The 2 failing steps are real product gaps, described below.
S13 was also run once against a "configured but unreachable" media box. For that run the API was started with
`Cctv__ApiUrl=http://127.0.0.1:59997 Cctv__RtspUrl=rtsp://127.0.0.1:58554 Cctv__PublicUrl=https://cctv.qa.invalid Cctv__InternalUser=… Cctv__InternalPassword=…`.
S13 passed 8/8 in that mode too.

Scenario files: `tests/qa/scenarios/06-packages.mjs`, `11-site.mjs`, `12-admin.mjs`, `13-cctv.mjs`, `14-help.mjs`.

Notes on running the scenarios:
- **Access tokens follow the QA clock.** Their lifetime is 15 minutes. Moving the clock backwards, or more than 15 minutes forwards, turns every existing token into a 401.
  For this reason the scenarios always move the clock forward from tomorrow and sign in again after every jump. Other scenario authors should do the same.
- **Inboxes return only the newest 30 items.** On a database that was used before, S06 starts its clock after the latest notification already there.
  Without this, the new notices would fall outside the first 30.
- **Every crowd and demo account is `isDemo`.** As a result, `reveal/start` returns `demoCode` on screen.
  The real "SMS only" path, where the dev log shows `[DEV SMS] OTP for 0912***1234: 123456`, cannot be reached from the QA crowd.
  This is because any user created by a demo admin is also demo (`UserService.cs:62`, `ClientService.cs:157`). That path is covered by `CredentialTests` instead.

## Coverage

| id | what | result | evidence |
|---|---|---|---|
| 2.6.a | admin creates a quota-consuming service | pass | POST /services 201 |
| 2.6.b | invalid package refused (special ≥ price, discount end ≤ start, no SKU, validity 0) | pass | 422 ×4 |
| 2.6.c | package with time-limited discount + full Markdown description (h2, bold, link; `<script>` stripped), FAQ, features | pass | effectivePrice 80M inside the window |
| 2.6.d | public package page shows the special price | pass | GET /public/site/packages/{slug} |
| 2.6.e | QA clock past the discount window → effective price back to regular | pass | 100M at +3 days |
| 2.6.f | client + portal login | pass | hasPortal true |
| 2.6.g | page password stored in the vault, never echoed or listed | pass | hasSecret, body scan |
| 2.6.h | assign FullAccess package; price frozen at the special price | pass | daysLeft 8, price 80M |
| 2.6.i | reveal: no permission → 403; wrong code → 422; right code → password for 30 s; reused code → 422; lastRevealedAt; owner notified | pass | owner inbox `credentials.revealed` (the audit row is not readable through the API: History has no `ClientCredential`) |
| 2.6.j | portal shows package, days left, quota 0/2 | pass | /portal/home |
| 2.6.k | quota runs out: 3rd order `outsidePackage=true` at the service base price; used never > total | pass | false,false,true |
| 2.6.l | cancelling an in-quota order frees the slot; missing required form field → 422 | pass | 4th order in quota |
| 2.6.m | renewal: note > 500 → 422; once per day (`alreadyRequested`); manager told once; staff → 403 | pass | |
| 2.6.n | day-by-day clock + `package-expiry` twice per step → 7, 3, 1, last-day, ended each exactly once; quota-used-up once; contract-end "change your passwords" once | pass | 22 job runs; counts asserted; invariant `no-duplicate-expiry-notice` green |
| 2.6.o | after the end: no active package, history keeps it, new order is extra | pass | |
| 2.6.p | package soft-delete + restore; re-assign; cancel an assigned package | pass | |
| 2.11.a | visitor: home, cases + one case, packages + one, blog + one post, both sitemaps | pass | all 200 |
| 2.11.b | unknown case/post/package → 404 | pass | |
| 2.11.c | consultation form: empty name / bad phone / message > 1000 → 422; honeypot → 202 and no lead | pass | |
| 2.11.d | consultation → exactly one CRM lead (double submit merged), message + origin "فرم سایت" | pass | GET /crm/leads?q=phone |
| 2.11.e | blog image upload (SVG refused 422); served at `/media/public/blog/<32hex>.png` | pass | |
| 2.11.f | /blog/markdown → safe HTML (no `<script>`, no `javascript:`) | pass | |
| 2.11.g | writer draft with image in the middle + automatic SEO; submit → inReview; second submit 409 | pass | |
| 2.11.h | writer can't publish (403); not public while in review | pass | |
| 2.11.i | reviewer (QA manager, blog.publish) schedules for +2 h; still 404 publicly | pass | |
| 2.11.j | cancel: scheduled post unpublished → draft; < 30 words can't publish (422); writer archives own draft | pass | |
| 2.11.k | clock +3 h → `blog-schedule` → public, image between the two paragraphs, foreign image dropped, listed; cancelled post stays 404; second job run → 0 | pass | |
| 2.11.l | writer can't edit after publishing (403) | pass | |
| 2.12.a | users CRUD + soft-delete/includeDeleted/restore; bad phone 422; duplicate 409; can't delete self 409 | pass | |
| 2.12.b | services CRUD; stale version 409; delete/restore | pass | |
| 2.12.c | packages: stale version 409; price change in History; delete frees SKU; restore while SKU reused → 409, then OK; Excel export | pass | |
| 2.12.d | clients CRUD + delete/restore; bad mobile 422 | pass | |
| 2.12.e | platforms CRUD; duplicate key 409; delete/restore | pass | |
| 2.12.f | leave types create/update/deactivate; bad colour 422; writer 403; **no delete/restore** | pass (gap noted) | DELETE → 405 |
| 2.12.g | asset categories create/rename/deactivate; duplicate 409; writer 403; **no delete/restore** | pass (gap noted) | |
| 2.12.h | help articles create/update/delete; writer 403; **no restore** | pass (gap noted) | |
| 2.12.i | new role (crm.leads own) → assign to a user → /me shows it at once; next token can call /crm/leads | pass | |
| 2.12.j | role edit (+cctv.view), stale version 409, role delete → next token loses access, restore → back; system role delete 409 | pass | |
| 2.12.k | **SECURITY**: a roles.manage-only user can't raise the permissions of their own role | **FAIL** | PUT own role 200 → users.manage=all (bug B1) |
| 2.12.l | English listed as LTR; admin text override in the en bundle; bad key 422; writer 403; PATCH /me language en (xx → 422) and back | pass | |
| 2.12.m | an API to add/activate a new language (PROMPT R13 / PROMPT3 §2.12) | **FAIL** | no write endpoint for languages (gap G1) |
| 2.12.n | brand colours: bad hex 422; writer 403; save → /public/brand shows them immediately; restored | pass | |
| 2.12.o | setup can't be re-run: status needsSetup=false; POST /setup → 409 `setup.demoInstall`; empty body refused | pass | |
| 2.13.a | camera page status (configured flag) | pass | false (standard QA API) / true (dead-box run) |
| 2.13.b | add camera: ethics checkbox, RTSP-only, password never returned; pure viewer doesn't see the masked address | pass | |
| 2.13.c | no cctv.view (writer, client) → 403 on list/snapshot/live/stop; viewer can't add/delete; anonymous 401 | pass | |
| 2.13.d | snapshot without a working box → 409 `cctv.notConfigured` / `cctv.noFrame`, < 12 s | pass | 290 ms with a dead box |
| 2.13.e | live: not configured → 409; configured → 5-minute token, idleSeconds 120 | pass | (both modes) |
| 2.13.f | idle stop endpoint accepted | pass | 204 |
| 2.13.g | resync: not configured → 409 (configured → 200, see B5); media-auth hook from a non-tunnel address → 403 | pass | |
| 2.13.h | deactivate → hidden from viewers, live 404; delete → 404 | pass | |
| 2.14.a | "؟" article by page key: exact, deepest prefix, unknown → 204, missing key → 400, anonymous 401 | pass | |
| 2.14.b | audiences: client vs staff lists and pages; client can't open a staff article by slug (404) | pass | |
| 2.14.c | PROMPT2 §13 seed articles present | pass | |
| 2.14.d | search; draft hidden from staff and from the page button; publish → used; "everyone" reaches clients; XSS stripped | pass | |
| 2.14.e | first-run tour inputs per role from /me (admin / worker / marketer / client) + tour step keys present | pass | tour is front-end only (`components/help/tour.tsx`) |
| 2.14.f | every navigation page (30) has a `help.page.*` text | pass | static check of `nav.ts` vs `fa-n12.ts` |
| 2.14.g | `node tools/check-help.mjs` | pass | "check-help: all 261 fields explained." (but see C1) |

Not covered at the API level:
- The real live loop (snapshot → WebRTC → stop after idle) needs MediaMTX on the server with an RTSP loop source, for example
  `ffmpeg -re -stream_loop -1 -i sample.mp4 -c copy -f rtsp rtsp://127.0.0.1:8554/loop` and a camera with `mainUrl=rtsp://127.0.0.1:8554/loop`.
  It also needs a browser (WHEP) to measure the 120-second idle stop, which lives in the page, not the API.
- Empty install → `/setup` wizard: this needs a separate `Seed__Mode=Empty` database and was not part of this run.

## Product bugs

**B1 — Privilege escalation through role editing (high).**
`RoleService.CreateAsync` and `UpdateAsync` (`src/Karnama.Application/Roles/RoleService.cs:62` and `:74`) never check that the caller holds the permissions they grant.
Role assignment has that guard (`UserService.cs:174`, `users.roleTooStrong`), but editing a role does not.
- Repro:
  1. Admin creates role R with only `roles.manage/all`.
  2. Admin creates user U with role R.
  3. U signs in, `GET /api/v1/roles/{R}`, then `PUT /api/v1/roles/{R}` with permissions `roles.manage, users.manage, settings.manage` → **200**.
  4. U's next token has `users.manage=all`.
- Result: from there U can create users with any role, and the same works for every other permission. The same hole lets a roles.manage user strengthen any role held by others.
- Fix idea: apply the same rule as `users.roleTooStrong`. Only an Owner may grant a permission/scope the caller does not hold.
- Scenario: S12 2.12.k.

**B2 — Removed or deactivated access keeps working for up to 15 minutes (medium, security).**
Permissions live in the JWT (`CurrentUser` reads claims), and the SignalR `PermissionsChanged` push only asks a well-behaved client to refresh.
- Repro:
  1. A user's token can call `/api/v1/crm/leads`.
  2. Admin removes the role → the old token still gets **200**.
  3. Admin then sets `isActive=false` → the old token still gets **200**.
- Expected per PROMPT3 §2.12 "اعمال لایو": immediate effect.
- Fix idea: a per-user security stamp or permission version claim, checked against a cached value on each request (or revocation of the token family plus a short token cache).
- Location: auth pipeline (`src/Karnama.Api/Infrastructure/CurrentUser.cs`), `UserService.cs:101`, `RoleService.cs:122`.

**B3 — `package-expiry` reports work it did not do (low).**
In `PackageExpiryJob.cs:42-47`, `Both()` does `sent++` even when the notifier skipped the notice because its event key already exists.
- Result: the quota-used-up branch runs on every pass, so every run returns ≥1 while a quota is used up.
- Repro:
  1. Create a client with a package whose quota is 1.
  2. Place 1 auto-accepted order.
  3. `POST /api/v1/qa/jobs/package-expiry` three times → `1, 1, 1`. No new notifications are created, so the invariant stays green.
- Impact: misleading Worker logs and QA counts. The job also runs one order query per active package on every pass.

**B4 — Public site keeps serving edited or deleted packages (low).**
`PackageService` (`src/Karnama.Application/Catalog/PackageService.cs`, Update/Delete/Restore/SetCover) never calls `IPublicCache.EvictAsync`. Blog, site home and cases do.
- Repro:
  1. Read `/api/v1/public/site/packages/{slug}`.
  2. Change the price (PUT) → same old price.
  3. DELETE the package → still 200 and still in `/public/site/packages`.
- This lasts until the 30 s output cache expires (`Program.cs:104`).

**B5 — CCTV "configured but box unreachable" reports success (low).**
The API was started with `Cctv__*` pointing at a closed port.
- Adding or updating a camera returns 200 with no warning, even though `UpsertPathAsync` failed (log: `MediaMTX unreachable while saving cam-…`).
- `POST /api/v1/cctv/resync` returns **200** with the camera count, although every push failed (`CctvService.cs:160-166` ignores the bool from `PushAsync` and `IMediaServer.UpsertPathAsync`).
- `POST …/live` returns 200 with a WHEP token for a box that is down (`CctvService.cs:196`). The page only fails later in the browser.
- Snapshot behaves correctly: 409 `cctv.noFrame` in about 0.3 s, never a 500.

**B6 — `live/stop` accepts any camera id (low).**
`StopLiveAsync` (`CctvService.cs:210`) doesn't check that the camera exists.
- Repro: `POST /api/v1/cctv/cameras/<random guid>/live/stop` → 204, and a `cctv.liveStopped` audit row is written for a camera that doesn't exist.
- Impact: audit noise, and any viewer can spam the audit log.

**B7 — Seeded data breaks the package capacity rule (low, seed only).**
On a fresh QA database "طلایی (نمونه)" has `capacity 10` but `activeClients 14`. `QaCrowdSeed.cs:86` (and `DemoCatalogSeed.cs:110`) add `ClientPackage` rows directly and skip the capacity check in `ClientService.cs:182`.
- Impact: demo screens show an over-full package, and a crowd run of "assign Gold" gets `clientPackages.full` straight away.

**B8 — Help delete ignores the QA clock (cosmetic).**
`HelpService.cs:118` uses `DateTimeOffset.UtcNow` instead of the injected `TimeProvider`. As a result, `deletedAt` is wrong under the QA clock and in tests.

## Gaps against the prompts (missing features, not wrong behaviour)

**G1 — No "add language" API (PROMPT R13 / §6.10, PROMPT3 §2.12).**
- Only `GET /api/v1/i18n/languages` exists, plus text overrides under `/api/v1/translations/{lang}`.
- English is pre-seeded and active, so the "switch a user to LTR" part works (2.12.l).
- An admin cannot add `ar`/`tr` or toggle a language. PROJECT_MAP marks M8 (languages) as ⬜, so this is known to be unbuilt.
- Scenario: S12 2.12.m stays red until it is built.

**G2 — No soft-delete or restore for some entities.**
- Leave types and asset categories have create/update (deactivate) only: no DELETE or restore.
- Help articles soft-delete but have no restore endpoint.
- Other main entities (users, roles, services, packages, clients, platforms) have both.
- PROMPT3 §2.12 says "CRUD همه‌ی موجودیت‌ها". This needs a decision: deactivation may be the intended "delete" for leave types and categories.

**G3 — No API for the credential-reveal audit.**
`/api/v1/history/{entityType}` doesn't accept `ClientCredential`. Reveals are audited (`credentials.revealed`, also `revealFailed`), and the owner gets a notification, but the audit rows can't be read back through the API or the panel history.

## Coverage gap in the automated help check

**C1 — `tools/check-help.mjs` only checks some form components.**
It scans the 6 wrapped components (`TextField`, `NumberField`, `SelectField`, `TextArea`, `JalaliDateField`, `Toggle`) and reports "all 261 fields explained".
About 100 raw `<input>`, `<textarea>` and `<select>` elements outside `components/ui` are not checked at all. Examples:
- blog post editor tags/category/cover alt and schedule date/time (`components/blog/post-editor.tsx:244-353`)
- image alt/caption (`rich-editor.tsx:233-238`)
- consultation form (`components/site/consult-form.tsx:38-50`)
- CRM settings (`crm/settings.tsx:81-98`)
- asset categories (`assets/categories.tsx:43,58`)
- brand page (`settings/brand/page.tsx:215`)
- texts page (`settings/texts/page.tsx:115,144`)
- case editor metrics (`siteadmin/case-editor.tsx:108-133`)
- portal order and approval notes

Some of these are search boxes or filters that need no help, but PROMPT2 §13 says "زیر هر فیلد". The check should also flag raw controls, or those should move to the wrapped components or get a `help-ok` opt-out.
These are not verified as bugs: the API run can't see whether a nearby label explains them.

## Scenario assumptions that were wrong (fixed in the scenarios, not product bugs)

- **Demo 09000000002 has no blog rights.** It is a video editor (`DEMO.editor`), not a blog editor. The blog reviewer/publisher in S11 is the QA manager (09910000003, `blog.publish`).
- **The QA manager has `cctv.manage`.** S13 therefore creates a role with `cctv.view` only, to get a pure viewer.
- **The page-help parameter is `key`, not `path`** (`GET /api/v1/help/page?key=attendance`). Without it the API returns 400.
- **The case detail returns `{ card: { slug, … }, metrics, shots, … }`**, not a flat object.
- **Default brand colours are a deliberate decision.** PROMPT2 §1 asked for black `#0A0A0A` + yellow `#FEFF00`, but `BrandDefaults` is `#3B6CFF`/`#0B1B34`. PROJECT_MAP ("رنگ و لوگو", line 130) records that the UI redesign deliberately moved to navy + royal blue, so this is not a bug.
  The tutorial-video section of PROMPT3 §6 still says black/yellow. That conflict needs a decision by Amir before Q7.
- **First-run tours are not API-backed.** They live in `components/help/tour.tsx` with `localStorage` `karnama.tour.v1.<userId>`. S14 checks their inputs (`/me` kind and permissions) and the step keys. Visual tour checks belong in Playwright.
- **Reusing a deleted package's SKU is intended.** A deleted package doesn't block a new one with the same SKU, and restoring the old one then returns 409 `packages.duplicate`. That is intended (`PackageService.RestoreAsync`).

## After fixes

Fresh QA database (`qa-api.sh 5093 karnama_qa_c`), then `run.mjs S04 S05 S08 S06 S11 S12 S13 S14`: S06 16/16, S11 12/12, **S12 14/15** (only G1, the missing language API, is still red), S13 8/8, S14 7/7, invariants green.
S13 was also run against a configured but dead media box (the `Cctv__*` variables above): 8/8.

| bug | fix | where | test |
|---|---|---|---|
| B1 role privilege escalation | A non-owner can create, edit, delete or restore a role only if they hold **every** grant of it (before and after the change) at that scope or wider; otherwise 403 `roles.tooStrong`. They can't edit a role they hold themselves (403 `roles.ownRole`). Owners are unrestricted. Checked against the database, not the token. | `RoleService.GuardAsync` | `RolesTests.A_roles_manager_cannot_escalate_through_roles`; S12 2.12.k |
| B2 revoked access works for 15 min | New `users.SecurityStamp` column (migration `Q1SecurityStamp`). It moves when the user's roles change, when a role's grants change or it is deleted/restored (all members), on deactivation and on delete. Access tokens carry it (`sst` claim); `OnTokenValidated` compares it with the current value through HybridCache (≤ 30 s, evicted right after a change), so an old token gets 401 at once and the web client refreshes (it already retries once after a refresh on 401). Refresh issues a token with the new stamp; a deactivated user's refresh is refused. Renaming a role or a user doesn't end sessions. The realtime `permissionsChanged` push is unchanged. Tokens of users that don't exist are rejected too. | `User`, `SessionStamps`, `Program.cs`, `TokenService`, `AuthService`, `UserService`, `RoleService` | `SessionStampTests` (in `AuthTests.cs`): role grant change → 401 → refresh → new rights; role removed → 401; deactivated → 401 and refresh refused |
| B3 `package-expiry` count | The job returns the number of notifications it really created (`Notifier.NotifyAsync` now returns that count). A second run returns 0. | `PackageExpiryJob`, `Notifier` | `NotifyTests.Package_expiry_job_reports_the_notifications_it_really_created` |
| B4 public cache keeps old packages | Package create/update/cover/delete/restore evict the public cache, like site content does. | `PackageService` | `CatalogTests.Package_changes_reach_the_public_site_at_once` |
| B5 dead box reports success | Configured but unreachable box: saving an active camera → 409 `cctv.unreachable` (after the input checks, nothing saved); a push that fails after saving → 409 too; resync → 409 `cctv.unreachable`; live → 409 `cctv.unreachable` (no token). Snapshot is unchanged (409 `cctv.noFrame`). Not configured still gives `cctv.notConfigured`. Creating a camera with `isActive:false` now really creates it inactive. | `CctvService` | `CctvTests.A_box_that_cannot_be_reached_is_reported_and_stop_needs_a_real_camera` |
| B6 `live/stop` any id | Unknown camera → 404, and no audit row. | `CctvService.StopLiveAsync` | same test; S13 2.13.f |
| B8 help delete clock | Uses the injected `TimeProvider`. | `HelpService` | `HelpTests.Deleting_an_article_stamps_the_app_clock` |

Not fixed here: B7 (seed over-fills packages, seed only), G1–G3 (missing features / decisions), C1 (help-check coverage).

Scenario changes (they assumed the old behaviour):
- S12 2.12.i: after the role is assigned, the user's **old** token now gets 401 (it used to keep working and only `/me` showed the change); the scenario signs in again and checks `/me` and `/crm/leads`.
- S12 2.12.j: also checks that the old token gets 401 right after the role is deleted, and after the user is deactivated.
- S13: a configured-but-dead box now refuses an active camera with 409 `cctv.unreachable`, so 2.13.b adds the camera inactive in that mode; 2.13.e/2.13.g accept 409 `cctv.unreachable`; 2.13.f checks that stopping an unknown camera is 404.
