"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { RotateCcw, Search } from "lucide-react";
import { useMemo, useState } from "react";
import { ErrorText, useErrorMessage } from "@/components/app/error-text";
import { useRequire } from "@/components/app/use-require";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { faCore } from "@/lib/i18n/fa";
import { faAccess } from "@/lib/i18n/fa-access";
import { faRoles } from "@/lib/i18n/fa-roles";
import { faSettings } from "@/lib/i18n/fa-settings";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";

registerTexts(faSettings);

/** Every built-in Persian text, i.e. the full list of editable keys. */
const ALL_FA: Record<string, string> = { ...faCore, ...faAccess, ...faRoles, ...faSettings };

export default function TextsPage() {
  const allowed = useRequire([
    { code: "languages.manage", scope: "all" },
    { code: "content.manage", scope: "all" },
  ]);
  const { t, languages, lang: uiLang } = useI18n();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const [lang, setLang] = useState(uiLang);
  const [q, setQ] = useState("");
  const [onlyChanged, setOnlyChanged] = useState(false);

  const overrides = useQuery({
    queryKey: ["text-overrides", lang],
    queryFn: () => api<{ key: string; value: string }[]>(`/api/v1/translations/${lang}`),
    enabled: allowed,
  });
  const builtIn = useQuery({
    queryKey: ["text-defaults", lang],
    queryFn: async (): Promise<Record<string, string>> =>
      lang === "fa" ? ALL_FA : lang === "en" ? { ...ALL_FA, ...(await import("@/lib/i18n/en")).en } : ALL_FA,
    staleTime: Infinity,
    enabled: allowed,
  });

  const rows = useMemo(() => {
    const map = new Map((overrides.data ?? []).map((o) => [o.key, o.value]));
    const needle = q.trim().toLowerCase();
    return Object.keys(ALL_FA)
      .sort()
      .map((key) => ({ key, def: builtIn.data?.[key] ?? ALL_FA[key], value: map.get(key) }))
      .filter((r) => !onlyChanged || r.value !== undefined)
      .filter((r) => !needle || r.key.toLowerCase().includes(needle) || r.def.toLowerCase().includes(needle) || (r.value ?? "").toLowerCase().includes(needle));
  }, [overrides.data, builtIn.data, q, onlyChanged]);

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

      <ul className="divide-y rounded-xl border bg-card">
        {rows.map((r) => (
          <li key={`${lang}-${r.key}-${r.value ?? ""}`} className="grid gap-2 p-4 md:grid-cols-[minmax(0,14rem)_minmax(0,1fr)_auto] md:items-start" data-testid={`text-row-${r.key}`}>
            <div className="min-w-0">
              <code dir="ltr" className="block truncate text-xs text-muted-foreground">
                {r.key}
              </code>
              {r.value !== undefined && <span className="mt-1 inline-block rounded bg-primary px-1.5 text-xs text-primary-foreground">{t("texts.changed")}</span>}
            </div>
            <div className="min-w-0 space-y-1">
              <textarea
                dir={current?.direction ?? "auto"}
                rows={r.def.length > 70 ? 3 : 1}
                defaultValue={r.value ?? r.def}
                aria-label={r.key}
                data-testid={`text-input-${r.key}`}
                onBlur={(e) => void save(r.key, e.target.value, r.value, r.def)}
                className="min-h-12 w-full resize-y rounded-md border border-input bg-background px-3 py-2.5 text-base outline-none focus-visible:ring-2 focus-visible:ring-ring md:min-h-10 md:text-sm"
              />
              {r.value !== undefined && (
                <p className="text-xs text-muted-foreground">
                  {t("texts.default")}: {r.def}
                </p>
              )}
            </div>
            {r.value !== undefined && (
              <Button type="button" variant="ghost" className="h-12 justify-self-start md:h-10" onClick={() => reset(r.key as TextKey)}>
                <RotateCcw aria-hidden="true" />
                {t("texts.reset")}
              </Button>
            )}
          </li>
        ))}
      </ul>
    </div>
  );
}
