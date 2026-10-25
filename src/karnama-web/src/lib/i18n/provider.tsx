"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useState, useSyncExternalStore } from "react";
import { api } from "../api";
import type { Language } from "../types";
import { persianDefault, textsRegistry, type TextKey } from "./registry";

// Persian ships in the main bundle; other built-in languages are fetched only when chosen.
const BUILT_IN_LOADERS: Record<string, () => Promise<Partial<Record<TextKey, string>>>> = {
  en: () => import("./en").then((m) => m.en),
};
const FALLBACK_LANGUAGES: Language[] = [
  { code: "fa", name: "فارسی", direction: "rtl", calendar: "persian", digits: "persian", isDefault: true },
  { code: "en", name: "English", direction: "ltr", calendar: "gregorian", digits: "latin", isDefault: false },
];
const STORAGE_KEY = "karnama.lang";

type Vars = Record<string, string | number>;

type I18nContextValue = {
  lang: string;
  dir: "rtl" | "ltr";
  languages: Language[];
  t: (key: TextKey, vars?: Vars) => string;
  setLang: (code: string) => void;
  formatNumber: (n: number) => string;
  formatDate: (d: Date | string, opts?: Intl.DateTimeFormatOptions) => string;
};

const I18nContext = createContext<I18nContextValue | null>(null);

function readStoredLang() {
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

function localeFor(lang: Language) {
  const calendar = lang.calendar === "persian" ? "persian" : lang.calendar === "hijri" ? "islamic-umalqura" : "gregory";
  const numbering = lang.digits === "persian" ? "arabext" : "latn";
  return `${lang.code}-u-ca-${calendar}-nu-${numbering}`;
}

export function I18nProvider({ children }: { children: React.ReactNode }) {
  const [languages, setLanguages] = useState<Language[]>(FALLBACK_LANGUAGES);
  const [lang, setLangState] = useState<string>("fa");
  const [overrides, setOverrides] = useState<Record<string, string>>({});
  const [builtIn, setBuiltIn] = useState<Partial<Record<TextKey, string>>>({});

  useEffect(() => {
    const stored = readStoredLang();
    // Read after mount (not in the initial state) so server/static HTML and the first client render match.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (stored) setLangState(stored);
    api<Language[]>("/api/v1/i18n/languages")
      .then((list) => list.length && setLanguages(list))
      .catch(() => undefined);
  }, []);

  const [textsEdit, setTextsEdit] = useState(0);
  useEffect(() => {
    const onChange = () => setTextsEdit((n) => n + 1);
    window.addEventListener("karnama:texts-changed", onChange);
    return () => window.removeEventListener("karnama:texts-changed", onChange);
  }, []);

  // Admin-edited texts for the current language (ETag-cached by the browser).
  useEffect(() => {
    let cancelled = false;
    const loader = BUILT_IN_LOADERS[lang];
    (loader ? loader() : Promise.resolve({}))
      .then((d) => !cancelled && setBuiltIn(d))
      .catch(() => undefined);
    api<{ texts: Record<string, string> }>(`/api/v1/i18n/${lang}`)
      .then((b) => !cancelled && setOverrides(b.texts ?? {}))
      .catch(() => !cancelled && setOverrides({}));
    return () => {
      cancelled = true;
    };
  }, [lang, textsEdit]);

  // Re-render when a page registers its module dictionary.
  const textsVersion = useSyncExternalStore(textsRegistry.subscribe, textsRegistry.version, textsRegistry.version);
  const current = languages.find((l) => l.code === lang) ?? FALLBACK_LANGUAGES[0];
  const dir = current.direction;

  useEffect(() => {
    document.documentElement.lang = lang;
    document.documentElement.dir = dir;
  }, [lang, dir]);

  const setLang = useCallback((code: string) => {
    setLangState(code);
    try {
      localStorage.setItem(STORAGE_KEY, code);
    } catch {
      /* storage unavailable */
    }
  }, []);

  const value = useMemo<I18nContextValue>(() => {
    const locale = localeFor(current);
    const numberFmt = new Intl.NumberFormat(locale);
    const t = (key: TextKey, vars?: Vars) => {
      let text = overrides[key] ?? builtIn[key] ?? persianDefault(key) ?? key;
      if (vars) {
        for (const [k, v] of Object.entries(vars)) {
          text = text.replaceAll(`{${k}}`, typeof v === "number" ? numberFmt.format(v) : v);
        }
      }
      return text;
    };
    return {
      lang,
      dir,
      languages,
      t,
      setLang,
      formatNumber: (n) => numberFmt.format(n),
      formatDate: (d, opts) => formatDate(locale, current, typeof d === "string" ? new Date(d) : d, opts),
    };
  // eslint-disable-next-line react-hooks/exhaustive-deps -- textsVersion forces a new t() after registration
  }, [lang, dir, languages, overrides, builtIn, current, setLang, textsVersion]);

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export function useI18n() {
  const ctx = useContext(I18nContext);
  if (!ctx) throw new Error("useI18n must be used inside I18nProvider");
  return ctx;
}

/**
 * Some engines return Persian-calendar dates in an odd order with a Latin comma ("۱۴۰۵ آبان ۳, یکشنبه").
 * For full dates in RTL languages we assemble "یکشنبه ۳ آبان ۱۴۰۵" from parts.
 */
function formatDate(locale: string, lang: Language, date: Date, opts?: Intl.DateTimeFormatOptions) {
  const options = opts ?? { weekday: "long", day: "numeric", month: "long", year: "numeric" };
  const fmt = new Intl.DateTimeFormat(locale, options);
  if (lang.direction !== "rtl" || !options.month || options.month === "numeric" || options.month === "2-digit") return fmt.format(date);
  const part = (type: Intl.DateTimeFormatPartTypes) => fmt.formatToParts(date).find((p) => p.type === type)?.value;
  return [part("weekday"), part("day"), part("month"), part("year")].filter(Boolean).join(" ");
}

/** Turns Persian/Arabic digits into ASCII (for inputs). */
export function toLatinDigits(s: string) {
  return s.replace(/[۰-۹]/g, (d) => String(d.charCodeAt(0) - 0x06f0)).replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 0x0660));
}
