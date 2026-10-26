"use client";

import { useQuery } from "@/lib/query";
import { ArrowRight, Clapperboard, Loader2, PenLine, PhoneCall, Shield, Store, type LucideIcon } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { BrandMark, useBrand } from "@/components/app/brand-mark";
import { ErrorText } from "@/components/app/error-text";
import { TilePattern } from "@/components/app/tile-pattern";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { InputOTP, InputOTPGroup, InputOTPSlot } from "@/components/ui/input-otp";
import { Label } from "@/components/ui/label";
import { api, ApiError, type Session } from "@/lib/api";
import { mediaUrl } from "@/lib/config";
import { useAuth } from "@/lib/auth";
import { toLatinDigits, useI18n } from "@/lib/i18n/provider";
import type { DemoAccount } from "@/lib/types";
import { faAuth } from "@/lib/i18n/fa-pages";
import { registerTexts as registerPageTexts } from "@/lib/i18n/registry";

registerPageTexts(faAuth);

type Step =
  | { kind: "phone" }
  | { kind: "code"; challengeId: string; phone: string; resendAt: number }
  | { kind: "totp"; token: string }
  | { kind: "totp-setup"; token: string; secret: string; qr: string };

type VerifyResponse =
  | { step: "completed"; session: Session }
  | { step: "twoFactorRequired" | "twoFactorSetupRequired"; twoFactorToken: string };

const DEMO_ICONS: Record<string, LucideIcon> = { shield: Shield, clapperboard: Clapperboard, "pen-line": PenLine, store: Store, phone: PhoneCall };

export function LoginScreen() {
  const { t } = useI18n();
  const router = useRouter();
  const { status, completeLogin } = useAuth();
  const brand = useBrand();
  const [step, setStep] = useState<Step>({ kind: "phone" });

  useEffect(() => {
    if (status === "authenticated") router.replace("/dashboard");
  }, [status, router]);

  const finish = async (session: Session) => {
    const me = await completeLogin(session);
    if (me) router.replace("/dashboard");
  };

  return (
    <div className="grid min-h-dvh lg:grid-cols-[minmax(0,1fr)_minmax(0,1.1fr)]">
      {/* Form side (start side: right in RTL) */}
      <main className="relative flex flex-col px-4 py-6 sm:px-10 lg:py-10">
        <header className="flex items-center justify-between">
          <BrandMark className="text-lg" />
        </header>

        <div className="mx-auto flex w-full max-w-md flex-1 flex-col justify-center py-10">
          {step.kind === "phone" && <PhoneStep onSent={(s) => setStep(s)} />}
          {step.kind === "code" && (
            <CodeStep
              step={step}
              onBack={() => setStep({ kind: "phone" })}
              onResent={(s) => setStep(s)}
              onResult={async (r) => {
                if (r.step === "completed") return finish(r.session);
                if (r.step === "twoFactorRequired") return setStep({ kind: "totp", token: r.twoFactorToken });
                const setup = await api<{ secret: string; otpAuthUri: string }>("/api/v1/auth/2fa/setup", {
                  method: "POST",
                  body: { twoFactorToken: r.twoFactorToken },
                  noAuthRetry: true,
                });
                const { toDataURL } = await import("qrcode"); // only owners see this step
                const qr = await toDataURL(setup.otpAuthUri, { margin: 1, width: 220 });
                setStep({ kind: "totp-setup", token: r.twoFactorToken, secret: setup.secret, qr });
              }}
            />
          )}
          {(step.kind === "totp" || step.kind === "totp-setup") && <TotpStep step={step} onDone={finish} />}

          {step.kind === "phone" && brand.demo && <DemoLogin onDone={finish} />}
        </div>

        <footer className="text-center text-sm text-muted-foreground">
          <Link href="/setup" className="underline-offset-4 hover:underline">
            {t("login.setupLink")}
          </Link>
        </footer>
      </main>

      {/* Brand side (21st: @diarmuradi/sign-in-3 split layout + @rahil1202/glass-video-hero): the site's own video loop
          and post photos behind the brand name; just the tile lattice when the site has no media yet. */}
      <BrandStage name={brand.name ?? t("brand.defaultName")} tagline={brand.tagline ?? t("brand.tagline")} />
    </div>
  );
}

function PhoneStep({ onSent }: { onSent: (s: Step) => void }) {
  const { t } = useI18n();
  const [phone, setPhone] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    const normalized = toLatinDigits(phone.trim()).replace(/[\s-]/g, "");
    if (!/^(\+98|0098|98|0)?9\d{9}$/.test(normalized)) {
      setError(new ApiError(422, "auth.invalidPhone", t("login.invalidPhone")));
      return;
    }
    setBusy(true);
    try {
      const r = await api<{ challengeId: string; resendAfterSeconds: number }>("/api/v1/auth/otp/request", {
        method: "POST",
        body: { phone: normalized },
        noAuthRetry: true,
      });
      onSent({ kind: "code", challengeId: r.challengeId, phone: normalized, resendAt: Date.now() + r.resendAfterSeconds * 1000 });
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={submit} className="space-y-6" noValidate>
      <h1 className="text-3xl font-extrabold">{t("login.title")}</h1>
      <div className="space-y-2">
        <Label htmlFor="phone">{t("login.phoneLabel")}</Label>
        <Input
          id="phone"
          data-testid="login-phone"
          type="tel"
          inputMode="tel"
          autoComplete="tel"
          dir="ltr"
          className="h-12 text-end text-lg tracking-wider"
          placeholder={t("login.phonePlaceholder")}
          value={phone}
          onChange={(e) => setPhone(e.target.value)}
          aria-invalid={!!error}
          required
        />
        <ErrorText error={error} />
      </div>
      <Button type="submit" data-testid="login-send" className="h-12 w-full text-base" disabled={busy}>
        {busy && <Loader2 className="animate-spin" aria-hidden="true" />}
        {busy ? t("login.sending") : t("login.sendCode")}
      </Button>
    </form>
  );
}

function CodeStep({
  step,
  onBack,
  onResent,
  onResult,
}: {
  step: Extract<Step, { kind: "code" }>;
  onBack: () => void;
  onResent: (s: Step) => void;
  onResult: (r: VerifyResponse) => Promise<unknown>;
}) {
  const { t, formatNumber } = useI18n();
  const [code, setCode] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(id);
  }, []);
  const wait = Math.max(0, Math.ceil((step.resendAt - now) / 1000));

  const verify = async (value: string) => {
    setBusy(true);
    setError(null);
    try {
      const r = await api<VerifyResponse>("/api/v1/auth/otp/verify", {
        method: "POST",
        body: { challengeId: step.challengeId, code: value },
        noAuthRetry: true,
      });
      await onResult(r);
    } catch (err) {
      setError(err);
      setCode("");
    } finally {
      setBusy(false);
    }
  };

  const resend = async () => {
    setError(null);
    try {
      const r = await api<{ challengeId: string; resendAfterSeconds: number }>("/api/v1/auth/otp/request", {
        method: "POST",
        body: { phone: step.phone },
        noAuthRetry: true,
      });
      onResent({ ...step, challengeId: r.challengeId, resendAt: Date.now() + r.resendAfterSeconds * 1000 });
    } catch (err) {
      setError(err);
    }
  };

  const shownPhone = step.phone.replace(/\d/g, (d) => formatNumber(Number(d)));

  return (
    <form
      className="space-y-6"
      onSubmit={(e) => {
        e.preventDefault();
        if (code.length === 6) void verify(code);
      }}
    >
      <div>
        <h1 className="text-3xl font-extrabold">{t("login.codeTitle")}</h1>
        <p className="mt-2 text-muted-foreground">{t("login.codeSentTo", { phone: shownPhone })}</p>
      </div>
      <div dir="ltr" className="flex justify-center">
        <InputOTP
          maxLength={6}
          value={code}
          onChange={(v) => {
            const latin = toLatinDigits(v).replace(/\D/g, "");
            setCode(latin);
            if (latin.length === 6) void verify(latin);
          }}
          inputMode="numeric"
          autoFocus
          disabled={busy}
          data-testid="login-otp"
          aria-label={t("login.codeTitle")}
        >
          <InputOTPGroup>
            {Array.from({ length: 6 }, (_, i) => (
              <InputOTPSlot key={i} index={i} className="size-12 text-lg" />
            ))}
          </InputOTPGroup>
        </InputOTP>
      </div>
      <ErrorText error={error} />
      <Button type="submit" data-testid="login-verify" className="h-12 w-full text-base" disabled={busy || code.length !== 6}>
        {busy && <Loader2 className="animate-spin" aria-hidden="true" />}
        {busy ? t("login.verifying") : t("login.verify")}
      </Button>
      <div className="flex items-center justify-between text-sm">
        <Button type="button" variant="ghost" className="h-12" onClick={onBack}>
          <ArrowRight className="rtl:rotate-0 ltr:rotate-180" aria-hidden="true" />
          {t("login.changePhone")}
        </Button>
        {wait > 0 ? (
          <span className="text-muted-foreground" aria-live="polite">
            {t("login.resendIn", { seconds: wait })}
          </span>
        ) : (
          <Button type="button" variant="link" className="h-12" onClick={resend}>
            {t("login.resend")}
          </Button>
        )}
      </div>
    </form>
  );
}

function TotpStep({ step, onDone }: { step: Extract<Step, { kind: "totp" | "totp-setup" }>; onDone: (s: Session) => Promise<void> }) {
  const { t } = useI18n();
  const [code, setCode] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const verify = async (value: string) => {
    setBusy(true);
    setError(null);
    try {
      const r = await api<VerifyResponse>("/api/v1/auth/2fa/verify", {
        method: "POST",
        body: { twoFactorToken: step.token, code: value },
        noAuthRetry: true,
      });
      if (r.step === "completed") await onDone(r.session);
    } catch (err) {
      setError(err);
      setCode("");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-extrabold">{step.kind === "totp" ? t("login.totpTitle") : t("login.totpSetupTitle")}</h1>
        <p className="mt-2 text-muted-foreground">{step.kind === "totp" ? t("login.totpHint") : t("login.totpSetupHint")}</p>
      </div>
      {step.kind === "totp-setup" && (
        <div className="flex flex-col items-center gap-3 rounded-lg border bg-card p-4">
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img src={step.qr} alt="QR" width={220} height={220} className="rounded-md bg-white p-2" />
          <p className="text-sm text-muted-foreground">{t("login.totpManual")}</p>
          <code dir="ltr" className="rounded bg-muted px-2 py-1 text-sm break-all select-all">
            {step.secret}
          </code>
        </div>
      )}
      <div dir="ltr" className="flex justify-center">
        <InputOTP
          maxLength={6}
          value={code}
          onChange={(v) => {
            const latin = toLatinDigits(v).replace(/\D/g, "");
            setCode(latin);
            if (latin.length === 6) void verify(latin);
          }}
          inputMode="numeric"
          autoFocus
          disabled={busy}
          aria-label={t("login.totpTitle")}
        >
          <InputOTPGroup>
            {Array.from({ length: 6 }, (_, i) => (
              <InputOTPSlot key={i} index={i} className="size-12 text-lg" />
            ))}
          </InputOTPGroup>
        </InputOTP>
      </div>
      <ErrorText error={error} />
      <Button className="h-12 w-full text-base" disabled={busy || code.length !== 6} onClick={() => verify(code)}>
        {busy && <Loader2 className="animate-spin" aria-hidden="true" />}
        {t("login.totpConfirm")}
      </Button>
    </div>
  );
}

function DemoLogin({ onDone }: { onDone: (s: Session) => Promise<void> }) {
  const { t } = useI18n();
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<unknown>(null);
  const { data } = useQuery({
    queryKey: ["demo-options"],
    queryFn: () =>
      api<DemoAccount[]>("/api/v1/auth/demo/options", { noAuthRetry: true }).catch((e) => {
        if (e instanceof ApiError && e.status === 404) return [];
        throw e;
      }),
    staleTime: 60_000,
  });
  if (!data?.length) return null;

  const login = async (role: string) => {
    setBusy(role);
    setError(null);
    try {
      await onDone(await api<Session>("/api/v1/auth/demo", { method: "POST", body: { role }, noAuthRetry: true }));
    } catch (err) {
      setError(err);
    } finally {
      setBusy(null);
    }
  };

  return (
    <section className="mt-10 border-t pt-6" aria-labelledby="demo-title">
      <h2 id="demo-title" className="font-bold">
        {t("login.demoTitle")}
      </h2>
      <p className="mt-1 text-sm text-muted-foreground">{t("login.demoHint")}</p>
      <div className="mt-4 grid grid-cols-1 gap-2 sm:grid-cols-2">
        {data.map((a) => {
          const Icon = DEMO_ICONS[a.icon] ?? Shield;
          return (
            <Button
              key={a.key}
              variant="outline"
              data-testid={`demo-${a.key}`}
              className="h-auto min-h-14 justify-start gap-3 px-3 py-2 text-start whitespace-normal"
              disabled={busy !== null}
              onClick={() => login(a.key)}
            >
              {busy === a.key ? <Loader2 className="size-5 animate-spin" aria-hidden="true" /> : <Icon className="size-5 text-emphasis" aria-hidden="true" />}
              <span className="flex flex-col">
                <span className="font-bold">{a.name}</span>
                <span className="text-xs font-normal text-muted-foreground">{a.description}</span>
              </span>
            </Button>
          );
        })}
      </div>
      <div className="mt-2">
        <ErrorText error={error} />
      </div>
    </section>
  );
}

type PublicHero = { hero?: { videoUrl: string | null; posterUrl: string | null; images: string[] } };

function BrandStage({ name, tagline }: { name: string; tagline: string }) {
  // Media is decoration: only on wide screens, and only after the page has finished loading (phones on 3G never pay for it).
  const [wide, setWide] = useState(false);
  useEffect(() => {
    if (!matchMedia("(min-width: 1024px)").matches) return;
    const go = () => setTimeout(() => setWide(true), 300);
    if (document.readyState === "complete") go();
    else window.addEventListener("load", go, { once: true });
    return () => window.removeEventListener("load", go);
  }, []);
  const { data } = useQuery({
    queryKey: ["public-hero"],
    enabled: wide,
    queryFn: () => api<PublicHero>("/api/v1/public/site/home", { noAuthRetry: true }).catch(() => ({}) as PublicHero),
    staleTime: 5 * 60_000,
  });
  const hero = data?.hero;
  const photos = (hero?.images ?? []).slice(0, 3);
  return (
    <aside className="tile-on-panel relative isolate order-first h-36 overflow-hidden bg-[var(--brand-black)] lg:order-none lg:h-auto" aria-hidden="true">
      {hero?.videoUrl ? (
        <video src={mediaUrl(hero.videoUrl)} poster={hero.posterUrl ? mediaUrl(hero.posterUrl) : undefined} autoPlay muted loop playsInline preload="none" className="absolute inset-0 -z-20 size-full object-cover opacity-70 motion-reduce:hidden" />
      ) : hero?.posterUrl ? (
        // eslint-disable-next-line @next/next/no-img-element -- uploaded still
        <img src={mediaUrl(hero.posterUrl)} alt="" className="absolute inset-0 -z-20 size-full object-cover opacity-70" />
      ) : null}
      <div className="absolute inset-0 -z-10 bg-gradient-to-t from-[var(--brand-black)] via-[var(--brand-black)]/40 to-[var(--brand-black)]/60" />
      <TilePattern className="absolute inset-0 -z-10 opacity-60" id="login-tile" />
      {photos.length > 0 && (
        <div className="absolute inset-x-0 top-[18%] hidden justify-center lg:flex">
          {photos.map((src, i) => (
            // eslint-disable-next-line @next/next/no-img-element -- uploaded post photo
            <img
              key={src}
              src={mediaUrl(src)}
              alt=""
              width={208}
              height={277}
              className="kn-login-card -mx-6 aspect-[3/4] w-44 rounded-2xl object-cover shadow-2xl ring-1 ring-white/20 xl:w-52"
              style={{ ["--x" as string]: i - 1, zIndex: i === 1 ? 2 : 1, animationDelay: `${300 + i * 140}ms` }}
            />
          ))}
        </div>
      )}
      <div className="relative hidden h-full flex-col justify-end p-12 lg:flex">
        <p className="max-w-md text-6xl leading-tight font-black text-[var(--brand-on-dark)]">{name}</p>
        <p className="mt-4 max-w-sm text-lg text-white/80">{tagline}</p>
      </div>
    </aside>
  );
}
