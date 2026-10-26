// S08 — chat (docs/PROMPT3.md §2.8, PROMPT2.md §7): manager ↔ workers direct chats, an editing group, a short voice
// message and a photo, a message "about this work" tied to an order, a client ↔ admin conversation. A client never
// sees staff conversations or other clients (403/404); unread counts move; a muted chat stays quiet.
import { randomInt, randomUUID } from "node:crypto";
import { qa, pick, check, eq, DEMO } from "../lib.mjs";

export const id = "S08";
export const title = "گفتگو — مدیر ↔ کارگرها، گروه تدوین، صوت و عکس، «درباره‌ی این کار»، مشتری ↔ ادمین، بی‌صدا";

const userId = (actor) => JSON.parse(Buffer.from(actor.token.split(".")[1], "base64url").toString()).sub;
const ANY = Array.from({ length: 500 }, (_, i) => i + 100);
const PNG = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==", "base64");
const OGG = Buffer.concat([Buffer.from("OggS"), Buffer.alloc(4000, 1)]);
const form = (bytes, name, type) => { const f = new FormData(); f.append("file", new Blob([bytes], { type }), name); return f; };

export async function run(rec, { crowd }) {
  const [managerP] = pick(crowd, "lead", 1, 2);
  const [e1P, e2P, e3P] = pick(crowd, "office", 3, 1);
  const [fieldP] = pick(crowd, "field", 1, 3);
  const [c1P, c2P] = pick(crowd, "client", 2, 10);
  const run0 = randomInt(100000, 999999);
  await qa.setClock(qa.tehran(11, 0));
  let manager, e1, e2, e3, field, c1, c2;
  const login = async () => {
    [manager, e1, e2, e3, field, c1, c2] = await Promise.all([managerP, e1P, e2P, e3P, fieldP, c1P, c2P].map((p) => qa.login(p, { android: p === fieldP })));
  };
  await login();
  const ids = { manager: userId(manager), e1: userId(e1), e2: userId(e2), e3: userId(e3), field: userId(field), c1: userId(c1), c2: userId(c2) };
  const send = (a, conv, body) => a.post(`/api/v1/chat/${conv}/messages`, { body, clientKey: randomUUID() });
  const unread = async (a) => (await a.get("/api/v1/chat/unread")).count;
  const convIn = async (a, convId) => (await a.get("/api/v1/chat")).find((c) => c.id === convId);
  const directs = {};

  await rec.step("2.8.a", "manager opens direct chats with 3 workers and writes; each worker's unread goes +1 and the chat is listed", async () => {
    for (const [k, a] of [["e1", e1], ["e2", e2], ["field", field]]) {
      const before = await unread(a);
      const c = await manager.post("/api/v1/chat/direct", { userId: ids[k] });
      eq(c.kind, "direct", "kind");
      directs[k] = c.id;
      const was = (await convIn(a, c.id))?.unread ?? 0;
      await send(manager, c.id, `سلام، برنامه‌ی امروز ${run0}`);
      eq(await unread(a), before + 1, `${k} unread +1`);
      const listed = await convIn(a, c.id);
      eq(listed?.unread, was + 1, `${k} sees one more unread in the list`);
      eq(listed.otherUserId, ids.manager, "other side is the manager");
    }
    const again = await manager.post("/api/v1/chat/direct", { userId: ids.e1 });
    eq(again.id, directs.e1, "opening the same direct chat again returns the same conversation");
  });

  await rec.step("2.8.a", "worker reads (unread → 0, manager sees «seen») and replies; resending with the same clientKey is idempotent", async () => {
    const page = await e1.get(`/api/v1/chat/${directs.e1}/messages`);
    const last = page.items.at(-1);
    await e1.post(`/api/v1/chat/${directs.e1}/read`, { upTo: last.id });
    eq((await convIn(e1, directs.e1)).unread, 0, "unread cleared");
    eq((await manager.get(`/api/v1/chat/${directs.e1}/messages`)).othersReadUpTo, last.id, "manager sees it was read");
    const key = randomUUID();
    const m1 = await e1.post(`/api/v1/chat/${directs.e1}/messages`, { body: "چشم، ساعت ۲ تحویل می‌دهم", clientKey: key });
    const m2 = await e1.post(`/api/v1/chat/${directs.e1}/messages`, { body: "چشم، ساعت ۲ تحویل می‌دهم", clientKey: key });
    eq(m2.id, m1.id, "same message (double tap)");
    check((await convIn(manager, directs.e1)).unread >= 1, "manager has the reply unread");
    const after = await e1.get(`/api/v1/chat/${directs.e1}/messages?after=${last.id}`);
    eq(after.items.length, 1, "cursor: only messages after the last one");
  });

  await rec.step("2.8.a", "worker ↔ worker is off by default: contacts are managers only, direct chat with a colleague → 403; empty message → 422", async () => {
    const contacts = await e1.get("/api/v1/chat/contacts");
    check(!contacts.some((c) => c.id === ids.e2), "colleague not in contacts");
    check(contacts.some((c) => c.id === ids.manager), "manager in contacts");
    check(!contacts.some((c) => c.isClient), "no clients in a worker's contacts");
    eq(await e1.refused("POST", "/api/v1/chat/direct", { userId: ids.e2 }, [403]), 403, "worker → worker");
    eq(await e1.refused("POST", "/api/v1/chat/direct", { userId: ids.c1 }, [403]), 403, "worker → client");
    const empty = await e1.call("POST", `/api/v1/chat/${directs.e1}/messages`, { body: "   ", clientKey: randomUUID() }, { expect: ANY });
    eq(empty.status, 422, "empty text");
  });

  let group;
  await rec.step("2.8.b", "manager creates «تیم تدوین» (a client id in the list is dropped); members get the message, an outsider gets 404; a worker cannot create groups", async () => {
    group = await manager.post("/api/v1/chat/groups", { title: `تیم تدوین ${run0}`, members: [ids.e1, ids.e2, ids.e3, ids.c1] });
    eq(group.kind, "group", "kind");
    const members = group.members.map((m) => m.id);
    for (const k of ["manager", "e1", "e2", "e3"]) check(members.includes(ids[k]), `${k} is a member`);
    check(!members.includes(ids.c1), "client silently dropped from the group");
    const before = await Promise.all([e1, e2, e3].map(unread));
    await send(manager, group.id, "ریلز کافه تا ساعت ۵ — هر کس بخشی را بردارد");
    const after = await Promise.all([e1, e2, e3].map(unread));
    after.forEach((n, i) => eq(n, before[i] + 1, `member ${i + 1} unread +1`));
    await send(e2, group.id, "من تدوین قسمت اول");
    eq(await field.refused("GET", `/api/v1/chat/${group.id}/messages`, undefined, [404]), 404, "non-member read");
    eq(await field.refused("POST", `/api/v1/chat/${group.id}/messages`, { body: "x", clientKey: randomUUID() }, [404]), 404, "non-member write");
    eq(await e1.refused("POST", "/api/v1/chat/groups", { title: "گروه من", members: [ids.e2] }, [403]), 403, "worker cannot create a group");
  });

  let voice, photo;
  await rec.step("2.8.c", "short voice message (12 s) and a photo in the group; members can open them, outsiders 404", async () => {
    voice = await e3.post(`/api/v1/chat/${group.id}/files?voice=true&duration=12&clientKey=${randomUUID()}`, undefined, { form: form(OGG, "voice.ogg", "audio/ogg") });
    eq(voice.kind, "voice", "voice kind");
    eq(voice.durationSeconds, 12, "duration");
    photo = await manager.post(`/api/v1/chat/${group.id}/files?caption=${encodeURIComponent("کاور پیشنهادی")}&clientKey=${randomUUID()}`, undefined, { form: form(PNG, "cover.png", "image/png") });
    eq(photo.kind, "image", "image kind");
    const r = await fetch(`${process.env.QA_API ?? "http://localhost:5090"}/api/v1/chat/files/${voice.id}`, { headers: { Authorization: `Bearer ${e1.token}` } });
    eq(r.status, 200, "member downloads the voice");
    eq(r.headers.get("content-type"), "audio/ogg", "voice content type");
    eq(await field.refused("GET", `/api/v1/chat/files/${photo.id}`, undefined, [404]), 404, "outsider cannot open the photo");
    eq(await c1.refused("GET", `/api/v1/chat/files/${voice.id}`, undefined, [404]), 404, "client cannot open the voice");
  });

  await rec.step("2.8.c", "voice longer than 2 minutes, a fake voice file and an executable are refused (422)", async () => {
    const long = await e3.call("POST", `/api/v1/chat/${group.id}/files?voice=true&duration=150`, undefined, { form: form(OGG, "long.ogg", "audio/ogg"), expect: ANY });
    eq(long.status, 422, "150 s voice");
    const fake = await e3.call("POST", `/api/v1/chat/${group.id}/files?voice=true&duration=5`, undefined, { form: form(PNG, "v.ogg", "audio/ogg"), expect: ANY });
    eq(fake.status, 422, "png pretending to be voice");
    const exe = await e3.call("POST", `/api/v1/chat/${group.id}/files`, undefined, { form: form(Buffer.from("MZ\x90\x00binary"), "run.exe", "application/octet-stream"), expect: ANY });
    eq(exe.status, 422, "unknown binary");
  });

  let orderConv, assignee, order;
  await rec.step("2.8.d", "«درباره‌ی این کار»: manager opens the order's chat (assignees are members); assignee writes there; an unassigned worker 404; a client 403", async () => {
    // A demo work item with a demo assignee we can sign in as.
    const demoPhones = [DEMO.editor, DEMO.writer, "09000000005", "09000000006"];
    const demo = await Promise.all(demoPhones.map((p) => qa.login(p)));
    const demoIds = demo.map(userId);
    const board = await manager.get("/api/v1/work/board");
    const item = board.find((w) => w.assigneeId && demoIds.includes(w.assigneeId) && w.status !== "done" && w.status !== "cancelled") ?? board.find((w) => w.assigneeId && demoIds.includes(w.assigneeId));
    check(item, "a work item with a demo assignee");
    assignee = demo[demoIds.indexOf(item.assigneeId)];
    order = item.orderId;
    orderConv = await manager.post(`/api/v1/chat/orders/${order}`);
    eq(orderConv.kind, "work", "kind (ConversationKind.Work)");
    eq(orderConv.orderId, order, "tied to the order");
    check(orderConv.title.includes(String(item.orderNumber)), `title carries the order number (${orderConv.title})`);
    check(orderConv.members.some((m) => m.id === item.assigneeId), "assignee is a member");
    eq((await manager.post(`/api/v1/chat/orders/${order}`)).id, orderConv.id, "same conversation the second time");
    await send(manager, orderConv.id, `درباره‌ی این کار: رنگ‌ها را با برند هماهنگ کن ${run0}`);
    const listed = await convIn(assignee, orderConv.id);
    check(listed && listed.unread >= 1, "assignee sees it with unread");
    await send(assignee, orderConv.id, "باشه، اصلاح می‌کنم");
    eq((await assignee.post(`/api/v1/chat/orders/${order}`)).id, orderConv.id, "assignee opens the same chat from the work item");
    eq(await field.refused("POST", `/api/v1/chat/orders/${order}`, undefined, [404]), 404, "unassigned worker");
    eq(await field.refused("GET", `/api/v1/chat/${orderConv.id}/messages`, undefined, [404]), 404, "unassigned worker cannot read");
    eq(await c1.refused("POST", `/api/v1/chat/orders/${order}`, undefined, [403]), 403, "client");
    eq(await manager.refused("POST", "/api/v1/chat/orders/00000000-0000-0000-0000-00000000c0de", undefined, [404]), 404, "unknown order");
  });

  let clientConv;
  await rec.step("2.8.e", "client ↔ admin: client's contacts are managers only; client writes, manager sees unread and answers", async () => {
    const contacts = await c1.get("/api/v1/chat/contacts");
    check(contacts.length > 0 && contacts.some((c) => c.id === ids.manager), "manager in client's contacts");
    check(!contacts.some((c) => [ids.e1, ids.e2, ids.e3, ids.field, ids.c2].includes(c.id)), "no workers or other clients");
    clientConv = await c1.post("/api/v1/chat/direct", { userId: ids.manager });
    const before = await unread(manager);
    await send(c1, clientConv.id, "سلام، پکیجم کی تمام می‌شود؟");
    eq(await unread(manager), before + 1, "manager unread +1");
    const seen = await convIn(manager, clientConv.id);
    eq(seen?.otherIsClient, true, "marked as a client chat");
    await send(manager, clientConv.id, "سلام، ۱۲ روز دیگر. تمدید را از پنل بزنید.");
    check((await convIn(c1, clientConv.id)).unread >= 1, "client sees the answer as unread");
  });

  await rec.step("2.8.f", "privacy: a client cannot reach workers, other clients, groups, order chats, staff direct chats or staff messages (403/404)", async () => {
    eq(await c1.refused("POST", "/api/v1/chat/direct", { userId: ids.e1 }, [403]), 403, "client → worker");
    eq(await c1.refused("POST", "/api/v1/chat/direct", { userId: ids.c2 }, [403]), 403, "client → client");
    eq(await c1.refused("GET", `/api/v1/chat/${group.id}/messages`, undefined, [404]), 404, "group");
    eq(await c1.refused("GET", `/api/v1/chat/${orderConv.id}/messages`, undefined, [404]), 404, "order chat");
    eq(await c1.refused("GET", `/api/v1/chat/${directs.e1}/messages`, undefined, [404]), 404, "manager ↔ worker chat");
    eq(await c1.refused("POST", `/api/v1/chat/${directs.e1}/messages`, { body: "x", clientKey: randomUUID() }, [404]), 404, "write into staff chat");
    eq(await c1.refused("POST", `/api/v1/chat/${directs.e1}/read`, { upTo: 999999 }, [404]), 404, "mark staff chat read");
    eq(await c1.refused("POST", `/api/v1/chat/${group.id}/mute`, { muted: true }, [404]), 404, "mute a group");
    eq(await c1.refused("PUT", `/api/v1/chat/groups/${group.id}`, { title: "x", members: [ids.c1] }, [403]), 403, "edit a group");
    eq(await c2.refused("GET", `/api/v1/chat/${clientConv.id}/messages`, undefined, [404]), 404, "other client cannot read client 1's chat");
    const list = await c1.get("/api/v1/chat");
    check(list.every((c) => c.id === clientConv.id), `client lists only their own chat (${list.map((c) => c.title).join(",")})`);
    const hits = await c1.get(`/api/v1/chat/search?q=${run0}`);
    eq(hits.length, 0, "search finds no staff messages");
    const managerMsgs = await manager.get(`/api/v1/chat/${clientConv.id}/messages`);
    check(managerMsgs.items.some((m) => m.authorId === ids.c1) && managerMsgs.items.some((m) => m.authorId === ids.manager), "manager sees both sides of the client chat");
  });

  await rec.step("2.8.g", "mute: worker mutes the manager chat → messages still count as unread but no bell; unmute → bell again", async () => {
    const bell = async (text) => (await field.get("/api/v1/notifications")).items.some((n) => n.kind === "chat.message" && n.body?.includes(text));
    await field.post(`/api/v1/chat/${directs.field}/mute`, { muted: true });
    eq((await convIn(field, directs.field)).muted, true, "muted flag");
    // A new hour bucket, so the one-bell-per-hour limit is not what keeps it quiet (random extra hours keep re-runs on one DB apart).
    await qa.advance(61 + 60 * (run0 % 24));
    await login();
    const u0 = await unread(field);
    const quiet = `پیام در حالت بی‌صدا ${run0}`;
    await send(manager, directs.field, quiet);
    eq(await unread(field), u0 + 1, "unread still counts");
    eq(await bell(quiet), false, "no bell while muted");
    await field.post(`/api/v1/chat/${directs.field}/mute`, { muted: false });
    eq((await convIn(field, directs.field)).muted, false, "unmuted");
    await qa.advance(61);
    await login();
    const loud = `پیام بعد از برداشتن بی‌صدا ${run0}`;
    await send(manager, directs.field, loud);
    eq(await bell(loud), true, "bell after unmute");
    eq(await e2.refused("POST", `/api/v1/chat/${directs.field}/mute`, { muted: true }, [404]), 404, "cannot mute someone else's chat");
  });

  await rec.step("2.8.h", "edit within 15 minutes, not after; soft delete hides the text for everyone", async () => {
    const m = await send(e1, directs.e1, "متن با غلط تایپی");
    const ed = await e1.put(`/api/v1/chat/messages/${m.id}`, { body: "متن درست" });
    check(ed.editedAt, "edited");
    eq(await manager.refused("PUT", `/api/v1/chat/messages/${m.id}`, { body: "دستکاری" }, [403, 409, 422]) > 0, true, "manager cannot edit the worker's message");
    await qa.advance(16);
    await login();
    const late = await e1.call("PUT", `/api/v1/chat/messages/${m.id}`, { body: "خیلی دیر" }, { expect: ANY });
    check([409, 422].includes(late.status), `edit after 15 min refused (${late.status})`);
    await e1.del(`/api/v1/chat/messages/${m.id}`);
    const seen = (await manager.get(`/api/v1/chat/${directs.e1}/messages`)).items.find((x) => x.id === m.id);
    eq(seen.deleted, true, "deleted");
    eq(seen.body, null, "text hidden");
  });

  await rec.step("2.8.i", "setting «clients chat with managers» off → a client can no longer start or continue a chat with the admin (PROMPT2 §7)", async () => {
    const admin = await qa.login(DEMO.admin);
    const saved = await admin.get("/api/v1/settings/chat");
    await admin.put("/api/v1/settings/chat", { ...saved, clientChat: false });
    try {
      check(!(await manager.get("/api/v1/chat/contacts")).some((c) => c.isClient), "managers no longer see clients as contacts");
      const contacts = await c2.get("/api/v1/chat/contacts");
      const start = await c2.call("POST", "/api/v1/chat/direct", { userId: ids.manager }, { expect: ANY });
      const cont = await c1.call("POST", `/api/v1/chat/${clientConv.id}/messages`, { body: "هنوز می‌توانم بنویسم؟", clientKey: randomUUID() }, { expect: ANY });
      eq(contacts.length, 0, "client has no contacts");
      eq(start.status, 403, "client cannot start a chat");
      check([403, 404].includes(cont.status), `client cannot keep writing in the old chat (${cont.status ?? 200})`);
    } finally {
      await admin.put("/api/v1/settings/chat", saved);
    }
  });
}
