"use client";

import { useState } from "react";
import { API_BASE } from "@/lib/config";

type Texts = Record<"name" | "phone" | "business" | "message" | "send" | "sending" | "sent" | "error", string>;

/** The only script on the home page: a plain form posting to the API. A hidden "website" field catches bots. */
export function ConsultForm({ texts, source }: { texts: Texts; source: string }) {
  const [state, setState] = useState<"idle" | "sending" | "sent" | "error">("idle");
  const submit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    setState("sending");
    try {
      const r = await fetch(`${API_BASE}/api/v1/public/site/leads`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ name: f.get("name"), phone: f.get("phone"), business: f.get("business"), message: f.get("message"), website: f.get("website"), source }),
      });
      setState(r.ok ? "sent" : "error");
    } catch {
      setState("error");
    }
  };
  if (state === "sent")
    return (
      <p role="status" className="rounded-xl border-2 border-foreground bg-primary p-6 text-lg font-bold text-primary-foreground" data-testid="consult-sent">
        {texts.sent}
      </p>
    );
  const field = "h-12 w-full rounded-md border-2 border-border bg-card px-3 text-base outline-none focus:border-foreground";
  return (
    <form onSubmit={submit} className="grid gap-3 sm:grid-cols-2" data-testid="consult-form">
      <label className="space-y-1 text-sm font-semibold">
        {texts.name}
        <input name="name" required minLength={2} maxLength={60} autoComplete="name" className={field} />
      </label>
      <label className="space-y-1 text-sm font-semibold">
        {texts.phone}
        <input name="phone" required inputMode="tel" dir="ltr" autoComplete="tel" placeholder="09..." className={`${field} text-end`} />
      </label>
      <label className="space-y-1 text-sm font-semibold sm:col-span-2">
        {texts.business}
        <input name="business" maxLength={80} className={field} />
      </label>
      <label className="space-y-1 text-sm font-semibold sm:col-span-2">
        {texts.message}
        <textarea name="message" rows={3} maxLength={1000} className={`${field} h-auto py-2`} />
      </label>
      <input name="website" tabIndex={-1} autoComplete="off" aria-hidden="true" className="absolute -start-[9999px] h-0 w-0 opacity-0" />
      {state === "error" && <p role="alert" className="text-sm font-semibold text-destructive sm:col-span-2">{texts.error}</p>}
      <button type="submit" disabled={state === "sending"} className="h-12 rounded-md border-2 border-foreground bg-primary font-extrabold text-primary-foreground hover:bg-foreground hover:text-background sm:col-span-2">
        {state === "sending" ? texts.sending : texts.send}
      </button>
    </form>
  );
}
