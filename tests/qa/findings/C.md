# QA findings C — S03 Telegram bot, S04 equipment, S05 CRM, S08 chat

Scope: docs/PROMPT3.md §2 items 3, 4, 5, 8 (against PROMPT2.md §3, §4, §7, §8). API-level only; no application code was changed.
Scenarios: `tests/qa/scenarios/03-telegram.mjs`, `04-assets.mjs`, `05-crm.mjs`, `08-chat.mjs`.

## How to run

S03 needs the QA API started with the Telegram gateway settings. `qa-api.sh` passes through environment variables that are already set, so:

```bash
Messengers__TelegramGatewaySecret=qa-telegram-gateway-secret-0123456789abcdef \
Messengers__TelegramGatewayUrl=http://127.0.0.1:5193/ \
Notify__TelegramArchiveChatId=-1009990001 \
Notify__TelegramBotUsername=karnama_qa_bot \
bash tests/qa/qa-api.sh 5093 karnama_qa_c </dev/null > /tmp/qa-c.log 2>&1
QA_API=http://localhost:5093 node tests/qa/run.mjs S03 S04 S05 S08
```

- S03 serves the mock gateway itself on `QA_TG_PORT` (default 5193) while it runs. The mock checks the API's HMAC signature and answers like Telegram. Override with `QA_TG_SECRET` / `QA_TG_PORT` / `QA_TG_ARCHIVE`.
- Outbound bot calls go to the `outbox` table. Only `Karnama.Worker` drains that table, and there is no QA job for it, so S03 reads it with **read-only** `psql` (`PGOPTIONS=-c default_transaction_read_only=on`, database `QA_DB`, default `karnama_qa_c`, password from `.env`).
- Redirect stdin and stdout when you start `qa-api.sh` from a tool. If you don't, the `setsid nohup` child keeps the pipe open and the caller hangs.

## Result

Each run used a fresh database (`qa-api.sh` drops and recreates it). Two final fresh runs gave the same result both times:

| Scenario | Steps | Result |
|---|---|---|
| S03 telegram (mock gateway) | 13/13 | PASS |
| S04 equipment | 15/15 | PASS |
| S05 CRM | 11/12 | FAIL: product bug P2 |
| S08 chat | 11/12 | FAIL: product bug P1 |
| DB invariants (all 9) | — | green |

Total: **50/52 steps pass**. The 2 failing steps each document a product bug.
The scenarios also pass when run again on a database they already ran on (random tags, phones, chat ids and update ids, and relative counts). The exception is 2.8.g: if two runs fall in the same random hour bucket, it can collide (about a 1-in-24 chance).
Durations: S03 about 3.6 s, S04 about 2.1 s, S05 about 12.5 s (100 leads), S08 about 3.2 s.

## Coverage

| id | what | result | evidence |
|---|---|---|---|
| 2.3.a | webhook refuses unsigned, wrong-key and stale (−10 min) updates | pass | 401 ×3 |
| 2.3.b | videographer, editor and manager link Telegram with `/start <code>`; the confirmation goes out through the gateway, HMAC-signed | pass | mock got signed `/v1/send` with «اتصال انجام شد»; prefs.linked has telegram |
| 2.3.b | a used link code from another chat is refused | pass | reply «معتبر نیست»; the link is unchanged |
| 2.3.c | a 152 MB video becomes one MediaAsset with sender, kind, size, duration, `sentAt`=message.date and `receivedAt`=QA clock | pass | `GET /media` row |
| 2.3.c | the same update twice, or the same file under a new update id, still gives one asset; the sender gets a duplicate notice | pass | 1 asset; outbox `Telegram:dup:<update>` |
| 2.3.d | archive `copyMessage` (archive chat ← sender chat/message) and the "which work?" keyboard are queued; inbox holders get `media.received` | pass | outbox `Telegram:archive:<id>`, `Telegram:ask:<id>` |
| 2.3.d | Telegram buttons: «later» is acknowledged; a worker who forges «assign to…» gets "no permission"; a stranger gets "invalid" | pass | outbox `Telegram:cb:<id>` answers |
| 2.3.e | worker gets 403 on the inbox and on assign; targets show link state | pass | |
| 2.3.e | assign errors: unlinked receiver gives 422 `media.notLinked`; unknown file gives 404; no receiver gives 422 | pass | |
| 2.3.f | manager assigns to the editor with a work item: status `assigned`, deliver `copyMessage` to the editor's chat, caption names the work and has no phone number, editor gets `media.assigned` | pass | outbox `Telegram:deliver:<id>:<user>:<ts>` (Pending) |
| 2.3.g | unlinked user sends a file: quiet 200, no MediaAsset, one polite "link your account" reply | pass | outbox `Telegram:unlinked:<update>` |
| 2.3.h | `/stop` unlinks; a file sent after that is treated as from a stranger | pass | |
| 2.3.i | invariant media-has-linked-sender | pass | |
| — | **delivery confirmed** (status `delivered` after Telegram answers ok) and **admin copies** | not covered | needs the Worker's OutboxDispatcher; see E1 |
| 2.4.a | create camera and tripod; duplicate tag gives 409; worker cannot create (403) | pass | |
| 2.4.b | hand-over with condition, accessories and expected return gives `pendingAcceptance`; worker gets `assets.handover` | pass | |
| 2.4.c | hand-over photo (multipart, PNG) is linked to the hand-over; fake image gives 422; photo without a hand-over gives 422; holder sees it | pass | |
| 2.4.d | handing over an asset that is already out gives 409 `assets.held` (fresh version) | pass | |
| 2.4.e | outsider accept gives 404; outsider GET asset gives 404; manager accepting for the worker gives 403 | pass | |
| 2.4.f | worker accepts: `active`, `acceptedAt`, manager gets `assets.accepted`, asset is in «پیش من»; double accept gives 409 | pass | |
| 2.4.g | manager cancels a pending hand-over (asset back to `available`); worker cancel gives 403; cancel after accept gives 409 | pass | |
| 2.4.g | worker declines with a note: available again; manager gets an important `assets.declined` | pass | |
| 2.4.g | race: two hand-overs at once give one success and one 409 | pass | |
| 2.4.h | direct transfer w1 → w2: an outsider and the giver cannot accept; w2 accepts and the asset never goes back to the shelf; expected return is carried over; history and notification | pass | |
| 2.4.h | transfer offer withdrawn | pass | |
| 2.4.i | QA clock +3 days, then `asset-alerts`: `overdue=true`, exactly one `assets.overdue` to the holder, managers told who has it, a second run the same day sends nothing | pass | |
| 2.4.j | return with damage report and return photo: `returnSubmitted`; managers get an important notice with the damage; double return gives 409; worker self-confirm gives 403 | pass | |
| 2.4.k | manager confirms as `inRepair`: no holder, append-only history (created, handedOver, accepted, transferOffered, transferred, returnSubmitted, returned); in-repair asset cannot be handed out (409) | pass | |
| 2.4.l | invariant one-active-handover-per-asset | pass | |
| 2.5.a | 100 leads created unassigned; duplicate phone gives 409 `crm.duplicate` | pass | |
| 2.5.b | marketer round-robin or assign gives 403 | pass | |
| 2.5.c | round-robin of the 100: difference ≤ 1 between marketers, all 5 QA marketers get ≥ 16 | pass | 16–17 each over **6** marketers in the rotation (see A1) |
| 2.5.d | each marketer lists exactly their own leads | pass | |
| 2.5.e | IDOR: A on B's lead gives 404 for GET, PUT, calls, activities, move, follow-up and convert; search by B's phone finds nothing; the duplicate message does not leak B's lead name; B's lead is unchanged | pass | |
| 2.5.f | call «no answer» keeps the stage; «interested» plus a follow-up moves the lead to «تماس اول» and puts it in «امروز»; message without channel gives 422 | pass | |
| 2.5.g | 5 marketers log calls in parallel; the report counts them | pass | |
| 2.5.h | QA clock +3 h, then `crm-followups`: exactly one `crm.followUp` to the owner, no repeat, B not told | pass | |
| 2.5.i | 3 wins (2 moved to «برد» first, 1 converted directly) become clients, lead linked, stage won, `converted` activity, managers get `crm.won`; converting again is idempotent | pass | |
| 2.5.j | manager assigns a package to each new client; marketer gets 403; report credits the sale | pass | a package had to be created first (see E2) |
| 2.5.k | reassign B → A is logged; B now gets 404; assigning to a non-marketer gives 422 | pass | |
| 2.5.l | round-robin of selected ids must not move a won lead | **FAIL** | P2 |
| 2.8.a | manager ↔ 3 workers direct chats: unread +1 for each, listed, same conversation on reopen | pass | |
| 2.8.a | read clears unread; manager sees `othersReadUpTo`; idempotent clientKey; cursor `after` | pass | |
| 2.8.a | worker ↔ worker off by default (403); worker → client gives 403; empty message gives 422 | pass | |
| 2.8.b | editing group: a client id passed in members is dropped; members' unread +1; outsider read/write gives 404; worker cannot create a group (403) | pass | |
| 2.8.c | voice 12 s (OGG) has kind voice and duration; photo has kind image; member download is 200 `audio/ogg`; outsider and client get 404 | pass | |
| 2.8.c | voice 150 s, PNG posing as voice, and `.exe` all give 422 | pass | |
| 2.8.d | «درباره‌ی این کار»: `POST /chat/orders/{id}` gives kind `work` with the assignee as a member and the same conversation on reopen; assignee writes; unassigned worker gets 404; client gets 403; unknown order gets 404 | pass | |
| 2.8.e | client ↔ admin: client contacts are managers only; manager unread +1, `otherIsClient`; reply is unread for the client | pass | |
| 2.8.f | client gets 403/404 for worker, other client, group, order chat, staff direct chat (read, write, read-mark), mute and group edit; other client cannot read; list and search show nothing from staff | pass | |
| 2.8.g | mute: unread still counts, no bell; unmute: bell again; muting someone else's chat gives 404 | pass | |
| 2.8.h | edit within 15 min works, after 16 min is refused; other people cannot edit; soft delete hides the text | pass | |
| 2.8.i | chat setting `clientChat=false` must block clients | **FAIL** | P1 |

## Product bugs

### P1: turning off "clients chat with managers" does not stop clients (medium, privacy/settings)
PROMPT2 §7 says the manager talks to "any user (… and **if enabled**, the client)". Settings → chat → `clientChat` is shown in the panel (`new-chat.tsx:128`), but it only hides clients from the managers' contact list.
Repro (S08 step 2.8.i, or by hand):
1. As 09000000001: `PUT /api/v1/settings/chat {"workerToWorker":false,"clientChat":false}`
2. As any client: `GET /api/v1/chat/contacts` returns 6 managers. `POST /api/v1/chat/direct {"userId":<manager>}` returns **200** and a conversation. `POST /api/v1/chat/{id}/messages` returns **200**.

Expected: 0 contacts, and 403 on start (and on sending in existing client chats).
Where: `src/Karnama.Application/Chat/ChatService.cs:89`. The `IsClient` branch of `ContactsAsync` ignores `settings.ClientChat`; only `:96` (the manager branch) uses it. `SendTextAsync`/`UploadAsync` (via `MineTrackedAsync`, `:401`) never check the setting for existing client conversations.

### P2: round-robin with explicit `leadIds` moves won/closed leads, and so moves the sale credit (medium, data integrity)
`POST /api/v1/crm/round-robin {"leadIds":[<won lead>]}` gives the won lead (already converted to a client) to the next marketer in turn. The CRM report credits sales by `Lead.OwnerId`, so the sale and commission silently move to someone else. Leads already owned by other marketers are also reshuffled without any warning.
Repro: S05 step 2.5.l (fails on every fresh run). By hand: convert a lead as marketer A, then as the manager call round-robin twice with that id. The owner changes.
Where: `src/Karnama.Application/Crm/CrmService.cs:319-320`. When ids are given there is no `openStages.Contains(l.StageId)` filter and no `OwnerId == null` filter; only the no-ids branch has them. Expected: skip closed leads (and probably already-owned ones, or require an explicit "reassign").

### P3: media inbox "linked" flag ignores the messenger (low)
`GET /api/v1/media/targets` marks a person `linked:true` if they have **any** messenger link. `src/Karnama.Application/Media/MediaService.cs:196` (`db.MessengerLinks.Select(l => l.UserId)` has no `Messenger` filter). Someone linked only on Bale shows as reachable for a Telegram file, and assigning then fails with 422 `media.notLinked`. Found by code review; not reproducible in QA because Bale is not configured there.

### P4: media can be assigned to any active user, including another business's client (low, hardening)
`POST /api/v1/media/{id}/assign {"toUserId":<client portal user>,"toClient":false}` is accepted for any active user (`MediaService.cs:242-244`; no `Kind == Staff` check). An admin mis-click could send client X's raw footage to client Y's Telegram. Suggestion: for `toClient:false` accept staff only. For clients, require `toClient:true`, which already resolves the right client from the work.

### P5: API inconsistency: asset photo `stage` query value is case-sensitive (low)
The API returns `"stage":"handOver"` and `"profile"` (camelCase). Sending the same value back gives `POST /api/v1/assets/{id}/photos?stage=profile` → **400 errors.badRequest**; only `Profile`/`HandOver`/`Return` work. The web maps the values itself (`assets/photos.tsx` `STAGE[...]`), so the UI works, but the Android app or third-party clients that echo API values will fail. Where: `src/Karnama.Api/Endpoints/AssetEndpoints.cs:24` (enum bound from query without a case-insensitive converter).

### P6: «/crm/marketers» lists owners and managers as marketers (low, UX)
`GET /api/v1/crm/marketers` returns everyone with `crm.leads` at any scope (owners, managers, demo admin). The "hand to…" picker therefore offers non-marketers. `CrmService.cs:173`. The round-robin rotation itself correctly excludes them (`NextInTurnAsync`).

### Observations (not bugs, mismatches with PROMPT2 wording)
- PROMPT2 §8.3 step 6 says the copy to admins goes out "at the same moment". The code queues admin copies only after the archive copy succeeds (`MediaService.cs:297`, `ApplyResultAsync`). That is reasonable (copy from the stable archive), but if the archive copy keeps retrying, admins wait.
- `ParsedUpdate` stamps callback updates (and messages without `date`) with `DateTimeOffset.UtcNow` instead of the injected clock (`src/Karnama.Application/Media/BotUpdate.cs:34,41`). This does not matter in production, but it is inconsistent under the QA clock.
- A file whose `file_unique_id` is already known is refused for **every** sender (`MediaService.cs` duplicate check is per messenger, not per sender). If two people forward the same clip, the second one is told "already registered" and nothing records that they sent it.

## Environment / QA-tool gaps (not product bugs)
- **E1: no QA job drains the outbox.** `QaService.Jobs` (`src/Karnama.Application/Qa/QaService.cs:23`) has no `outbox` entry, and the API never hosts `OutboxDispatcher` (Worker only). So from the QA API you cannot verify archive → admin copies → `sentToAdmins`, `delivered`, `failed` (blocked bot), the `media.failed` notice, or any Telegram/Bale/SMS notification actually leaving. Suggest adding `"outbox"` → `OutboxDispatcher.DispatchOnceAsync` (S03 already serves a mock gateway that answers like Telegram).
- **E2: the QA crowd seed over-fills every package.** `src/Karnama.Infrastructure/Qa/QaCrowdSeed.cs:73,86` assigns packages without checking `Capacity`. After seeding: طلایی 14/10, نقره‌ای 14/10, برنزی 28/10. So in a QA database **no seeded package can be assigned** (`409 clientPackages.full`). This also affects S06 (packages). S05 creates its own uncapped package when none has room.
- **E3: `qa-api.sh` does not configure Telegram** (gateway URL/secret, archive chat, bot username). Without them, `/api/v1/bots/telegram` is always 401 and `/notifications/links/telegram` is 422 `messenger.off`. Pass them as environment variables (see "How to run").

## Scenario assumptions that were wrong (fixed in the scenarios)
- A1: "distribute among the 5 QA marketers": the rotation is every holder of `crm.leads:Own` without `:All`. That includes the demo marketer «نیما» (09000000008), so it has 6 people and there is no API to restrict a round-robin to a subset. The scenario checks an even split over the rotation, with all 5 QA marketers included.
- A2: the order chat kind is `work` (`ConversationKind.Work`), not `order`.
- A3: a chat bell is created at most once per conversation per person per hour (event key `chat:<conv>:<user>:<hour>`). The mute test therefore moves the QA clock into a new hour before each check.
- A4: `POST /api/v1/packages` returns `nameValues`, not `name` (the list endpoint returns `name`).
