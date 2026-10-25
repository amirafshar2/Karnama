"use client";

import { useQuery } from "@/lib/query";
import { Loader2, Send } from "lucide-react";
import { useState } from "react";
import { useErrorMessage } from "@/components/app/error-text";
import { Button } from "@/components/ui/button";
import { faM6 } from "@/lib/i18n/fa-m6";
import { registerTexts } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { FilesPanel } from "@/components/app/files-panel";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { useIsMobile } from "@/hooks/use-mobile";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import { cn } from "@/lib/utils";
import type { OrderDetail } from "@/lib/work-types";

registerTexts(faM6);

export function PortalOrder({ id, onClose }: { id: string; onClose: () => void }) {
  const { t, dir, formatNumber, formatDate } = useI18n();
  const isMobile = useIsMobile();
  const order = useQuery({ queryKey: ["portal-order", id], queryFn: () => api<OrderDetail>(`/api/v1/orders/${id}`) });
  const messages = useQuery({ queryKey: ["portal-messages", id], queryFn: () => api<{ id: string; author: string; fromMe: boolean; body: string; at: string }[]>(`/api/v1/portal/orders/${id}/messages`) });
  const o = order.data;
  const errorMessage = useErrorMessage();
  const [draft, setDraft] = useState("");
  const [sending, setSending] = useState(false);
  const send = async () => {
    if (!draft.trim()) return;
    setSending(true);
    try {
      await api(`/api/v1/portal/orders/${id}/messages`, { method: "POST", body: { body: draft } });
      setDraft("");
      notify.success(t("portalMsg.sent"));
      await messages.refetch();
    } catch (e) {
      notify.error(errorMessage(e));
    } finally {
      setSending(false);
    }
  };
  return (
    <Sheet open onOpenChange={(v) => !v && onClose()}>
      <SheetContent side={isMobile ? "bottom" : dir === "rtl" ? "left" : "right"} className={cn("flex flex-col gap-0 p-0", isMobile ? "h-[90dvh] rounded-t-2xl" : "w-full sm:max-w-xl")}>
        <SheetHeader className="border-b p-5">
          <SheetTitle className="text-xl">{o ? `#${formatNumber(o.number)} — ${o.title}` : t("common.loading")}</SheetTitle>
          {o && <SheetDescription>{o.serviceName}</SheetDescription>}
        </SheetHeader>
        {!o ? (
          <div className="grid flex-1 place-items-center">
            <Loader2 className="animate-spin" aria-label={t("common.loading")} />
          </div>
        ) : (
          <div className="flex-1 space-y-6 overflow-y-auto p-5">
            <dl className="grid gap-x-4 gap-y-2 rounded-lg border p-3 text-sm sm:grid-cols-[auto_1fr]">
              {o.answers.map((a) => (
                <div key={a.key} className="contents">
                  <dt className="text-muted-foreground">{a.label}</dt>
                  <dd className="whitespace-pre-wrap">{Array.isArray(a.value) ? a.value.join("، ") : a.value === null || a.value === undefined ? "—" : String(a.value)}</dd>
                </div>
              ))}
            </dl>
            <section className="space-y-2">
              <h3 className="font-bold">{t("portal.files")}</h3>
              <FilesPanel files={o.files} target={{ orderId: o.id, purpose: "brief" }} canUpload={o.status === "submitted" || o.status === "inProgress"} canShare={false} onChanged={() => void order.refetch()} />
            </section>
            <section className="space-y-2">
              <h3 className="font-bold">{t("portal.messages")}</h3>
              <ul className="space-y-2">
                {(messages.data ?? []).map((m) => (
                  <li key={m.id} className={cn("rounded-lg border p-3 text-sm", m.fromMe && "ms-6 bg-muted/40")}>
                    <p className="mb-1 text-xs text-muted-foreground">
                      {m.author} · {formatDate(m.at, { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" })}
                    </p>
                    <p className="whitespace-pre-wrap">{m.body}</p>
                  </li>
                ))}
                {messages.data?.length === 0 && <li className="text-sm text-muted-foreground">{t("portal.noMessages")}</li>}
              </ul>
              <form
                className="flex items-end gap-2"
                onSubmit={(e) => {
                  e.preventDefault();
                  void send();
                }}
              >
                <label htmlFor="pm" className="sr-only">
                  {t("portalMsg.placeholder")}
                </label>
                <textarea
                  id="pm"
                  rows={2}
                  maxLength={4000}
                  placeholder={t("portalMsg.placeholder")}
                  className="min-h-12 flex-1 rounded-md border bg-transparent p-2 text-base"
                  value={draft}
                  onChange={(e) => setDraft(e.target.value)}
                  data-testid="portal-msg"
                />
                <Button type="submit" className="h-12" disabled={sending || !draft.trim()} data-testid="portal-msg-send">
                  <Send className="rtl:-scale-x-100" aria-hidden="true" />
                  {t("portalMsg.send")}
                </Button>
              </form>
            </section>
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
