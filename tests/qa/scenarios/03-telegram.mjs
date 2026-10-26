// S03 — Telegram files bot through a mock gateway (docs/PROMPT3.md §2.3, PROMPT2.md §8).
// A field videographer links the bot and sends a video → MediaAsset with the upload time → archive copy queued →
// the manager assigns it in the files inbox to an editor → copyMessage to the editor queued in the outbox.
// An unlinked Telegram user sends a file → nothing stored, one polite line back.
//
// Needs the QA API started with the gateway settings (see tests/qa/findings/C.md "How to run"):
//   Messengers__TelegramGatewaySecret=<QA_TG_SECRET> Messengers__TelegramGatewayUrl=http://127.0.0.1:<QA_TG_PORT>/
//   Notify__TelegramArchiveChatId=<QA_TG_ARCHIVE> Notify__TelegramBotUsername=karnama_qa_bot
// This scenario serves the mock gateway (POST /v1/send, /v1/call, /v1/thumb) on QA_TG_PORT while it runs.
// Outbound bot calls are queued in the outbox and only the Worker drains it, so they are checked with read-only SQL
// (QA_DB, default karnama_qa_c).
import { createServer } from "node:http";
import { createHmac, randomInt } from "node:crypto";
import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { qa, pick, check, eq } from "../lib.mjs";

export const id = "S03";
export const title = "ربات تلگرام (Mock Gateway) — فایل فیلمبردار، صندوق فایل‌ها، سپردن به تدوینگر، کاربر وصل‌نشده";

const SECRET = process.env.QA_TG_SECRET ?? "qa-telegram-gateway-secret-0123456789abcdef";
const PORT = Number(process.env.QA_TG_PORT ?? 5193);
const ARCHIVE = Number(process.env.QA_TG_ARCHIVE ?? -1009990001);
const DB = process.env.QA_DB ?? "karnama_qa_c";
const userId = (actor) => JSON.parse(Buffer.from(actor.token.split(".")[1], "base64url").toString()).sub;
const ANY = Array.from({ length: 500 }, (_, i) => i + 100);
/** MediaService.ShortId: base64url of .NET Guid.ToByteArray() (first three groups little-endian). */
const shortId = (guid) => {
  const h = guid.replaceAll("-", "");
  const le = (s) => s.match(/../g).reverse().join("");
  return Buffer.from(le(h.slice(0, 8)) + le(h.slice(8, 12)) + le(h.slice(12, 16)) + h.slice(16), "hex").toString("base64url");
};

/** Read-only SQL against the QA database (outbox rows are not exposed by the API). */
function sql(query) {
  const env = readFileSync(new URL("../../../.env", import.meta.url), "utf8");
  const pw = /^POSTGRES_PASSWORD=(.*)$/m.exec(env)?.[1]?.trim();
  const out = execFileSync("psql", [`postgresql://karnama:${encodeURIComponent(pw)}@localhost:5432/${DB}`, "-At", "-c", query],
    { env: { ...process.env, PGOPTIONS: "-c default_transaction_read_only=on" } }).toString().trim();
  return out ? JSON.parse(out) : [];
}
const outboxLike = (needle) => sql(
  `SELECT COALESCE(json_agg(json_build_object('key', "IdempotencyKey", 'status', "Status", 'payload', "Payload"::json) ORDER BY "Id"), '[]') FROM outbox WHERE "IdempotencyKey" LIKE '${needle.replace(/'/g, "''")}'`);

/** The mock gateway: records what the API sends, checks the HMAC signature, answers like Telegram. */
function startGateway() {
  const calls = [];
  let msg = 9000;
  const server = createServer((req, res) => {
    let body = "";
    req.on("data", (c) => (body += c));
    req.on("end", () => {
      const ts = req.headers["x-karnama-timestamp"];
      const sig = req.headers["x-karnama-signature"];
      const signed = createHmac("sha256", SECRET).update(`${ts}.${body}`).digest("hex") === sig;
      let json = null;
      try { json = JSON.parse(body); } catch { /* ignore */ }
      calls.push({ path: req.url, signed, json });
      res.setHeader("Content-Type", "application/json");
      if (!signed) { res.statusCode = 401; res.end("{}"); return; }
      res.end(JSON.stringify({ ok: true, result: { message_id: ++msg } }));
    });
  });
  return new Promise((resolve, reject) => {
    server.once("error", reject);
    server.listen(PORT, "127.0.0.1", () => resolve({ calls, close: () => new Promise((r) => server.close(r)) }));
  });
}

/** POST a Telegram update the way the gateway forwards it (signed with the QA clock's time). */
async function postUpdate(update, { secret = SECRET, skewSeconds = 0, unsigned = false } = {}) {
  const body = JSON.stringify(update);
  const now = Math.floor(Date.parse((await qa.clock()).now) / 1000) + skewSeconds;
  const headers = unsigned ? {} : { "X-Karnama-Timestamp": String(now), "X-Karnama-Signature": createHmac("sha256", secret).update(`${now}.${body}`).digest("hex") };
  return qa.anon("POST", "/api/v1/bots/telegram", update, { headers });
}

export async function run(rec, { crowd }) {
  const [videoP] = pick(crowd, "field", 1, 0);
  const [editorP] = pick(crowd, "office", 1, 0);
  const [unlinkedStaffP] = pick(crowd, "office", 1, 15);
  const [managerP] = pick(crowd, "lead", 1, 2);
  const run0 = randomInt(1_000_000, 9_000_000);
  let updateSeq = run0 * 100;
  const nextUpdate = () => ++updateSeq;
  const chat = { video: 7_000_000_000 + run0, editor: 7_100_000_000 + run0, manager: 7_200_000_000 + run0, stranger: 7_300_000_000 + run0 };
  const tag = `QA-C-${run0}`;

  let gw;
  try { gw = await startGateway(); }
  catch (e) { rec.steps.push({ id: "2.3.a", title: "mock gateway could not start", ok: false, error: String(e) }); return; }
  try {
    await qa.setClock(qa.tehran(10, 30));
    const video = await qa.login(videoP, { android: true });
    const editor = await qa.login(editorP);
    const manager = await qa.login(managerP);
    const ids = { video: userId(video), editor: userId(editor), manager: userId(manager) };

    const textUpdate = (chatId, text) => ({
      update_id: nextUpdate(),
      message: { message_id: randomInt(1, 1e6), date: Math.floor(Date.parse((new Date()).toISOString()) / 1000), chat: { id: chatId, type: "private" }, from: { id: chatId, first_name: "QA" }, text },
    });

    await rec.step("2.3.a", "webhook refuses unsigned, wrongly signed and stale (replayed) updates", async () => {
      const u = textUpdate(chat.stranger, "سلام");
      eq((await postUpdate(u, { unsigned: true })).status, 401, "unsigned");
      eq((await postUpdate(u, { secret: "wrong-secret-wrong-secret-wrong-secret" })).status, 401, "wrong key");
      eq((await postUpdate(u, { skewSeconds: -600 })).status, 401, "timestamp 10 minutes old");
    });

    const link = async (actor, chatId) => {
      const code = await actor.post("/api/v1/notifications/links/telegram");
      check(code.url.includes(`start=${code.code}`), "deep link carries the one-time code");
      const before = gw.calls.length;
      eq((await postUpdate(textUpdate(chatId, `/start ${code.code}`))).status, 200, "/start accepted");
      return { code: code.code, replies: gw.calls.slice(before) };
    };

    let videoCode;
    await rec.step("2.3.b", "videographer, editor and manager link Telegram with /start <one-time code>; reply goes out through the signed gateway", async () => {
      const r = await link(video, chat.video);
      videoCode = r.code;
      const reply = r.replies.find((c) => c.path === "/v1/send" && c.json?.chatId === chat.video);
      check(reply, `gateway got the link confirmation (calls: ${JSON.stringify(r.replies).slice(0, 300)}) — is the API started with Messengers__TelegramGatewayUrl=http://127.0.0.1:${PORT}/ ?`);
      check(reply.signed, "outbound call is HMAC-signed with the shared key");
      check(reply.json.text.includes("اتصال انجام شد"), `confirmation text (${reply.json.text})`);
      check((await video.get("/api/v1/notifications/prefs")).linked.includes("telegram"), "prefs show telegram linked");
      await link(editor, chat.editor);
      await link(manager, chat.manager);
    });

    await rec.step("2.3.b", "a used link code is refused (cannot hijack the link from another chat)", async () => {
      const before = gw.calls.length;
      await postUpdate(textUpdate(chat.stranger, `/start ${videoCode}`));
      const reply = gw.calls.slice(before).find((c) => c.json?.chatId === chat.stranger);
      check(reply && reply.json.text.includes("معتبر نیست"), `stranger told the code is invalid (${JSON.stringify(reply?.json)})`);
      const targets = await manager.get("/api/v1/media/targets");
      check(targets.staff.find((s) => s.id === ids.video)?.linked, "videographer still linked");
    });

    const sentAt = Math.floor(Date.parse((await qa.clock()).now) / 1000) - 90;
    const videoMessageId = randomInt(1000, 1e6);
    const videoUpdate = {
      update_id: nextUpdate(),
      message: {
        message_id: videoMessageId, date: sentAt, chat: { id: chat.video, type: "private" }, from: { id: chat.video, first_name: "فیلمبردار" },
        caption: `راش کافه ${tag}`,
        video: { file_id: `BAAC-${tag}`, file_unique_id: `u-${tag}`, file_size: 152_000_000, duration: 185, mime_type: "video/mp4", thumbnail: { file_id: `th-${tag}`, file_unique_id: `tu-${tag}` } },
      },
    };
    let asset;
    const findAsset = async (captionPart) => (await manager.get("/api/v1/media?limit=100")).items.filter((m) => m.caption?.includes(captionPart));

    await rec.step("2.3.c", "videographer sends a 152 MB video → one MediaAsset: sender, size, Telegram time and server upload time", async () => {
      const clockNow = Date.parse((await qa.clock()).now);
      eq((await postUpdate(videoUpdate)).status, 200, "webhook");
      const found = await findAsset(tag);
      eq(found.length, 1, "one asset");
      asset = found[0];
      eq(asset.senderId, ids.video, "sender");
      eq(asset.kind, "video", "kind");
      eq(asset.fileSize, 152_000_000, "size (bytes never downloaded, only file_id kept)");
      eq(asset.durationSeconds, 185, "duration");
      eq(Date.parse(asset.sentAt), sentAt * 1000, "sentAt = message.date");
      check(Math.abs(Date.parse(asset.receivedAt) - clockNow) < 10_000, `receivedAt (${asset.receivedAt}) = server (QA) clock at upload (${new Date(clockNow).toISOString()})`);
      eq(asset.status, "received", "status before the outbox runs");
    });

    await rec.step("2.3.c", "same update delivered twice and the same file re-sent → still one asset; sender told it is a duplicate", async () => {
      await postUpdate(videoUpdate);
      const again = structuredClone(videoUpdate);
      again.update_id = nextUpdate();
      again.message.message_id += 1;
      await postUpdate(again);
      eq((await findAsset(tag)).length, 1, "no duplicate MediaAsset");
      const dup = outboxLike(`Telegram:dup:${again.update_id}`);
      eq(dup.length, 1, "duplicate notice queued");
      check(dup[0].payload.args.text.includes("قبلاً"), "duplicate notice text");
    });

    await rec.step("2.3.d", "archive copy (copyMessage → archive channel) and the 'which work?' question are queued; inbox holders notified in-app", async () => {
      check(asset, "asset from 2.3.c");
      const archive = outboxLike(`Telegram:archive:${asset.id.replaceAll("-", "")}`);
      eq(archive.length, 1, "archive copy queued");
      eq(archive[0].payload.method, "copyMessage", "method");
      eq(archive[0].payload.args.chat_id, ARCHIVE, "to the archive channel");
      eq(archive[0].payload.args.from_chat_id, chat.video, "from the videographer chat");
      eq(archive[0].payload.args.message_id, videoMessageId, "message id");
      const ask = outboxLike(`Telegram:ask:${asset.id.replaceAll("-", "")}`);
      eq(ask.length, 1, "question queued");
      check(ask[0].payload.args.reply_markup.inline_keyboard.length >= 1, "buttons (open work + later)");
      const inbox = await manager.get("/api/v1/notifications");
      check(inbox.items.some((n) => n.kind === "media.received" && n.link === "/media"), "manager got media.received");
      // Admin copies are queued only after the archive copy succeeded (MediaService.ApplyResultAsync), i.e. by the Worker.
      const adminCopies = outboxLike(`Telegram:admin:${asset.id.replaceAll("-", "")}:%`);
      eq(adminCopies.length, 0, "admin copies wait for the archive result (Worker)");
    });

    const callback = (chatId, data) => ({ update_id: nextUpdate(), callback_query: { id: `cb${updateSeq}`, from: { id: chatId }, data, message: { message_id: 1, chat: { id: chatId, type: "private" } } } });
    const answerOf = (u) => outboxLike(`Telegram:cb:${u.callback_query.id}`)[0]?.payload.args.text;

    await rec.step("2.3.d", "Telegram buttons: sender taps «later» → acknowledged; a worker forging «assign to…» → no permission; a stranger → invalid", async () => {
      const ask = outboxLike(`Telegram:ask:${asset.id.replaceAll("-", "")}`)[0].payload.args.reply_markup.inline_keyboard;
      const later = ask.flat().find((b) => b.callback_data.startsWith("l|"));
      const u1 = callback(chat.video, later.callback_data);
      await postUpdate(u1);
      check(answerOf(u1)?.includes("بعداً"), `later acknowledged (${answerOf(u1)})`);
      const assignButton = `a|${shortId(asset.id)}`;
      const u2 = callback(chat.editor, assignButton);
      await postUpdate(u2);
      check(answerOf(u2)?.includes("اجازه"), `editor has no permission (${answerOf(u2)})`);
      const u3 = callback(chat.stranger, assignButton);
      await postUpdate(u3);
      check(answerOf(u3)?.includes("معتبر نیست"), `stranger's button invalid (${answerOf(u3)})`);
      eq((await manager.get(`/api/v1/media/${asset.id}`)).assignedToId, null, "still unassigned");
    });

    let work;
    await rec.step("2.3.e", "files inbox: a worker is refused; manager sees targets with link state", async () => {
      eq(await editor.refused("GET", "/api/v1/media", undefined, [403]), 403, "editor cannot open the inbox");
      eq(await editor.refused("POST", `/api/v1/media/${asset.id}/assign`, { toUserId: ids.editor, toClient: false }, [403]), 403, "editor cannot assign");
      const t = await manager.get("/api/v1/media/targets");
      check(t.staff.find((s) => s.id === ids.editor)?.linked === true, "editor shown as linked");
      check(t.staff.find((s) => s.name === unlinkedStaffP.name)?.linked === false, "unlinked colleague shown as not linked");
      work = t.work.find((w) => w.deadline) ?? t.work[0];
      check(work, "an open work item to attach");
    });

    await rec.step("2.3.e", "assign errors: unlinked receiver 422, unknown file 404, no receiver 422", async () => {
      const t = await manager.get("/api/v1/media/targets");
      const unlinked = t.staff.find((s) => s.name === unlinkedStaffP.name);
      const r1 = await manager.call("POST", `/api/v1/media/${asset.id}/assign`, { toUserId: unlinked.id, toClient: false }, { expect: ANY });
      eq(r1.status, 422, `unlinked receiver (${r1.code})`);
      eq(r1.code, "media.notLinked", "code");
      eq(await manager.refused("POST", "/api/v1/media/00000000-0000-0000-0000-00000000c0de/assign", { toUserId: ids.editor, toClient: false }, [404]), 404, "unknown file");
      eq(await manager.refused("POST", `/api/v1/media/${asset.id}/assign`, { toClient: false }, [422]), 422, "no receiver");
      eq((await manager.get(`/api/v1/media/${asset.id}`)).status, "received", "failed attempts change nothing");
    });

    await rec.step("2.3.f", "manager assigns the video to the editor for a work item → assigned; copyMessage into the editor's Telegram queued (no phone in caption)", async () => {
      const r = await manager.post(`/api/v1/media/${asset.id}/assign`, { workItemId: work.id, toUserId: ids.editor, toClient: false });
      eq(r.status, "assigned", "status");
      eq(r.assignedToId, ids.editor, "assigned to");
      eq(r.workItemId, work.id, "work linked");
      check(r.workTitle, "work title shown in the inbox");
      const deliver = outboxLike(`Telegram:deliver:${asset.id.replaceAll("-", "")}:${ids.editor.replaceAll("-", "")}:%`);
      eq(deliver.length, 1, "delivery queued");
      const p = deliver[0].payload;
      eq(p.method, "copyMessage", "copy, never re-upload");
      eq(p.purpose, "deliver", "purpose");
      eq(p.args.chat_id, chat.editor, "to the editor's chat");
      check(p.args.caption.includes("برای کار"), `caption names the work (${p.args.caption})`);
      check(!/09\d{9}|۰۹[۰-۹]{9}/.test(p.args.caption), "no phone number in caption");
      eq(deliver[0].status, 0, "pending in the outbox until the Worker sends it");
      const n = await editor.get("/api/v1/notifications");
      check(n.items.some((x) => x.kind === "media.assigned"), "editor notified in-app");
      const row = (await manager.get("/api/v1/media?open=true&limit=100")).items.find((m) => m.id === asset.id);
      eq(row?.assignedToName, editorP.name, "inbox row shows who has it");
    });

    await rec.step("2.3.g", "unlinked Telegram user sends a file → 200, nothing stored, one polite 'link your account first' reply", async () => {
      const u = {
        update_id: nextUpdate(),
        message: {
          message_id: 77, date: Math.floor(Date.now() / 1000), chat: { id: chat.stranger, type: "private" }, from: { id: chat.stranger, first_name: "ناشناس" },
          caption: `ناشناس ${tag}`, document: { file_id: `doc-${tag}`, file_unique_id: `ud-${tag}`, file_size: 1000, file_name: "x.zip" },
        },
      };
      eq((await postUpdate(u)).status, 200, "quiet 200");
      eq((await findAsset(`ناشناس ${tag}`)).length, 0, "no MediaAsset");
      const reply = outboxLike(`Telegram:unlinked:${u.update_id}`);
      eq(reply.length, 1, "one reply queued");
      eq(reply[0].payload.args.chat_id, chat.stranger, "to that chat");
      check(reply[0].payload.args.text.includes("وصل"), `polite text (${reply[0].payload.args.text})`);
    });

    await rec.step("2.3.h", "/stop unlinks; a file sent afterwards is treated as from a stranger", async () => {
      const before = gw.calls.length;
      await postUpdate(textUpdate(chat.video, "/stop"));
      check(gw.calls.slice(before).some((c) => c.json?.chatId === chat.video && c.json.text.includes("قطع")), "unlink confirmed");
      check(!(await video.get("/api/v1/notifications/prefs")).linked.includes("telegram"), "prefs no longer linked");
      const u = structuredClone(videoUpdate);
      u.update_id = nextUpdate();
      u.message.caption = `بعد از قطع ${tag}`;
      u.message.video.file_unique_id = `u2-${tag}`;
      await postUpdate(u);
      eq((await findAsset(`بعد از قطع ${tag}`)).length, 0, "not stored");
      eq(outboxLike(`Telegram:unlinked:${u.update_id}`).length, 1, "told to link");
    });

    await rec.step("2.3.i", "invariant: every MediaAsset has a known sender", async () => {
      const inv = (await qa.invariants()).find((i) => i.name === "media-has-linked-sender");
      check(inv?.ok, JSON.stringify(inv));
    });
  } finally {
    await gw.close();
  }
}
