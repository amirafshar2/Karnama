"use client";

import { ExternalLink, Plus, Trash2 } from "lucide-react";
import { useState } from "react";
import { ErrorText } from "@/components/app/error-text";
import { TextArea, TextField, Toggle } from "@/components/app/form";
import { useRequire } from "@/components/app/use-require";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { faSiteAdmin } from "@/lib/i18n/fa-siteadmin";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { useQuery } from "@/lib/query";
import { cn } from "@/lib/utils";
import type { HomeContent } from "@/components/siteadmin/types";

registerTexts(faSiteAdmin);

export default function HomeContentPage() {
  const allowed = useRequire([{ code: "content.manage", scope: "all" }]);
  const q = useQuery({ queryKey: ["site-home"], enabled: allowed, queryFn: () => api<HomeContent>("/api/v1/site/home"), staleTime: Infinity });
  if (!allowed) return null;
  if (!q.data) return <div className="h-96 animate-pulse rounded-xl bg-muted" />;
  return <Editor initial={q.data} />;
}

function Counter({ n, min, max }: { n: number; min?: number; max: number }) {
  const { t, formatNumber } = useI18n();
  const bad = n > max || (min !== undefined && n < min);
  return <span className={cn("text-xs", bad ? "font-bold text-destructive" : "text-muted-foreground")}>{t("home.chars", { n: formatNumber(n), max: formatNumber(max) })}</span>;
}

function Section({ title, help, children }: { title: string; help?: string; children: React.ReactNode }) {
  return (
    <section className="space-y-4 rounded-xl border bg-card p-5">
      <div>
        <h2 className="text-lg font-bold">{title}</h2>
        {help && <p className="text-sm text-muted-foreground">{help}</p>}
      </div>
      {children}
    </section>
  );
}

function Editor({ initial }: { initial: HomeContent }) {
  const { t } = useI18n();
  const [c, setC] = useState(initial);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const L = (k: TextKey) => t(k);
  const set = (patch: Partial<HomeContent>) => setC({ ...c, ...patch });
  const setContact = (patch: Partial<HomeContent["contact"]>) => setC({ ...c, contact: { ...c.contact, ...patch } });
  const nz = (s: string) => (s.trim() === "" ? null : s);

  const save = async () => {
    setBusy(true);
    setError(null);
    try {
      setC(await api<HomeContent>("/api/v1/site/home", { method: "PUT", body: c }));
      notify.success(t("home.saved"));
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  return (
    <form
      className="max-w-3xl space-y-6"
      noValidate
      onSubmit={(e) => {
        e.preventDefault();
        void save();
      }}
      data-testid="home-editor"
    >
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-extrabold">{t("home.title")}</h1>
          <p className="mt-1 text-sm text-muted-foreground">{t("home.subtitle")}</p>
        </div>
        <Button asChild variant="outline" className="h-11">
          <a href="/" target="_blank" rel="noopener noreferrer">
            <ExternalLink aria-hidden="true" />
            {t("home.view")}
          </a>
        </Button>
      </header>

      <Section title={t("home.seo")} help={t("home.seoHelp")}>
        <div className="rounded-lg border bg-background p-4" aria-hidden="true">
          <p className="text-lg leading-7 text-sky-800 dark:text-sky-300">{c.seoTitle || "—"}</p>
          <p className="mt-1 text-sm leading-6 text-muted-foreground">{c.seoDescription || "—"}</p>
        </div>
        <div className="space-y-1">
          <TextField label={L("home.seoTitle")} help={L("home.seoTitleHelp")} value={c.seoTitle} onChange={(v) => set({ seoTitle: v })} testId="home-seo-title" />
          <Counter n={c.seoTitle.length} min={1} max={60} />
        </div>
        <div className="space-y-1">
          <TextArea label={L("home.seoDescription")} help={L("home.seoDescriptionHelp")} value={c.seoDescription} onChange={(v) => set({ seoDescription: v })} rows={3} testId="home-seo-desc" />
          <Counter n={c.seoDescription.length} min={50} max={160} />
        </div>
      </Section>

      <Section title={t("home.hero")}>
        <TextField label={L("home.heroTitle")} help={L("home.heroTitleHelp")} value={c.heroTitle} onChange={(v) => set({ heroTitle: v })} testId="home-hero-title" />
        <TextArea label={L("home.heroSubtitle")} help={L("home.heroSubtitleHelp")} value={c.heroSubtitle} onChange={(v) => set({ heroSubtitle: v })} rows={3} optional />
        <TextField label={L("home.ctaLabel")} help={L("home.ctaLabelHelp")} value={c.ctaLabel} onChange={(v) => set({ ctaLabel: v })} />
      </Section>

      <Section title={t("home.show")}>
        <Toggle label={L("home.showPackages")} help={L("home.showPackagesHelp")} checked={c.showPackages} onChange={(v) => set({ showPackages: v })} />
        <Toggle label={L("home.showCases")} help={L("home.showCasesHelp")} checked={c.showCases} onChange={(v) => set({ showCases: v })} />
        <Toggle label={L("home.showBlog")} help={L("home.showBlogHelp")} checked={c.showBlog} onChange={(v) => set({ showBlog: v })} />
      </Section>

      <Section title={t("home.sections")}>
        <div className="grid gap-4 sm:grid-cols-2">
          {(["servicesTitle", "casesTitle", "packagesTitle", "processTitle", "testimonialsTitle", "blogTitle", "faqTitle"] as const).map((k) => (
            <TextField key={k} label={L(`home.${k}` as TextKey)} help={L("home.sectionTitleHelp")} value={c[k]} onChange={(v) => set({ [k]: v } as Partial<HomeContent>)} />
          ))}
        </div>
      </Section>

      <Section title={t("home.process")}>
        <ol className="space-y-4">
          {c.process.map((s, i) => (
            <li key={i} className="space-y-2 rounded-lg border p-3">
              <div className="flex items-center justify-between">
                <span className="text-sm font-bold">{t("home.step")} {i + 1}</span>
                <Button type="button" variant="ghost" size="sm" onClick={() => set({ process: c.process.filter((_, j) => j !== i) })} aria-label={t("home.remove")}>
                  <Trash2 className="size-4" aria-hidden="true" />
                </Button>
              </div>
              <TextField label={L("home.stepTitle")} help={L("home.stepTitleHelp")} value={s.title} onChange={(v) => set({ process: c.process.map((x, j) => (j === i ? { ...x, title: v } : x)) })} />
              <TextArea label={L("home.stepBody")} help={L("home.stepBodyHelp")} value={s.body} onChange={(v) => set({ process: c.process.map((x, j) => (j === i ? { ...x, body: v } : x)) })} rows={2} />
            </li>
          ))}
        </ol>
        {c.process.length < 8 && (
          <Button type="button" variant="outline" className="h-11" onClick={() => set({ process: [...c.process, { title: "", body: "" }] })}>
            <Plus aria-hidden="true" />
            {t("home.addStep")}
          </Button>
        )}
      </Section>

      <Section title={t("home.faq")} help={t("home.faqHelp")}>
        <ul className="space-y-4">
          {c.faq.map((f, i) => (
            <li key={i} className="space-y-2 rounded-lg border p-3">
              <div className="flex justify-end">
                <Button type="button" variant="ghost" size="sm" onClick={() => set({ faq: c.faq.filter((_, j) => j !== i) })} aria-label={t("home.remove")}>
                  <Trash2 className="size-4" aria-hidden="true" />
                </Button>
              </div>
              <TextField label={L("home.question")} help={L("home.questionHelp")} value={f.question} onChange={(v) => set({ faq: c.faq.map((x, j) => (j === i ? { ...x, question: v } : x)) })} />
              <TextArea label={L("home.answer")} help={L("home.answerHelp")} value={f.answer} onChange={(v) => set({ faq: c.faq.map((x, j) => (j === i ? { ...x, answer: v } : x)) })} rows={3} />
            </li>
          ))}
        </ul>
        {c.faq.length < 20 && (
          <Button type="button" variant="outline" className="h-11" onClick={() => set({ faq: [...c.faq, { question: "", answer: "" }] })}>
            <Plus aria-hidden="true" />
            {t("home.addFaq")}
          </Button>
        )}
      </Section>

      <Section title={t("home.consult")}>
        <TextField label={L("home.consultTitle")} help={L("home.consultTitleHelp")} value={c.consultTitle} onChange={(v) => set({ consultTitle: v })} />
        <TextArea label={L("home.consultText")} help={L("home.consultTextHelp")} value={c.consultText} onChange={(v) => set({ consultText: v })} rows={2} />
      </Section>

      <Section title={t("home.contact")} help={t("home.mapHelp")}>
        <div className="grid gap-4 sm:grid-cols-2">
          <TextField label={L("home.phone")} help={L("home.phoneHelp")} dir="ltr" value={c.contact.phone ?? ""} onChange={(v) => setContact({ phone: nz(v) })} optional testId="home-phone" />
          <TextField label={L("home.city")} help={L("home.cityHelp")} value={c.contact.city ?? ""} onChange={(v) => setContact({ city: nz(v) })} optional />
          <TextField label={L("home.address")} help={L("home.addressHelp")} value={c.contact.address ?? ""} onChange={(v) => setContact({ address: nz(v) })} optional />
          <TextField label={L("home.hours")} help={L("home.hoursHelp")} value={c.contact.hours ?? ""} onChange={(v) => setContact({ hours: nz(v) })} optional />
          <TextField label={L("home.email")} help={L("home.emailHelp")} dir="ltr" value={c.contact.email ?? ""} onChange={(v) => setContact({ email: nz(v) })} optional />
          <TextField label={L("home.instagram")} help={L("home.instagramHelp")} dir="ltr" value={c.contact.instagram ?? ""} onChange={(v) => setContact({ instagram: nz(v) })} optional />
          <TextField label={L("home.telegram")} help={L("home.telegramHelp")} dir="ltr" value={c.contact.telegram ?? ""} onChange={(v) => setContact({ telegram: nz(v) })} optional />
          <TextField label={L("home.neshan")} help={L("home.mapHelp")} dir="ltr" value={c.contact.neshanUrl ?? ""} onChange={(v) => setContact({ neshanUrl: nz(v) })} optional placeholder="https://nshn.ir/..." />
          <TextField label={L("home.balad")} help={L("home.mapHelp")} dir="ltr" value={c.contact.baladUrl ?? ""} onChange={(v) => setContact({ baladUrl: nz(v) })} optional placeholder="https://balad.ir/..." />
        </div>
      </Section>

      <div className="sticky bottom-20 z-10 rounded-xl border bg-background/95 p-3 shadow-sm backdrop-blur md:bottom-4">
        <div className="flex items-center gap-3">
          <Button type="submit" className="h-12 flex-1 sm:flex-none sm:px-10" disabled={busy} data-testid="home-save">
            {t("common.save")}
          </Button>
          <div className="min-w-0 flex-1"><ErrorText error={error} /></div>
        </div>
      </div>
    </form>
  );
}
