// S11 — public website + blog workflow (docs/PROMPT3.md §2 item 11; PROMPT2.md §8–9 and §18).
// An anonymous visitor reads home, portfolio, blog and packages and sends the consultation form → the lead shows in the CRM.
// A blog writer writes a post with an uploaded image in the middle and Markdown → review → the editor schedules it →
// QA clock + "blog-schedule" job → it is public.
import { deflateSync } from "node:zlib";
import { qa, check, eq, pick, DEMO } from "../lib.mjs";

export const id = "S11";
export const title = "public site: pages, consultation lead, blog write → review → scheduled publish";

const sfx = Date.now().toString(36).slice(-5);
const need = (v, what) => { if (!v) throw new Error(`skipped: prerequisite missing (${what})`); return v; };

/** A real 2×2 PNG (the server sniffs magic bytes). */
function png() {
  const crcTable = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
  const crc = (buf) => { let c = 0xffffffff; for (const b of buf) c = crcTable[(c ^ b) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
  const chunk = (type, data) => {
    const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
    const td = Buffer.concat([Buffer.from(type), data]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td));
    return Buffer.concat([len, td, c]);
  };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(2, 0); ihdr.writeUInt32BE(2, 4); ihdr[8] = 8; ihdr[9] = 2;
  const raw = Buffer.from([0, 255, 0, 0, 0, 255, 0, 0, 0, 0, 255, 255, 255, 0]);
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", ihdr), chunk("IDAT", deflateSync(raw)), chunk("IEND", Buffer.alloc(0))]);
}

const para = (text) => ({ type: "paragraph", content: [{ type: "text", text }] });
const words = (n, w = "محتوا") => Array.from({ length: n }, (_, i) => `${w}${i % 7 === 0 ? " اینستاگرام" : ""}`).join(" ");

export async function run(rec, { crowd }) {
  // Tomorrow 10:00 Tehran: always ahead of real time; tokens follow the QA clock, so sign in after every jump.
  await qa.setClock(qa.tehran(10, 0, 1));
  const writerP = pick(crowd, "writer", 1, 0)[0] ?? { phone: DEMO.writer };
  let admin = await qa.login(DEMO.admin);
  // The reviewer/publisher: the QA manager (blog.publish). Demo 09000000002 is a video editor without blog rights.
  const reviewerP = pick(crowd, "lead", 1, 2)[0];
  let editor = await qa.login(reviewerP);
  let writer = await qa.login(writerP);
  const phone = "0935" + String(Math.floor(1_000_000 + Math.random() * 8_999_999));

  await rec.step("2.11.a", "visitor: home, portfolio list + one case, packages, blog list + one post, sitemaps", async () => {
    const home = await qa.anon("GET", "/api/v1/public/site/home");
    eq(home.status, 200, "home");
    for (const k of ["hero", "services", "cases", "packages"]) check(k in home.data, `home has ${k}`);
    const cases = await qa.anon("GET", "/api/v1/public/site/cases");
    eq(cases.status, 200, "cases"); check(cases.data.length > 0, "at least one case");
    const one = await qa.anon("GET", `/api/v1/public/site/cases/${encodeURIComponent(cases.data[0].slug)}`);
    eq(one.status, 200, "case page"); eq(one.data.card?.slug, cases.data[0].slug, "case slug");
    const pk = await qa.anon("GET", "/api/v1/public/site/packages");
    eq(pk.status, 200, "packages"); check(pk.data.length > 0, "packages listed");
    const pk1 = await qa.anon("GET", `/api/v1/public/site/packages/${encodeURIComponent(pk.data[0].slug)}`);
    eq(pk1.status, 200, "package page");
    const blog = await qa.anon("GET", "/api/v1/public/blog");
    eq(blog.status, 200, "blog list"); check(blog.data.items.length > 0, "posts listed");
    const post = await qa.anon("GET", `/api/v1/public/blog/posts/${encodeURIComponent(blog.data.items[0].slug)}`);
    eq(post.status, 200, "post page"); check(post.data.html?.length > 50, "post has html");
    for (const p of ["/api/v1/public/site/sitemap", "/api/v1/public/blog/sitemap"]) eq((await qa.anon("GET", p)).status, 200, p);
  });

  await rec.step("2.11.b", "visitor: unknown case / post / package → 404 (no 500)", async () => {
    for (const p of ["/api/v1/public/site/cases/no-such-case-qa", "/api/v1/public/blog/posts/no-such-post-qa", "/api/v1/public/site/packages/no-such-package-qa"])
      eq((await qa.anon("GET", p)).status, 404, p);
  });

  await rec.step("2.11.c", "consultation form validation: no name / bad phone / too long message → 422; honeypot → 202 and nothing stored", async () => {
    eq((await qa.anon("POST", "/api/v1/public/site/leads", { name: "", phone, business: null, message: null, source: null, website: null })).status, 422, "empty name");
    eq((await qa.anon("POST", "/api/v1/public/site/leads", { name: "علی", phone: "12345", business: null, message: null, source: null, website: null })).status, 422, "bad phone");
    eq((await qa.anon("POST", "/api/v1/public/site/leads", { name: "علی", phone, business: null, message: "x".repeat(1001), source: null, website: null })).status, 422, "message too long");
    eq((await qa.anon("POST", "/api/v1/public/site/leads", { name: "ربات", phone: "09350000000", business: null, message: null, source: null, website: "http://spam" })).status, 202, "honeypot answers like success");
    const bot = await admin.get(`/api/v1/crm/leads?q=09350000000`);
    eq(bot.items.length, 0, "honeypot lead not stored");
  });

  await rec.step("2.11.d", "consultation form → lead appears in the CRM once (double submit is one lead)", async () => {
    const body = { name: `بازدیدکننده QA ${sfx}`, phone, business: "کافه آزمایشی", message: "برای پیج کافه مشاوره می‌خواهم", source: "/packages", website: "" };
    eq((await qa.anon("POST", "/api/v1/public/site/leads", body)).status, 202, "submitted");
    eq((await qa.anon("POST", "/api/v1/public/site/leads", body)).status, 202, "double tap");
    const leads = await admin.get(`/api/v1/crm/leads?q=${phone}`);
    eq(leads.items.length, 1, "exactly one lead");
    eq(leads.items[0].name, body.name, "lead name");
    const detail = await admin.get(`/api/v1/crm/leads/${leads.items[0].id}`);
    check((detail.message ?? "").includes("مشاوره"), "message kept");
    check(JSON.stringify(detail).includes("فرم سایت"), "origin = website form");
  });

  let image, post, post2;
  image = await rec.step("2.11.e", "writer uploads an image for the post body (non-image refused)", async () => {
    const bad = new FormData(); bad.append("file", new Blob([Buffer.from("<svg onload=alert(1)>")], { type: "image/svg+xml" }), "x.svg");
    await writer.call("POST", "/api/v1/blog/media", undefined, { form: bad, expect: [422] });
    const fd = new FormData(); fd.append("file", new Blob([png()], { type: "image/png" }), "qa.png");
    const up = await writer.call("POST", "/api/v1/blog/media", undefined, { form: fd });
    check(/^\/media\/public\/blog\/[0-9a-f]{32}\.png$/.test(up.url), `own media url: ${up.url}`);
    const r = await fetch(new URL(up.url, process.env.QA_API ?? "http://localhost:5090"));
    eq(r.status, 200, "image served publicly");
    return up;
  });

  await rec.step("2.11.f", "Markdown is converted to safe HTML (script and javascript: links removed)", async () => {
    const r = await writer.post("/api/v1/blog/markdown", { markdown: "## تیتر\n\n**پررنگ** و [لینک](https://example.com) و [بد](javascript:alert(1))\n\n<script>alert(1)</script>" });
    check(r.html.includes("<h2") && r.html.includes("<strong>پررنگ</strong>"), `markdown rendered: ${r.html}`);
    check(!/<script|javascript:/i.test(r.html), `unsafe parts stripped: ${r.html}`);
  });

  const content = () => JSON.stringify({
    type: "doc", content: [
      para(`این یک مطلب آزمایشی QA ${sfx} است. ${words(25)}`),
      { type: "heading", attrs: { level: 2 }, content: [{ type: "text", text: "عکس وسط متن" }] },
      { type: "image", attrs: { src: need(image, "image").url, alt: "عکس آزمایشی", title: "زیرنویس عکس" } },
      para(`ادامه‌ی متن بعد از عکس. ${words(25, "فروش")}`),
      { type: "image", attrs: { src: "https://evil.example/x.png", alt: "external" } },
    ],
  });

  post = await rec.step("2.11.g", "writer saves a draft with the image in the middle and submits it for review", async () => {
    const p = await writer.post("/api/v1/blog/posts", { title: `مطلب آزمایشی QA ${sfx}`, contentJson: content(), categoryId: null, tags: ["qa"], coverImage: null, coverAlt: null, seoTitle: null, metaDescription: null, focusKeyword: null, slug: null, version: null });
    eq(p.status, "draft", "draft");
    check(p.wordCount >= 30, `word count ${p.wordCount}`);
    check(p.seoTitle && p.metaDescription && p.slug, "automatic SEO filled");
    const s = await writer.post(`/api/v1/blog/posts/${p.id}/submit`);
    eq(s.status, "inReview", "in review");
    await writer.refused("POST", `/api/v1/blog/posts/${p.id}/submit`, {}, [409]);
    return s;
  });

  await rec.step("2.11.h", "writer cannot publish; the post is not public yet", async () => {
    need(post, "post");
    await writer.refused("POST", `/api/v1/blog/posts/${post.id}/publish`, { at: null, version: post.version }, [403]);
    eq((await qa.anon("GET", `/api/v1/public/blog/posts/${encodeURIComponent(post.slug)}`)).status, 404, "not public while in review");
  });

  await rec.step("2.11.i", "editor schedules publishing for +2h; still hidden until then", async () => {
    need(post, "post");
    const at = new Date(new Date((await qa.clock()).now).getTime() + 2 * 3600_000).toISOString();
    const s = await editor.post(`/api/v1/blog/posts/${post.id}/publish`, { at, version: post.version });
    eq(s.status, "scheduled", "scheduled");
    eq(await qa.job("blog-schedule") >= 0, true, "job runs");
    eq((await qa.anon("GET", `/api/v1/public/blog/posts/${encodeURIComponent(post.slug)}`)).status, 404, "not public before its time");
    post = s;
  });

  post2 = await rec.step("2.11.j", "cancel: a second scheduled post is unpublished before its time and never goes live", async () => {
    const p = await writer.post("/api/v1/blog/posts", { title: `مطلب لغوشده QA ${sfx}`, contentJson: JSON.stringify({ type: "doc", content: [para(words(40, "لغو"))] }), categoryId: null, tags: [], coverImage: null, coverAlt: null, seoTitle: null, metaDescription: null, focusKeyword: null, slug: null, version: null });
    const at = new Date(new Date((await qa.clock()).now).getTime() + 60 * 60_000).toISOString();
    const s = await editor.post(`/api/v1/blog/posts/${p.id}/publish`, { at, version: p.version });
    eq(s.status, "scheduled", "scheduled");
    const u = await editor.post(`/api/v1/blog/posts/${p.id}/unpublish`);
    eq(u.status, "draft", "back to draft");
    // A post that is too short can't be published at all.
    const short = await writer.post("/api/v1/blog/posts", { title: `کوتاه QA ${sfx}`, contentJson: JSON.stringify({ type: "doc", content: [para("فقط چند کلمه")] }), categoryId: null, tags: [], coverImage: null, coverAlt: null, seoTitle: null, metaDescription: null, focusKeyword: null, slug: null, version: null });
    await editor.refused("POST", `/api/v1/blog/posts/${short.id}/publish`, { at: null, version: short.version }, [422]);
    await writer.del(`/api/v1/blog/posts/${short.id}`); // writer may archive their own draft
    return u;
  });

  await rec.step("2.11.k", "QA clock +3h → blog-schedule → the post is public with the image inside; the cancelled one is not", async () => {
    need(post, "post");
    await qa.advance(180);
    admin = await qa.login(DEMO.admin); editor = await qa.login(reviewerP); writer = await qa.login(writerP);
    const n = await qa.job("blog-schedule");
    check(n >= 1, `job published ${n}`);
    const r = await qa.anon("GET", `/api/v1/public/blog/posts/${encodeURIComponent(post.slug)}`);
    eq(r.status, 200, "public now");
    const html = r.data.html;
    const imgAt = html.indexOf(`src="${image.url}"`);
    check(imgAt > 0, "uploaded image rendered");
    check(html.indexOf("ادامه‌ی متن") > imgAt && html.indexOf("مطلب آزمایشی") < imgAt, "image sits between the two paragraphs");
    check(!html.includes("evil.example"), "foreign image dropped");
    const list = await qa.anon("GET", "/api/v1/public/blog");
    check(list.data.items.some((i) => i.slug === post.slug), "listed on the blog page");
    if (post2) eq((await qa.anon("GET", `/api/v1/public/blog/posts/${encodeURIComponent(post2.slug)}`)).status, 404, "cancelled post stays hidden");
    const again = await qa.job("blog-schedule");
    eq(again, 0, "nothing left to publish");
  });

  await rec.step("2.11.l", "after publishing the writer can no longer edit it (editor only)", async () => {
    need(post, "post");
    const cur = await writer.get(`/api/v1/blog/posts/${post.id}`);
    eq(cur.status, "published", "published");
    await writer.refused("PUT", `/api/v1/blog/posts/${post.id}`, { title: "تغییر", contentJson: cur.contentJson, categoryId: null, tags: [], coverImage: null, coverAlt: null, seoTitle: null, metaDescription: null, focusKeyword: null, slug: null, version: cur.version }, [403]);
  });
}
