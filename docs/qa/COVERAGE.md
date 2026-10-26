# پوشش سناریوها (docs/PROMPT3.md §2)

> ساخته‌شده از آخرین اجرا (`run-2026-10-26T11-19-28-217Z.json`، 2026-10-26T11:19:28.217Z) با `node tests/qa/coverage.mjs`. هر ردیف یک بررسی خودکار است.

**جمع:** 198/198 بررسی سبز · 14/14 سناریو · ثابت‌های دیتابیس: همه سبز

## §2.1 ورود صبح

| شناسه | بررسی | نتیجه | سناریو |
|---|---|---|---|
| 2.1.a | a default work schedule applies to the crowd (Sat–Wed 09:00, grace 10) so lateness is computable | ✅ | S01 |
| 2.1.b | kiosk gets a rotating QR; a worker and a kiosk login from home are refused | ✅ | S01 |
| 2.1.c | 10 office staff clock in at 08:55 with a fresh kiosk QR → green, no review, not late | ✅ | S01 |
| 2.1.d | a QR already used by someone else is flagged (red + needs review), never counted twice | ✅ | S01 |
| 2.1.e | a forged or stale QR from home is flagged red | ✅ | S01 |
| 2.1.f | replaying the same clock-in (same idempotency key) does not open a second shift | ✅ | S01 |
| 2.1.j | field worker with a genuine GPS fix inside the office radius → green via Geofence | ✅ | S01 |
| 2.1.g | a second clock-in with a new key while a shift is open → 409 | ✅ | S01 |
| 2.1.h | clock-in from home (no QR, no GPS) → accepted but yellow + needs review | ✅ | S01 |
| 2.1.i | field worker with a mock (fake) GPS location → red + needs review | ✅ | S01 |
| 2.1.k | absence job after start+grace: late and absent people get an Absence; present people do not; re-run is idempotent | ✅ | S01 |
| 2.1.k2 | the absent worker is notified (and, per PROMPT §6.7.2, the admin too) | ✅ | S01 |
| 2.1.l | two late arrivals (09:25, 09:40) → lateMinutes 25 / 40 and their absence turns into lateness | ✅ | S01 |
| 2.1.m | absent worker A writes a reason (09–11 doctor) → manager excuses it | ✅ | S01 |
| 2.1.n | absent worker B writes a reason → manager rejects (unexcused) | ✅ | S01 |
| 2.1.o | absence error paths: worker can't decide (403), can't write someone else's reason (404), became-late can't be decided (409) | ✅ | S01 |
| 2.1.p | manager live board and review queue reflect the morning | ✅ | S01 |
| 2.1.q | manager accepts the home shift and rejects the fake-location shift; a worker can't review | ✅ | S01 |
| 2.1.s | monthly attendance report counts a late arrival once (40 min), not shift + absence twice | ✅ | S01 |
| 2.1.t | double-tap: 5 parallel clock-ins (different keys) open exactly one shift; the rest get 409 | ✅ | S01 |
| 2.1.t2 | offline-queue retry: 4 parallel sends with the SAME idempotency key → one shift, no 5xx | ✅ | S01 |
| 2.1.u | the kiosk tablet account is not treated as an absent worker | ✅ | S01 |
| 2.1.v | an approved clock-in correction (09:25 → 08:58) also clears the lateness it caused | ✅ | S01 |
| 2.1.r | invariants after the morning (one open shift per user, kiosk QR used once) | ✅ | S01 |

## §2.2 سفارش‌ها

| شناسه | بررسی | نتیجه | سناریو |
|---|---|---|---|
| 2.2.a | مشتری کاتالوگ و فرم دینامیک خدمت را می‌بیند | ✅ | S02 |
| 2.2.b | داده‌ی نامعتبر فرم → 422 (الزامی خالی، گزینه‌ی ناموجود، تاریخ غلط، نوع غلط) | ✅ | S02 |
| 2.2.c | مشتری‌ها با فرم معتبر سفارش می‌دهند (اولویت مشتری همیشه عادی) | ✅ | S02 |
| 2.2.d | کارگر بدون مجوز سفارش را بپذیرد → 403؛ مشتری B سفارش A را ببیند → 404 | ✅ | S02 |
| 2.2.e | ادمین می‌پذیرد → WorkItem با ددلاین SLA؛ پذیرش دوباره → 409 | ✅ | S02 |
| 2.2.f | پیشنهاد خودکار کارگر (نقش مرحله، کمترین بار) → سپردن | ✅ | S02 |
| 2.2.f2 | سپردن کار به کسی که مجوز اجرای کار ندارد (حساب کیوسک) → 422 | ✅ | S02 |
| 2.2.g | تایمر: شروع → مکث → کار دوم → مکث خودکار اولی (فقط یک تایمر باز) | ✅ | S02 |
| 2.2.h | آپلود قابل ادامه: تکه‌ی ۱ → قطع نت وسط تکه‌ی ۲ → پرسیدن offset → ادامه؛ offset غلط → 409 | ✅ | S02 |
| 2.2.i | ارسال برای بازبینی → رد با یادداشت → اصلاح → تأیید → ارسال برای مشتری | ✅ | S02 |
| 2.2.j | مشتری A تأیید می‌کند؛ مشتری B «اصلاح لازم است» می‌خواهد | ✅ | S02 |
| 2.2.j2 | تأیید دوباره‌ی کار تمام‌شده → 409 و ستاره‌ی مشتری عوض نشود | ✅ | S02 |
| 2.2.k | PublishJob پیج آژانس: آژانس منتشر می‌کند + لینک؛ مشتری نمی‌تواند | ✅ | S02 |
| 2.2.k2 | کارگری که روی سفارش این مشتری کار نمی‌کند نمی‌تواند پست را «منتشر شد» بزند | ✅ | S02 |
| 2.2.l | پیج دست مشتری: یادآوری سر ساعت (publish-tick) → «منتشر کردم»؛ دیگری Overdue | ✅ | S02 |
| 2.2.m | لغو سفارش: مشتری لغو می‌کند؛ کارها لغو و سهمیه آزاد می‌شود | ✅ | S02 |
| 2.2.n | سهمیه تمام → سفارش «خارج از پکیج» با قیمت پایه؛ خدمت بیرون از پکیج هم خارج از پکیج | ✅ | S02 |
| 2.2.o | ثابت‌ها: هیچ تایمر یا شیفت تکراری؛ خروج = توقف تایمر | ✅ | S02 |

## §2.3 ربات تلگرام

| شناسه | بررسی | نتیجه | سناریو |
|---|---|---|---|
| 2.3.a | webhook refuses unsigned, wrongly signed and stale (replayed) updates | ✅ | S03 |
| 2.3.b | videographer, editor and manager link Telegram with /start <one-time code>; reply goes out through the signed gateway | ✅ | S03 |
| 2.3.b | a used link code is refused (cannot hijack the link from another chat) | ✅ | S03 |
| 2.3.c | videographer sends a 152 MB video → one MediaAsset: sender, size, Telegram time and server upload time | ✅ | S03 |
| 2.3.c | same update delivered twice and the same file re-sent → still one asset; sender told it is a duplicate | ✅ | S03 |
| 2.3.d | archive copy (copyMessage → archive channel) and the 'which work?' question are queued; inbox holders notified in-app | ✅ | S03 |
| 2.3.d | Telegram buttons: sender taps «later» → acknowledged; a worker forging «assign to…» → no permission; a stranger → invalid | ✅ | S03 |
| 2.3.e | files inbox: a worker is refused; manager sees targets with link state | ✅ | S03 |
| 2.3.e | assign errors: unlinked receiver 422, unknown file 404, no receiver 422 | ✅ | S03 |
| 2.3.f | manager assigns the video to the editor for a work item → assigned; copyMessage into the editor's Telegram queued (no phone in caption) | ✅ | S03 |
| 2.3.g | unlinked Telegram user sends a file → 200, nothing stored, one polite 'link your account first' reply | ✅ | S03 |
| 2.3.h | /stop unlinks; a file sent afterwards is treated as from a stranger | ✅ | S03 |
| 2.3.i | invariant: every MediaAsset has a known sender | ✅ | S03 |

## §2.4 تجهیزات

| شناسه | بررسی | نتیجه | سناریو |
|---|---|---|---|
| 2.4.a | manager registers a camera and a tripod (tag = property code, unique) | ✅ | S04 |
| 2.4.b | camera handed to worker 1 with condition, accessories, expected return → pending acceptance; worker notified | ✅ | S04 |
| 2.4.c | hand-over photo (multipart) is attached to the hand-over; a non-image is refused | ✅ | S04 |
| 2.4.d | camera already out → hand over again is 409 (assets.held); version is fresh so it is the business rule | ✅ | S04 |
| 2.4.e | someone else cannot accept worker 1's hand-over (404 for an outsider, nothing changes) | ✅ | S04 |
| 2.4.f | worker 1 accepts → active with acceptance time; manager notified; listed under «پیش من» | ✅ | S04 |
| 2.4.g | cancel: a pending tripod hand-over to worker 3 is cancelled by the manager → back on the shelf; worker cannot cancel; accepted ones cannot be cancelled | ✅ | S04 |
| 2.4.g | decline: worker 3 refuses a tripod hand-over with a note → available again, manager gets an important notice | ✅ | S04 |
| 2.4.g | race: two hand-overs of the tripod at the same moment → exactly one wins (409 for the other) | ✅ | S04 |
| 2.4.h | direct transfer: worker 1 offers the camera to worker 2; worker 3 cannot take it; worker 2 accepts → new active hand-over, no stop at the shelf | ✅ | S04 |
| 2.4.h | transfer offer can be withdrawn (cancel) before it is accepted | ✅ | S04 |
| 2.4.i | late return: QA clock 3 days ahead + asset-alerts → overdue flag and exactly one alert to the holder (re-run same day: none) | ✅ | S04 |
| 2.4.j | worker 2 returns with a damage report + return photo → waiting for the manager (important notice) | ✅ | S04 |
| 2.4.k | manager confirms the return as InRepair → asset off duty, no holder, full append-only history | ✅ | S04 |
| 2.4.l | invariant: at most one live hand-over per asset | ✅ | S04 |

## §2.5 CRM

| شناسه | بررسی | نتیجه | سناریو |
|---|---|---|---|
| 2.5.a | manager creates 100 leads (unassigned); a duplicate phone is refused with 409 | ✅ | S05 |
| 2.5.b | a marketer cannot hand out leads (round-robin / assign → 403) | ✅ | S05 |
| 2.5.c | round-robin of the 100 leads → every marketer in the rotation gets 16 or 17 (difference ≤ 1), all 5 QA marketers included | ✅ | S05 |
| 2.5.d | each marketer lists exactly their own leads | ✅ | S05 |
| 2.5.e | IDOR: marketer A cannot open, update, call, move, follow up or convert marketer B's lead (404, nothing changes) | ✅ | S05 |
| 2.5.f | marketer A calls: no answer (stays «جدید») then interested with a follow-up in 2h → «تماس اول», follow-up set, in «امروز» | ✅ | S05 |
| 2.5.g | every marketer logs a call result on one of their leads (5 marketers in parallel) | ✅ | S05 |
| 2.5.h | QA clock +3h → crm-followups → marketer A gets one follow-up reminder; re-run: none | ✅ | S05 |
| 2.5.i | 3 leads won (2 moved to «برد» first, 1 converted directly) → each becomes a client; managers notified | ✅ | S05 |
| 2.5.j | manager assigns a package to each new client (the «پیشنهاد پکیج»); a marketer cannot | ✅ | S05 |
| 2.5.k | re-assigning a lead from B to A is logged; B loses access (404) | ✅ | S05 |
| 2.5.l | round-robin of explicitly selected leads must not move a won (closed) lead to another marketer (the sale credit would move) | ✅ | S05 |
| 2.5.m | «hand to…» list (/crm/marketers) = the rotation: all QA marketers, not the manager | ✅ | S05 |

## §2.6 پکیج‌ها

| شناسه | بررسی | نتیجه | سناریو |
|---|---|---|---|
| 2.6.a | admin creates a quota-consuming service for the package | ✅ | S06 |
| 2.6.b | invalid package is refused (special ≥ price, discount ends before it starts, no SKU) | ✅ | S06 |
| 2.6.c | admin creates a package with a time-limited discount and a full description | ✅ | S06 |
| 2.6.d | public site shows the package with its special price | ✅ | S06 |
| 2.6.e | discount window ends → effective price back to the regular price (QA clock +3 days) | ✅ | S06 |
| 2.6.f | admin creates a client with a portal login | ✅ | S06 |
| 2.6.g | staff stores the page password in the vault (never returned) | ✅ | S06 |
| 2.6.h | assign the FullAccess package to the client | ✅ | S06 |
| 2.6.i | revealing the password: no permission → 403; wrong code → 422; right code → shown once; reported to the owner | ✅ | S06 |
| 2.6.j | client signs in to the portal and sees package, days left and quota | ✅ | S06 |
| 2.6.k | quota runs out: third order is marked extra (outside the package) | ✅ | S06 |
| 2.6.l | cancel an in-quota order → its slot is free again for the next order | ✅ | S06 |
| 2.6.m | renewal request from the portal: managers told once per day; repeat says 'already asked'; staff can't use it | ✅ | S06 |
| 2.6.n | QA clock day by day + package-expiry twice per day → 7/3/1/last-day/ended each exactly once | ✅ | S06 |
| 2.6.o | after the end: package not active, portal shows no running package, history keeps it | ✅ | S06 |
| 2.6.p | package soft-delete + restore keeps the client's history; manager can cancel an assigned package | ✅ | S06 |

## §2.7 مرخصی

| شناسه | بررسی | نتیجه | سناریو |
|---|---|---|---|
| 2.7.a | leave types are listed (annual 26 days, sick needs a document, mission) | ✅ | S07 |
| 2.7.b | balance before any request: 26 allowed, 0 used | ✅ | S07 |
| 2.7.c | worker asks for a 2-day annual leave → pending, managers notified live | ✅ | S07 |
| 2.7.d | manager approves → approved, worker notified, balance 2 used / 24 left | ✅ | S07 |
| 2.7.e | hourly leave (tomorrow 10–12) → manager rejects with a note; balance unchanged | ✅ | S07 |
| 2.7.f | an approved hourly leave counts as hours/workday in the balance | ✅ | S07 |
| 2.7.g | requester cancels a pending request; cancelling again → 409; someone else can't cancel (403) | ✅ | S07 |
| 2.7.h | admin registers a mission for a field worker (ForUserId) → approved at once, worker notified, no annual balance used | ✅ | S07 |
| 2.7.i | overlapping request (inside the approved 2-day leave) → 409 | ✅ | S07 |
| 2.7.j | validation: short reason, end before start, hourly > 12 h → refused | ✅ | S07 |
| 2.7.k | leave entirely in the past: documented behaviour (accepted as pending, never auto-approved) | ✅ | S07 |
| 2.7.l | a worker can't approve (403) or register leave for someone else (403) | ✅ | S07 |
| 2.7.m | a manager can't approve their own request (403); another lead can; deciding twice → 409 | ✅ | S07 |
| 2.7.n | approved daily leave shows on the team calendar; rejected hourly leave does not | ✅ | S07 |
| 2.7.o | approved requester can still cancel a future approved leave; the balance gives the days back | ✅ | S07 |
| 2.7.q | sick leave without the required document is accepted but marked missingDocument (manager decides) | ✅ | S07 |
| 2.7.p | the absence job does not flag someone on an approved mission (tomorrow 09:30) | ✅ | S07 |
| 2.7.r | leave registered by the admin after an absence was detected resolves it (no unexcused absence next to a paid leave) | ✅ | S07 |

## §2.8 گفتگو

| شناسه | بررسی | نتیجه | سناریو |
|---|---|---|---|
| 2.8.a | manager opens direct chats with 3 workers and writes; each worker's unread goes +1 and the chat is listed | ✅ | S08 |
| 2.8.a | worker reads (unread → 0, manager sees «seen») and replies; resending with the same clientKey is idempotent | ✅ | S08 |
| 2.8.a | worker ↔ worker is off by default: contacts are managers only, direct chat with a colleague → 403; empty message → 422 | ✅ | S08 |
| 2.8.b | manager creates «تیم تدوین» (a client id in the list is dropped); members get the message, an outsider gets 404; a worker cannot create groups | ✅ | S08 |
| 2.8.c | short voice message (12 s) and a photo in the group; members can open them, outsiders 404 | ✅ | S08 |
| 2.8.c | voice longer than 2 minutes, a fake voice file and an executable are refused (422) | ✅ | S08 |
| 2.8.d | «درباره‌ی این کار»: manager opens the order's chat (assignees are members); assignee writes there; an unassigned worker 404; a client 403 | ✅ | S08 |
| 2.8.e | client ↔ admin: client's contacts are managers only; client writes, manager sees unread and answers | ✅ | S08 |
| 2.8.f | privacy: a client cannot reach workers, other clients, groups, order chats, staff direct chats or staff messages (403/404) | ✅ | S08 |
| 2.8.g | mute: worker mutes the manager chat → messages still count as unread but no bell; unmute → bell again | ✅ | S08 |
| 2.8.h | edit within 15 minutes, not after; soft delete hides the text for everyone | ✅ | S08 |
| 2.8.i | setting «clients chat with managers» off → a client can no longer start or continue a chat with the admin (PROMPT2 §7) | ✅ | S08 |

## §2.9 عصر

| شناسه | بررسی | نتیجه | سناریو |
|---|---|---|---|
| 2.9.a | four workers clock in at 08:50 from the office network → green | ✅ | S09 |
| 2.9.b | worker starts the work timer on an assigned card (inside the shift) | ✅ | S09 |
| 2.9.c | break start stops the timer; double start → 409; timer during break → 409; end → 30 min break | ✅ | S09 |
| 2.9.d | early leave at 16:00 with a reason → earlyLeaveMinutes 60 | ✅ | S09 |
| 2.9.e | clock-out at 17:05 stops the running timer; worked = 8h15 − 30 min break | ✅ | S09 |
| 2.9.f | clock-out replay with the same key is a no-op; a new clock-out without a shift → 409 | ✅ | S09 |
| 2.9.g | forgotten clock-out: still open at 20:00 (end + 4 h not reached); closed at 21:30 at the scheduled end, needs review | ✅ | S09 |
| 2.9.h | forgotten clock-out warns the worker and the admin (PROMPT.md §6.6 «هشدار») | ✅ | S09 |
| 2.9.i | invariant hours-match-events holds after the automatic close | ✅ | S09 |
| 2.9.j | correction request validation: nothing given, out before in, future time → refused | ✅ | S09 |
| 2.9.k | worker asks to correct the forgotten clock-out to 18:30 → pending; a worker can't decide (403) | ✅ | S09 |
| 2.9.l | manager approves → shift ends 18:30, review cleared, appended as a Correction event; deciding twice → 409 | ✅ | S09 |
| 2.9.m | a rejected correction leaves the shift unchanged | ✅ | S09 |
| 2.9.n | correction for a whole missed day (yesterday 09:00–17:00) creates the shift on approval | ✅ | S09 |
| 2.9.q | a clock-in-only correction for a past day while the worker is clocked in must not leave two open shifts | ✅ | S09 |
| 2.9.o | invariants after the evening (hours-match-events, one open shift/timer) | ✅ | S09 |
| 2.9.p | live board at night: the evening crew shows 'out' | ✅ | S09 |

## §2.10 گزارش و امتیاز

| شناسه | بررسی | نتیجه | سناریو |
|---|---|---|---|
| 2.10.a | داده‌ی طراحی‌شده: دو پست تکی برای X، کمپین دومرحله‌ای برای Y و Z | ✅ | S10 |
| 2.10.b | روز ۱: X کار ۱ را با تایمر انجام و زود تحویل می‌دهد؛ Y مرحله‌ی ۱ را زود تحویل می‌دهد | ✅ | S10 |
| 2.10.c | مشتری ۳ روز جواب نمی‌دهد (بعد از ددلاین)؛ X کار ۲ را خودش دیر تحویل می‌دهد | ✅ | S10 |
| 2.10.d | مشتری کمپین بعد از ۴ روز مرحله‌ی ۱ را تأیید می‌کند → Z بلافاصله مرحله‌ی ۲ را تحویل می‌دهد | ✅ | S10 |
| 2.10.e | گزارش بازه‌ای کل تیم و هر کارگر (ردیف‌ها، نمودار روزانه، جزئیات) | ✅ | S10 |
| 2.10.f | کارگر فقط گزارش خودش را می‌بیند؛ گزارش دیگری → 404؛ خروجی Excel → 403 | ✅ | S10 |
| 2.10.g | خروجی Excel: فایل واقعی XLSX (PK، نوع، نام فایل) با ردیف کارگرها | ✅ | S10 |
| 2.10.h | امتیازها + «چرا این امتیاز» برای هر نفر؛ کمتر از ۵ نمونه → «داده‌ی کافی نیست» | ✅ | S10 |
| 2.10.i | تأخیر به‌خاطر مشتری (تأیید دیر) روی «به‌موقع» اثر ندارد؛ تأخیر واقعی کارگر اثر دارد | ✅ | S10 |
| 2.10.j | مرحله‌ی بعدی که به‌خاطر سکوت مشتری دیر باز شد نباید امتیاز Z را کم کند | ✅ | S10 |

## §2.11 سایت عمومی

| شناسه | بررسی | نتیجه | سناریو |
|---|---|---|---|
| 2.11.a | visitor: home, portfolio list + one case, packages, blog list + one post, sitemaps | ✅ | S11 |
| 2.11.b | visitor: unknown case / post / package → 404 (no 500) | ✅ | S11 |
| 2.11.c | consultation form validation: no name / bad phone / too long message → 422; honeypot → 202 and nothing stored | ✅ | S11 |
| 2.11.d | consultation form → lead appears in the CRM once (double submit is one lead) | ✅ | S11 |
| 2.11.e | writer uploads an image for the post body (non-image refused) | ✅ | S11 |
| 2.11.f | Markdown is converted to safe HTML (script and javascript: links removed) | ✅ | S11 |
| 2.11.g | writer saves a draft with the image in the middle and submits it for review | ✅ | S11 |
| 2.11.h | writer cannot publish; the post is not public yet | ✅ | S11 |
| 2.11.i | editor schedules publishing for +2h; still hidden until then | ✅ | S11 |
| 2.11.j | cancel: a second scheduled post is unpublished before its time and never goes live | ✅ | S11 |
| 2.11.k | QA clock +3h → blog-schedule → the post is public with the image inside; the cancelled one is not | ✅ | S11 |
| 2.11.l | after publishing the writer can no longer edit it (editor only) | ✅ | S11 |

## §2.12 ادمین

| شناسه | بررسی | نتیجه | سناریو |
|---|---|---|---|
| 2.12.a | users: create, validation, update, soft-delete (gone from list, kept with includeDeleted), restore | ✅ | S12 |
| 2.12.b | services: create, update with version, stale version → 409, delete, restore | ✅ | S12 |
| 2.12.c | packages: update with stale version → 409; delete + restore; export works | ✅ | S12 |
| 2.12.d | clients: create, bad mobile → 422, update, delete, restore | ✅ | S12 |
| 2.12.e | platforms: create, duplicate key → 409, update, delete, restore | ✅ | S12 |
| 2.12.f | leave types: create, bad colour → 422, update/deactivate (no delete/restore endpoint — see findings) | ✅ | S12 |
| 2.12.g | asset categories: create, duplicate → 409, rename/deactivate (no delete/restore endpoint — see findings) | ✅ | S12 |
| 2.12.h | help articles: create, update, delete (no restore endpoint — see findings); writer can't edit | ✅ | S12 |
| 2.12.i | new role with chosen permissions → assigned to a user → next token and /me reflect it | ✅ | S12 |
| 2.12.j | role edited (add cctv.view) and soft-deleted/restored → the member's next token follows each change | ✅ | S12 |
| 2.12.k | SECURITY: a role manager can't raise the permissions of a role they hold themselves | ✅ | S12 |
| 2.12.l | English language: listed as LTR; admin overrides an English text; user switches to English (LTR) and back | ✅ | S12 |
| 2.12.m | PROMPT R13 add a language from the panel: Arabic (RTL) added, then switched off; a writer can't; default can't be switched off | ✅ | S12 |
| 2.12.n | brand colours: invalid → 422; no permission → 403; saved → /public/brand shows them; restored | ✅ | S12 |
| 2.12.o | setup can't be re-run on this install: status says done; POST /setup refused | ✅ | S12 |

## §2.13 دوربین

| شناسه | بررسی | نتیجه | سناریو |
|---|---|---|---|
| 2.13.a | admin sees the camera page status (configured flag + cameras) | ✅ | S13 |
| 2.13.b | add camera: ethics confirmation and RTSP address required; password never returned | ✅ | S13 |
| 2.13.c | no cctv.view (staff writer, client) → 403 on list, snapshot, live, stop; viewer can't manage | ✅ | S13 |
| 2.13.d | snapshot without a working media box → clear 409 (not 500), quickly | ✅ | S13 |
| 2.13.e | live view: not configured → 409; configured → short-lived token for this camera (audited) | ✅ | S13 |
| 2.13.f | idle stop endpoint: stopping a live view is accepted and audited (also for an unknown session); unknown camera → 404 | ✅ | S13 |
| 2.13.g | resync without a media box → 409 (dead box → 409 cctv.unreachable); media-auth hook refuses callers outside the tunnel | ✅ | S13 |
| 2.13.h | deactivate → hidden from viewers and no live; delete → 404 | ✅ | S13 |

## §2.14 راهنما

| شناسه | بررسی | نتیجه | سناریو |
|---|---|---|---|
| 2.14.a | the '؟' article for a page: exact key, deepest prefix, unknown page → 204 | ✅ | S14 |
| 2.14.b | audiences: staff don't get client articles, clients don't get staff articles; the client dashboard has its own | ✅ | S14 |
| 2.14.c | PROMPT2 §13 seed articles exist (ورود، ربات، مرخصی، پکیج، تجهیز، تأیید محتوا، پایان پکیج) | ✅ | S14 |
| 2.14.d | help center search and category filter; drafts visible to editors only; XSS stripped | ✅ | S14 |
| 2.14.e | first-run tour inputs per role (tour is client-side; it reads kind + permissions from /api/v1/me) | ✅ | S14 |
| 2.14.f | every navigation page has a '؟' page text (help.page.* in fa-n12.ts) | ✅ | S14 |
| 2.14.g | automated check: no form field without help text (node tools/check-help.mjs) | ✅ | S14 |

## ثابت‌های دیتابیس

| ثابت | نتیجه |
|---|---|
| one-open-timer-per-user | ✅ |
| one-open-shift-per-user | ✅ |
| one-active-handover-per-asset | ✅ |
| quota-never-negative | ✅ |
| kiosk-qr-used-once | ✅ |
| no-duplicate-expiry-notice | ✅ |
| outbox-not-stuck | ✅ |
| media-has-linked-sender | ✅ |
| hours-match-events | ✅ |

## بخش‌هایی که جای دیگری پوشش داده شده‌اند

| مورد | کجا | توضیح |
|---|---|---|
| ظاهر همه‌ی صفحه‌ها در دسکتاپ، Pixel 7 و ۳۶۰px | `tests/e2e` (۱۱۲ تست تصویری) | `bash tests/e2e/run-visual.sh`؛ عکس‌ها در `tests/e2e/screenshots` |
| اپ اندروید (خروجی استاتیک) | `tests/e2e/specs/app.spec.ts` | ورود، بارگذاری دوباره، باز شدن صفحه‌های اصلی |
| اپ اندروید روی امولاتور/گوشی واقعی | دستی (Android Studio) | ۲۶ مهر ۱۴۰۵ اجرا شد؛ اسکن QR، GPS و دوربین فقط روی گوشی واقعی معنا دارد |
| دستگاه اثر انگشت (ADMS) | `tests/Karnama.IntegrationTests/DeviceTests.cs` | پروتکل iclock، تکرار، تاریخ‌های نامعتبر |
| SMS واقعی و رمز یک‌بارمصرف | `CredentialTests.cs`, `AuthTests.cs` | در QA همه‌ی حساب‌ها نمونه‌اند و کد روی صفحه است |
| خروجی PDF | دستی | PDF همان «چاپ» مرورگر از صفحه‌ی گزارش است (CSS چاپ در `globals.css`) |
| دوربین واقعی (RTSP → MediaMTX) | دستی روی سرور | نیاز به MediaMTX با یک ویدیوی تکراری؛ رفتار بدون سرور در S13 تست شده |
| نصب خالی و `/setup` | `tests/e2e/specs/setup.spec.ts` | نصب خالی جدا روی پورت 3001 |
| کمکِ همه‌ی فیلدها | `node tools/check-help.mjs` (در S14) | فقط کامپوننت‌های فرم مشترک را می‌بیند؛ ورودی‌های ساده (جستجو) استثنا |
