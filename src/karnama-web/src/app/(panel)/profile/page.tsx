"use client";

import { LogOut } from "lucide-react";
import { useTheme, type ThemeChoice } from "@/lib/theme";
import { useState } from "react";
import { ErrorText } from "@/components/app/error-text";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n/provider";
import type { Me } from "@/lib/types";
import { faProfile } from "@/lib/i18n/fa-pages";
import { registerTexts as registerPageTexts } from "@/lib/i18n/registry";
import dynamic from "next/dynamic";

const NotifyPrefs = dynamic(() => import("@/components/notify/notify-prefs").then((m) => m.NotifyPrefs), { ssr: false });

registerPageTexts(faProfile);

export default function ProfilePage() {
  const { me, reloadMe, logout } = useAuth();
  const { t, languages, lang, setLang } = useI18n();
  const { theme, setTheme } = useTheme();
  const [error, setError] = useState<unknown>(null);
  if (!me) return null;

  const changeLanguage = async (code: string) => {
    setError(null);
    setLang(code);
    try {
      await api<Me>("/api/v1/me", { method: "PATCH", body: { languageCode: code } });
      await reloadMe();
    } catch (err) {
      setError(err);
    }
  };

  return (
    <div className="max-w-xl space-y-8">
      <h1 className="text-2xl font-extrabold">{t("profile.title")}</h1>

      <section className="rounded-xl border bg-card p-5">
        <p className="text-lg font-bold">{me.displayName}</p>
        <dl className="mt-3 grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 text-sm">
          <dt className="text-muted-foreground">{t("profile.phone")}</dt>
          <dd dir="ltr" className="text-end">{me.phoneMasked}</dd>
          <dt className="text-muted-foreground">{t("profile.twoFactor")}</dt>
          <dd>{me.twoFactorEnabled ? t("profile.twoFactorOn") : t("profile.twoFactorOff")}</dd>
        </dl>
      </section>

      <fieldset className="space-y-3">
        <legend className="font-bold">{t("profile.language")}</legend>
        <div className="grid gap-2 sm:grid-cols-2">
          {languages.map((l) => (
            <Choice key={l.code} name="language" value={l.code} checked={lang === l.code} onChange={changeLanguage} testId={`lang-${l.code}`}>
              {l.name}
            </Choice>
          ))}
        </div>
        <ErrorText error={error} />
      </fieldset>

      {(
        <fieldset className="space-y-3">
          <legend className="font-bold">{t("profile.theme")}</legend>
          <div className="grid gap-2 sm:grid-cols-3">
            {(["light", "dark", "system"] as const).map((v) => (
              <Choice key={v} name="theme" value={v} checked={theme === v} onChange={(v) => setTheme(v as ThemeChoice)} testId={`theme-${v}`}>
                {t(v === "light" ? "profile.themeLight" : v === "dark" ? "profile.themeDark" : "profile.themeSystem")}
              </Choice>
            ))}
          </div>
        </fieldset>
      )}

      <NotifyPrefs />

      <Button variant="outline" className="h-12 w-full sm:w-auto" onClick={() => void logout()} data-testid="logout">
        <LogOut className="rtl:rotate-180" aria-hidden="true" />
        {t("nav.logout")}
      </Button>
    </div>
  );
}

/** Native radio as a large tappable card (no extra JS). */
function Choice({
  name,
  value,
  checked,
  onChange,
  testId,
  children,
}: {
  name: string;
  value: string;
  checked: boolean;
  onChange: (v: string) => void;
  testId?: string;
  children: React.ReactNode;
}) {
  return (
    <label className="flex min-h-12 cursor-pointer items-center gap-3 rounded-lg border bg-card px-4 has-checked:border-foreground has-checked:font-bold has-focus-visible:ring-2 has-focus-visible:ring-ring">
      <input type="radio" name={name} value={value} checked={checked} onChange={() => onChange(value)} className="size-4 accent-[var(--foreground)]" data-testid={testId} />
      {children}
    </label>
  );
}
