"use client";

import { Pencil, Plus, RefreshCw, Video } from "lucide-react";
import dynamic from "next/dynamic";
import { useEffect, useState } from "react";
import { ErrorText, useErrorMessage } from "@/components/app/error-text";
import { useRequire } from "@/components/app/use-require";
import { Button } from "@/components/ui/button";
import { api, apiBlob } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { faCrud } from "@/lib/i18n/fa-crud";
import { faN11 } from "@/lib/i18n/fa-n11";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { useQuery, useQueryClient } from "@/lib/query";
import { hasPermission } from "@/lib/types";
import { cn } from "@/lib/utils";
import type { Camera, CctvStatus } from "./types";

registerTexts(faN11);
registerTexts(faCrud);

const LiveView = dynamic(() => import("./live-view").then((m) => m.LiveView), { ssr: false });
const CameraEditor = dynamic(() => import("./camera-editor").then((m) => m.CameraEditor), { ssr: false });

const GRIDS = [1, 4, 9] as const;

export default function CctvPage() {
  const allowed = useRequire([{ code: "cctv.view", scope: "all" }, { code: "cctv.manage", scope: "all" }]);
  const { t, formatNumber } = useI18n();
  const { me } = useAuth();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const canView = hasPermission(me, "cctv.view", "all");
  const canManage = hasPermission(me, "cctv.manage", "all");
  const q = useQuery({ queryKey: ["cctv"], enabled: allowed, queryFn: () => api<CctvStatus>("/api/v1/cctv"), refetchInterval: 30_000 });
  const [grid, setGrid] = useState<(typeof GRIDS)[number]>(4);
  const [live, setLive] = useState<Camera | null>(null);
  const [editing, setEditing] = useState<Camera | "new" | null>(null);
  if (!allowed) return null;
  const cams = q.data?.cameras ?? [];

  const resync = async () => {
    try {
      const n = await api<number>("/api/v1/cctv/resync", { method: "POST" });
      notify.success(t("cctv.resynced", { n: formatNumber(n) }));
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };

  return (
    <div className="space-y-5">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-extrabold">{t("cctv.title")}</h1>
          <p className="mt-1 max-w-prose text-muted-foreground">{t("cctv.subtitle")}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <div className="flex rounded-lg border" role="group" aria-label={t("cctv.grid")}>
            {GRIDS.map((g) => (
              <button key={g} type="button" aria-pressed={grid === g} onClick={() => setGrid(g)} className={cn("min-h-11 min-w-11 px-3 text-sm font-semibold", grid === g && "bg-foreground text-background")}>
                {formatNumber(g)}
              </button>
            ))}
          </div>
          {canManage && <Button className="h-12 md:h-11" onClick={() => setEditing("new")} data-testid="cctv-add"><Plus aria-hidden="true" />{t("cctv.add")}</Button>}
          {canManage && q.data?.configured && <Button variant="outline" className="h-12 md:h-11" onClick={resync}><RefreshCw aria-hidden="true" />{t("cctv.resync")}</Button>}
        </div>
      </header>
      {q.data && !q.data.configured && <p className="rounded-xl border border-amber-400 bg-amber-50 p-4 text-amber-950 dark:bg-amber-950 dark:text-amber-100" data-testid="cctv-not-configured">{t("cctv.notConfigured")}</p>}
      {q.error ? <ErrorText error={q.error} /> : null}
      {q.data && cams.length === 0 && <p className="rounded-xl border border-dashed p-6 text-center text-muted-foreground">{t("cctv.empty")}</p>}
      <ul className={cn("grid gap-3", grid === 1 ? "grid-cols-1" : grid === 4 ? "grid-cols-1 sm:grid-cols-2" : "grid-cols-2 lg:grid-cols-3")} data-testid="cctv-grid">
        {cams.map((c) => (
          <li key={c.id} className="overflow-hidden rounded-xl border bg-card">
            <Tile camera={c} enabled={canView && !!q.data?.configured && c.isActive && !live} />
            <div className="flex items-center justify-between gap-2 p-3">
              <div className="min-w-0">
                <p className="truncate font-semibold">{c.name}</p>
                {c.location && <p className="truncate text-xs text-muted-foreground">{c.location}</p>}
              </div>
              <div className="flex shrink-0 items-center gap-1">
                <span className={cn("rounded px-2 py-0.5 text-xs font-semibold", c.online ? "bg-emerald-100 text-emerald-950 dark:bg-emerald-950 dark:text-emerald-100" : "bg-muted")}>
                  {c.online ? t("cctv.online") : t("cctv.offline")}
                </span>
                {canView && q.data?.configured && (
                  <Button variant="outline" className="h-11" onClick={() => setLive(c)} aria-label={`${t("cctv.live")}: ${c.name}`}><Video aria-hidden="true" /></Button>
                )}
                {canManage && <Button variant="ghost" className="h-11" onClick={() => setEditing(c)} aria-label={`${t("cctv.edit")}: ${c.name}`}><Pencil aria-hidden="true" /></Button>}
              </div>
            </div>
          </li>
        ))}
      </ul>
      {live && <LiveView camera={live} onClose={() => setLive(null)} />}
      {editing && <CameraEditor camera={editing === "new" ? null : editing} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); void qc.invalidateQueries({ queryKey: ["cctv"] }); }} />}
    </div>
  );
}

/** Snapshot every 10 s while the page is visible (substream; the server shares one frame between viewers). */
function Tile({ camera, enabled }: { camera: Camera; enabled: boolean }) {
  const { t } = useI18n();
  const [url, setUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    if (!enabled) return;
    let made: string | null = null;
    let stop = false;
    const load = async () => {
      if (document.hidden) return;
      try {
        const b = await apiBlob(`/api/v1/cctv/cameras/${camera.id}/snapshot`);
        if (stop) return;
        const next = URL.createObjectURL(b);
        setUrl((old) => { if (old) URL.revokeObjectURL(old); return next; });
        made = next;
        setFailed(false);
      } catch {
        if (!stop) setFailed(true);
      }
    };
    void load();
    const h = window.setInterval(load, 10_000);
    return () => {
      stop = true;
      window.clearInterval(h);
      if (made) URL.revokeObjectURL(made);
    };
  }, [camera.id, enabled]);
  return (
    <div className="grid aspect-video place-items-center bg-neutral-900 text-sm text-neutral-300">
      {/* eslint-disable-next-line @next/next/no-img-element -- private blob */}
      {url ? <img src={url} alt={camera.name} className="size-full object-cover" /> : failed ? t("cctv.noFrame") : null}
    </div>
  );
}
