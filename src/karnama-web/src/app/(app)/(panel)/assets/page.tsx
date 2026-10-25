"use client";

import { AlertTriangle, Plus, QrCode, Search } from "lucide-react";
import dynamic from "next/dynamic";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { ErrorText } from "@/components/app/error-text";
import { useRequire } from "@/components/app/use-require";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { faCrud } from "@/lib/i18n/fa-crud";
import { faN3 } from "@/lib/i18n/fa-n3";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { useQuery } from "@/lib/query";
import { hasPermission } from "@/lib/types";
import { useBlobUrl } from "@/lib/use-blob-url";
import { useLive } from "@/lib/use-live";
import { cn } from "@/lib/utils";
import { StatusChip } from "./status-chip";
import { STATUSES, type AssetItem, type AssetStatus, type Category } from "./types";

registerTexts(faN3);
registerTexts(faCrud);

const AssetSheet = dynamic(() => import("./asset-sheet").then((m) => m.AssetSheet), { ssr: false });
const AssetEditor = dynamic(() => import("./asset-editor").then((m) => m.AssetEditor), { ssr: false });
const ScanSheet = dynamic(() => import("./scan-sheet").then((m) => m.ScanSheet), { ssr: false });
const Categories = dynamic(() => import("./categories").then((m) => m.Categories), { ssr: false });

type Tab = "mine" | "all" | "categories";

function Inner() {
  const allowed = useRequire([{ code: "assets.view", scope: "own" }]);
  const { t } = useI18n();
  const { me } = useAuth();
  const router = useRouter();
  const path = usePathname();
  const params = useSearchParams();
  const seesAll = hasPermission(me, "assets.view", "all") || hasPermission(me, "assets.assign", "all") || hasPermission(me, "assets.manage", "all");
  const canManage = hasPermission(me, "assets.manage", "all");
  const tabs: Tab[] = ["mine", ...(seesAll ? (["all"] as Tab[]) : []), ...(canManage ? (["categories"] as Tab[]) : [])];
  const [picked, setTab] = useState<Tab | null>(null);
  const tab = picked ?? (seesAll ? "all" : "mine");
  const [creating, setCreating] = useState(false);
  const [scanning, setScanning] = useState(false);
  useLive(["asset"], [["assets"], ["assets-mine"], ["asset"]]);

  const id = params.get("id");
  const tag = params.get("tag");
  const open = (x: { id?: string; tag?: string } | null) => router.replace(x?.id ? `${path}?id=${x.id}` : x?.tag ? `${path}?tag=${encodeURIComponent(x.tag)}` : path, { scroll: false });
  if (!allowed) return null;

  return (
    <div className="space-y-5">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-extrabold">{t("as.title")}</h1>
          <p className="mt-1 max-w-prose text-muted-foreground">{t("as.subtitle")}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" className="h-12 md:h-11" onClick={() => setScanning(true)} data-testid="as-scan">
            <QrCode aria-hidden="true" />
            {t("as.scan")}
          </Button>
          {canManage && (
            <Button className="h-12 md:h-11" onClick={() => setCreating(true)} data-testid="as-new">
              <Plus aria-hidden="true" />
              {t("as.new")}
            </Button>
          )}
        </div>
      </header>
      {tabs.length > 1 && (
        <div className="flex flex-wrap gap-2" role="tablist">
          {tabs.map((x) => (
            <button key={x} role="tab" aria-selected={tab === x} onClick={() => setTab(x)} data-testid={`as-tab-${x}`}
              className={cn("min-h-11 rounded-full border-2 px-4 text-sm font-semibold", tab === x ? "border-foreground bg-foreground text-background" : "border-border hover:border-foreground")}>
              {t(`as.tab.${x}` as TextKey)}
            </button>
          ))}
        </div>
      )}
      {tab === "mine" && <Mine onOpen={(x) => open({ id: x })} />}
      {tab === "all" && <All onOpen={(x) => open({ id: x })} />}
      {tab === "categories" && <Categories />}

      {(id || tag) && <AssetSheet id={id} tag={tag} onClose={() => open(null)} onOpened={(x) => tag && open({ id: x })} />}
      {creating && <AssetEditor asset={null} onClose={() => setCreating(false)} onSaved={(x) => { setCreating(false); open({ id: x }); }} />}
      {scanning && <ScanSheet onClose={() => setScanning(false)} onTag={(x) => { setScanning(false); open({ tag: x }); }} />}
    </div>
  );
}

export default function AssetsPage() {
  return (
    <Suspense>
      <Inner />
    </Suspense>
  );
}

function Mine({ onOpen }: { onOpen: (id: string) => void }) {
  const { t } = useI18n();
  const q = useQuery({ queryKey: ["assets-mine"], queryFn: () => api<{ holding: AssetItem[]; incoming: AssetItem[] }>("/api/v1/assets/mine") });
  if (q.error) return <ErrorText error={q.error} />;
  if (!q.data) return <div className="h-40 animate-pulse rounded-xl bg-muted" />;
  return (
    <div className="space-y-6" data-testid="as-mine">
      {q.data.incoming.length > 0 && (
        <section className="space-y-2">
          <h2 className="font-bold">{t("as.incoming")}</h2>
          <Grid items={q.data.incoming} onOpen={onOpen} />
        </section>
      )}
      <section className="space-y-2">
        <h2 className="font-bold">{t("as.holding")}</h2>
        {q.data.holding.length === 0 ? <p className="rounded-xl border border-dashed p-6 text-center text-muted-foreground">{t("as.mineEmpty")}</p> : <Grid items={q.data.holding} onOpen={onOpen} />}
      </section>
    </div>
  );
}

function All({ onOpen }: { onOpen: (id: string) => void }) {
  const { t } = useI18n();
  const [q, setQ] = useState("");
  const [status, setStatus] = useState<AssetStatus | "">("");
  const [category, setCategory] = useState("");
  const qs = new URLSearchParams();
  if (q.trim()) qs.set("q", q.trim());
  if (status) qs.set("status", status);
  if (category) qs.set("category", category);
  const list = useQuery({ queryKey: ["assets", qs.toString()], queryFn: () => api<AssetItem[]>(`/api/v1/assets?${qs}`) });
  const cats = useQuery({ queryKey: ["asset-categories"], queryFn: () => api<Category[]>("/api/v1/asset-categories") });
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap gap-2">
        <label className="relative min-w-56 flex-1">
          <span className="sr-only">{t("as.search")}</span>
          <Search aria-hidden="true" className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
          <input type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder={t("as.search")} className="h-11 w-full rounded-lg border bg-background ps-9 pe-3 text-base md:text-sm" data-testid="as-search" />
        </label>
        <select aria-label={t("as.allStatuses")} value={status} onChange={(e) => setStatus(e.target.value as AssetStatus | "")} className="h-11 rounded-lg border bg-background px-3 text-sm" data-testid="as-filter-status">
          <option value="">{t("as.allStatuses")}</option>
          {STATUSES.map((s) => <option key={s} value={s}>{t(`as.status.${s}` as TextKey)}</option>)}
        </select>
        <select aria-label={t("as.allCategories")} value={category} onChange={(e) => setCategory(e.target.value)} className="h-11 rounded-lg border bg-background px-3 text-sm">
          <option value="">{t("as.allCategories")}</option>
          {(cats.data ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </select>
      </div>
      {list.error ? <ErrorText error={list.error} /> : !list.data ? <div className="h-40 animate-pulse rounded-xl bg-muted" /> : list.data.length === 0 ? (
        <p className="rounded-xl border border-dashed p-6 text-center text-muted-foreground">{t("as.empty")}</p>
      ) : (
        <Grid items={list.data} onOpen={onOpen} />
      )}
    </div>
  );
}

function Grid({ items, onOpen }: { items: AssetItem[]; onOpen: (id: string) => void }) {
  return (
    <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3" data-testid="as-list">
      {items.map((a) => <Card key={a.id} a={a} onOpen={() => onOpen(a.id)} />)}
    </ul>
  );
}

function Card({ a, onOpen }: { a: AssetItem; onOpen: () => void }) {
  const { t, formatDate } = useI18n();
  const cover = useBlobUrl(`/api/v1/assets/photos/${a.coverPhotoId}`, !!a.coverPhotoId);
  return (
    <li>
      <button type="button" onClick={onOpen} className="flex w-full gap-3 rounded-xl border bg-card p-3 text-start hover:border-foreground" data-testid={`as-item-${a.tag}`}>
        <span className="grid size-16 shrink-0 place-items-center overflow-hidden rounded-lg bg-muted text-xs text-muted-foreground">
          {/* eslint-disable-next-line @next/next/no-img-element -- private blob */}
          {cover ? <img src={cover} alt="" className="size-full object-cover" /> : a.categoryName}
        </span>
        <span className="min-w-0 flex-1 space-y-1">
          <span className="block truncate font-semibold">{a.name}</span>
          <span className="block text-xs text-muted-foreground" dir="auto">{a.tag}{a.serial ? ` · ${a.serial}` : ""}</span>
          <span className="flex flex-wrap items-center gap-1.5 text-xs">
            <StatusChip a={a} />
            {a.holderName && <span>{t("as.holder", { name: a.holderName })}</span>}
          </span>
          {a.overdue && (
            <span className="flex items-center gap-1 text-xs font-semibold text-destructive">
              <AlertTriangle aria-hidden="true" className="size-3.5" />
              {t("as.overdue")} ({formatDate(a.expectedReturn!, { day: "numeric", month: "long" })})
            </span>
          )}
        </span>
      </button>
    </li>
  );
}
