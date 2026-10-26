"use client";

import { useQuery, useQueryClient } from "@/lib/query";
import { ExternalLink, RotateCcw, Search } from "lucide-react";
import { useMemo, useState } from "react";
import { ErrorText, useErrorMessage } from "@/components/app/error-text";
import { useRequire } from "@/components/app/use-require";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { faSettings } from "@/lib/i18n/fa-settings";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import dynamic from "next/dynamic";
import { useAuth } from "@/lib/auth";
import { hasPermission } from "@/lib/types";
import { OTHER_SECTION, TEXT_SECTIONS, sectionOf, textKind } from "@/lib/i18n/text-guide";

// Language management is for the few who hold languages.manage; loaded only for them.
const LanguagesCard = dynamic(() => import("@/components/app/languages-card").then((m) => m.LanguagesCard), { ssr: false });

registerTexts(faSettings);


export default function TextsPage() {
  const allowed = useRequire([
    { code: "languages.manage", scope: "all" },
    { code: "content.manage", scope: "all" },
  ]);
  const { t, languages, lang: uiLang } = useI18n();
  const { me } = useAuth();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const [lang, setLang] = useState(uiLang);
  const [q, setQ] = useState("");
  const [onlyChanged, setOnlyChanged] = useState(false);
  const [section, setSection] = useState("");

  const overrides = useQuery({
    queryKey: ["text-overrides", lang],
    queryFn: () => api<{ key: string; value: string }[]>(`/api/v1/translations/${lang}`),
    enabled: allowed,
  });
  const builtIn = useQuery({
    queryKey: ["text-defaults", lang],
    queryFn: async (): Promise<{ fa: Record<string, string>; local: Record<string, string> }> => {
      // Loaded on demand so the page itself stays inside the bandwidth budget.
      const { ALL_FA } = await import("@/lib/i18n/all-fa");
      return { fa: ALL_FA, local: lang === "en" ? { ...ALL_FA, ...(await import("@/lib/i18n/en")).en } : ALL_FA };
    },
    staleTime: Infinity,
    enabled: allowed,
  });

  const rows = useMemo(() => {
    const map = new Map((overrides.data ?? []).map((o) => [o.key, o.value]));
    const needle = q.trim().toLowerCase();
    const fa = builtIn.data?.fa ?? {};
    return Object.keys(fa)
      .sort()
      .map((key) => ({ key, def: builtIn.data?.local[key] ?? fa[key], value: map.get(key) }))
      .filter((r) => !section || sectionOf(r.key) === section)
      .filter((r) => !onlyChanged || r.value !== undefined)
      .filter((r) => !needle || r.key.toLowerCase().includes(needle) || r.def.toLowerCase().includes(needle) || (r.value ?? "").toLowerCase().includes(needle));
  }, [overrides.data, builtIn.data, q, onlyChanged, section]);

  // Rows grouped by the part of the program they belong to, in the order of the guide (menu order), unknown ones last.
  const order = Object.keys(TEXT_SECTIONS);
  const groups = useMemo(() => {
    const by = new Map<string, typeof rows>();
    for (const r of rows) {
      const k = TEXT_SECTIONS[sectionOf(r.key)] ? sectionOf(r.key) : "";
      by.set(k, [...(by.get(k) ?? []), r]);
    }
    return [...by.entries()].sort((a, b) => (a[0] ? order.indexOf(a[0]) : 999) - (b[0] ? order.indexOf(b[0]) : 999));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rows]);
  const sectionChoices = useMemo(() => {
    const present = new Set(Object.keys(builtIn.data?.fa ?? {}).map(sectionOf));
    return order.filter((k) => present.has(k)).map((k) => ({ key: k, name: TEXT_SECTIONS[k].name }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [builtIn.data]);
  const openAll = !!section || q.trim().length > 1 || onlyChanged;

  if (!allowed) return null;

  const refresh = async () => {
    await qc.invalidateQueries({ queryKey: ["text-overrides", lang] });
    // The UI picks up the new text on its next bundle fetch; reload it now for the current language.
    if (lang === uiLang) window.dispatchEvent(new Event("karnama:texts-changed"));
  };

  const save = async (key: string, value: string, previous: string | undefined, def: string) => {
    if ((previous ?? def) === value) return;
    try {
      if (value.trim() === "" || value === def) await api(`/api/v1/translations/${lang}/${encodeURIComponent(key)}`, { method: "DELETE" });
      else await api(`/api/v1/translations/${lang}/${encodeURIComponent(key)}`, { method: "PUT", body: { value } });
      notify.success(t("texts.saved"));
      await refresh();
    } catch (err) {
      notify.error(errorMessage(err));
    }
  };

  const reset = async (key: string) => {
    try {
      await api(`/api/v1/translations/${lang}/${encodeURIComponent(key)}`, { method: "DELETE" });
      notify.success(t("texts.resetDone"));
      await refresh();
    } catch (err) {
      notify.error(errorMessage(err));
    }
  };

  const current = languages.find((l) => l.code === lang);

  return (
    <div className="space-y-6">
      <header>
        <h1 className="text-2xl font-extrabold">{t("texts.title")}</h1>
        <p className="mt-1 max-w-prose text-muted-foreground">{t("texts.subtitle")}</p>
        <p className="mt-2 max-w-prose text-sm text-muted-foreground">{t("texts.help")}</p>
      </header>

      {hasPermission(me, "languages.manage", "all") && <LanguagesCard />}

      <div className="flex flex-wrap items-end gap-4">
        <label className="flex flex-col gap-1 text-sm font-medium">
          {t("texts.language")}
          <select
            className="h-12 rounded-md border border-input bg-card px-3 text-base md:h-11 md:text-sm"
            value={lang}
            onChange={(e) => setLang(e.target.value)}
            data-testid="texts-lang"
          >
            {languages.map((l) => (
              <option key={l.code} value={l.code}>
                {l.name}
              </option>
            ))}
          </select>
        </label>
        <label className="flex flex-col gap-1 text-sm font-medium">
          {t("texts.section")}
          <select
            className="h-12 max-w-full rounded-md border border-input bg-card px-3 text-base md:h-11 md:text-sm"
            value={section}
            onChange={(e) => setSection(e.target.value)}
            data-testid="texts-section"
          >
            <option value="">{t("texts.allSections")}</option>
            {sectionChoices.map((c) => (
              <option key={c.key} value={c.key}>
                {c.name}
              </option>
            ))}
          </select>
        </label>
        <div className="relative w-full sm:max-w-xs">
          <Search className="pointer-events-none absolute top-1/2 start-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
          <input
            type="search"
            className="h-12 w-full rounded-md border border-input bg-card ps-9 pe-3 text-base outline-none focus-visible:ring-2 focus-visible:ring-ring md:h-11 md:text-sm"
            placeholder={t("texts.search")}
            aria-label={t("texts.search")}
            value={q}
            onChange={(e) => setQ(e.target.value)}
            data-testid="texts-search"
          />
        </div>
        <label className="flex min-h-12 cursor-pointer items-center gap-2 text-sm font-medium">
          <input type="checkbox" checked={onlyChanged} onChange={(e) => setOnlyChanged(e.target.checked)} className="size-5 accent-[var(--foreground)]" />
          {t("texts.onlyChanged")}
        </label>
        <span className="text-sm text-muted-foreground">{t("texts.count", { count: rows.length })}</span>
      </div>

      <ErrorText error={overrides.error} />

      <div className="space-y-3">
        {groups.map(([key, list]) => {
          const g = TEXT_SECTIONS[key] ?? OTHER_SECTION;
          return (
            <details key={key || "other"} open={openAll} className="group rounded-xl border bg-card" data-testid={`text-section-${key || "other"}`}>
              <summary className="flex cursor-pointer list-none flex-wrap items-center justify-between gap-2 p-4 [&::-webkit-details-marker]:hidden">
                <span className="min-w-0">
                  <span className="block text-base font-bold">{g.name}</span>
                  <span className="mt-1 block text-sm text-muted-foreground">
                    <b className="font-semibold text-foreground">{t("texts.where")}</b> {g.where}
                  </span>
                  <span className="mt-0.5 block text-xs text-muted-foreground">
                    {t("texts.who")} {g.who} · {t("texts.count", { count: list.length })}
                  </span>
                </span>
                {g.href && (
                  <a href={g.href} target="_blank" rel="noopener" className="inline-flex min-h-10 items-center gap-1 rounded-md px-2 text-sm font-semibold underline underline-offset-4 hover:bg-muted" onClick={(e) => e.stopPropagation()}>
                    {t("texts.open")}
                    <ExternalLink className="size-4" aria-hidden="true" />
                  </a>
                )}
              </summary>
              <ul className="divide-y border-t">
                {list.map((r) => {
                  const kind = textKind(r.key);
                  return (
                    <li key={`${lang}-${r.key}-${r.value ?? ""}`} className="grid gap-2 p-4 md:grid-cols-[minmax(0,1fr)_auto] md:items-start" data-testid={`text-row-${r.key}`}>
                      <div className="min-w-0 space-y-1">
                        {(kind || r.value !== undefined) && (
                          <p className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                            {kind && (
                              <span>
                                {t("texts.kind")} {kind}
                              </span>
                            )}
                            {r.value !== undefined && <span className="rounded bg-primary px-1.5 text-primary-foreground">{t("texts.changed")}</span>}
                          </p>
                        )}
                        <textarea
                          dir={current?.direction ?? "auto"}
                          rows={r.def.length > 70 ? 3 : 1}
                          defaultValue={r.value ?? r.def}
                          aria-label={kind ? `${g.name} — ${kind}` : g.name}
                          data-testid={`text-input-${r.key}`}
                          onBlur={(e) => void save(r.key, e.target.value, r.value, r.def)}
                          className="min-h-12 w-full resize-y rounded-md border border-input bg-background px-3 py-2.5 text-base outline-none focus-visible:ring-2 focus-visible:ring-ring md:min-h-10 md:text-sm"
                        />
                        {/\{\w+\}/.test(r.def) && <p className="text-xs text-muted-foreground">{t("texts.keepBraces")}</p>}
                        {r.value !== undefined && (
                          <p className="text-xs text-muted-foreground">
                            {t("texts.default")}: {r.def}
                          </p>
                        )}
                        <p className="text-[11px] text-muted-foreground/70">
                          {t("texts.techKey")}: <code dir="ltr">{r.key}</code>
                        </p>
                      </div>
                      {r.value !== undefined && (
                        <Button type="button" variant="ghost" className="h-12 justify-self-start md:h-10" onClick={() => reset(r.key as TextKey)}>
                          <RotateCcw aria-hidden="true" />
                          {t("texts.reset")}
                        </Button>
                      )}
                    </li>
                  );
                })}
              </ul>
            </details>
          );
        })}
      </div>
    </div>
  );
}
