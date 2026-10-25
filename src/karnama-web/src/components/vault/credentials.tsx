"use client";

import { useQuery, useQueryClient } from "@/lib/query";
import { Copy, Eye, EyeOff, KeyRound, Loader2, Pencil, Plus, ShieldCheck, Trash2 } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import { EditorSheet } from "@/components/app/editor-sheet";
import { ErrorText, useErrorMessage } from "@/components/app/error-text";
import { SelectField, TextArea, TextField, Toggle } from "@/components/app/form";
import { Button } from "@/components/ui/button";
import { InputOTP, InputOTPGroup, InputOTPSlot } from "@/components/ui/input-otp";
import { api } from "@/lib/api";
import { faN4 } from "@/lib/i18n/fa-n4";
import { toLatinDigits, useI18n } from "@/lib/i18n/provider";
import { registerTexts } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";

registerTexts(faN4);

export type Credential = {
  id: string;
  clientId: string;
  platformId: string;
  platformName: string;
  handle: string;
  hasSecret: boolean;
  businessSuiteAccess: boolean;
  source: "staff" | "client";
  updatedAt: string;
  lastRevealedAt: string | null;
};

type Secret = { password: string | null; recoveryEmail: string | null; recoveryPhone: string | null; twoFactorNote: string | null; visibleSeconds: number };

type Mode =
  | { kind: "staff"; clientId: string; canManage: boolean; canReveal: boolean }
  | { kind: "portal" };

const listPath = (m: Mode) => (m.kind === "staff" ? `/api/v1/clients/${m.clientId}/credentials` : "/api/v1/portal/credentials");
const itemPath = (m: Mode, id: string) => (m.kind === "staff" ? `/api/v1/credentials/${id}` : `/api/v1/portal/credentials/${id}`);

/**
 * Page logins of one client (PROMPT2 §10.2). Staff see which pages have a stored login and, with credentials.reveal,
 * open one after an SMS code for 30 seconds. The client's portal is write-only: a saved password is never shown again.
 */
type Account = { platformId: string; platformName?: string; handle: string };

export function Credentials({ mode, accounts }: { mode: Mode; accounts?: Account[] }) {
  const { t, formatDate } = useI18n();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const key = ["credentials", mode.kind === "staff" ? mode.clientId : "mine"];
  const list = useQuery({ queryKey: key, queryFn: () => api<Credential[]>(listPath(mode)) });
  const [editing, setEditing] = useState<Credential | "new" | null>(null);
  const [revealing, setRevealing] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null);
  const canWrite = mode.kind === "portal" || mode.canManage;
  const d = (iso: string) => formatDate(iso, { day: "numeric", month: "long", year: "numeric" });

  const remove = async (id: string) => {
    try {
      await api(itemPath(mode, id), { method: "DELETE" });
      setConfirmDelete(null);
      notify.success(t("vault.deleted"));
      await qc.invalidateQueries({ queryKey: key });
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };

  return (
    <section className="space-y-3" data-testid="vault">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h3 className="flex items-center gap-2 text-lg font-bold">
            <KeyRound className="size-5 text-primary" aria-hidden="true" />
            {t("vault.title")}
          </h3>
          <p className="text-sm text-muted-foreground">{mode.kind === "staff" ? t("vault.subtitle") : t("vault.portalSubtitle")}</p>
        </div>
        {canWrite && (
          <Button type="button" variant="outline" className="h-12 md:h-11" onClick={() => setEditing("new")} data-testid="vault-add">
            <Plus aria-hidden="true" />
            {t("vault.add")}
          </Button>
        )}
      </div>

      {list.error ? <ErrorText error={list.error} /> : null}
      {list.data && list.data.length === 0 && <p className="rounded-lg border border-dashed p-4 text-sm text-muted-foreground">{t("vault.empty")}</p>}

      <ul className="space-y-3">
        {(list.data ?? []).map((c) => (
          <li key={c.id} className="rounded-xl border bg-card p-4" data-testid={`vault-item-${c.handle}`}>
            <div className="flex flex-wrap items-start justify-between gap-2">
              <div className="min-w-0">
                <p className="font-semibold">
                  {c.platformName} · <bdi dir="ltr">@{c.handle}</bdi>
                </p>
                <div className="mt-1 flex flex-wrap gap-1.5 text-xs">
                  {c.hasSecret ? <Badge strong>{t("vault.hasSecret")}</Badge> : <Badge>{t("vault.noSecret")}</Badge>}
                  {c.businessSuiteAccess && (
                    <Badge>
                      <ShieldCheck className="size-3.5" aria-hidden="true" />
                      {t("vault.bySuite")}
                    </Badge>
                  )}
                  <Badge>{c.source === "client" ? t("vault.byClient") : t("vault.byStaff")}</Badge>
                </div>
                <p className="mt-1 text-xs text-muted-foreground">
                  {t("vault.updated", { date: d(c.updatedAt) })}
                  {mode.kind === "staff" && c.lastRevealedAt ? ` — ${t("vault.lastSeen", { date: d(c.lastRevealedAt) })}` : ""}
                </p>
              </div>
              <div className="flex flex-wrap gap-1">
                {mode.kind === "staff" && mode.canReveal && c.hasSecret && revealing !== c.id && (
                  <Button type="button" variant="secondary" className="h-11" onClick={() => setRevealing(c.id)} data-testid={`vault-reveal-${c.handle}`}>
                    <Eye aria-hidden="true" />
                    {t("vault.reveal")}
                  </Button>
                )}
                {canWrite && (
                  <Button type="button" variant="ghost" size="icon" className="size-11" aria-label={t("vault.edit")} onClick={() => setEditing(c)} data-testid={`vault-edit-${c.handle}`}>
                    <Pencil aria-hidden="true" />
                  </Button>
                )}
                {canWrite && (
                  <Button type="button" variant="ghost" size="icon" className="size-11 text-destructive" aria-label={t("vault.delete")} onClick={() => setConfirmDelete(c.id)}>
                    <Trash2 aria-hidden="true" />
                  </Button>
                )}
              </div>
            </div>
            {confirmDelete === c.id && (
              <div className="mt-3 flex flex-wrap items-center gap-2 rounded-lg bg-destructive/10 p-3 text-sm">
                <span className="me-auto">{t("vault.deleteConfirm")}</span>
                <Button type="button" variant="destructive" className="h-11" onClick={() => remove(c.id)} data-testid="vault-delete-yes">
                  {t("crud.confirmYes")}
                </Button>
                <Button type="button" variant="ghost" className="h-11" onClick={() => setConfirmDelete(null)}>
                  {t("common.cancel")}
                </Button>
              </div>
            )}
            {revealing === c.id && <Reveal id={c.id} onDone={() => { setRevealing(null); void qc.invalidateQueries({ queryKey: key }); }} />}
          </li>
        ))}
      </ul>

      {editing && (
        <CredentialForm
          mode={mode}
          initial={editing === "new" ? null : editing}
          accounts={accounts}
          onClose={() => setEditing(null)}
          onSaved={async () => {
            setEditing(null);
            notify.success(t("vault.saved"));
            await qc.invalidateQueries({ queryKey: key });
          }}
        />
      )}
    </section>
  );
}

function Badge({ children, strong }: { children: React.ReactNode; strong?: boolean }) {
  return <span className={`inline-flex items-center gap-1 rounded-md px-2 py-0.5 ${strong ? "bg-secondary font-medium text-secondary-foreground" : "border text-muted-foreground"}`}>{children}</span>;
}

/** SMS code → the secret for 30 seconds (also hidden as soon as the tab is left). Nothing is kept after it closes. */
function Reveal({ id, onDone }: { id: string; onDone: () => void }) {
  const { t, formatNumber } = useI18n();
  const [start, setStart] = useState<{ challengeId: string; maskedPhone: string; demoCode: string | null } | null>(null);
  const [code, setCode] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [secret, setSecret] = useState<Secret | null>(null);
  const [left, setLeft] = useState(0);
  const started = useRef(false);

  const hide = useCallback(() => {
    setSecret(null);
    onDone();
  }, [onDone]);

  useEffect(() => {
    if (started.current) return;
    started.current = true;
    api<{ challengeId: string; maskedPhone: string; demoCode: string | null }>(`/api/v1/credentials/${id}/reveal/start`, { method: "POST" })
      .then(setStart)
      .catch(setError);
  }, [id]);

  useEffect(() => {
    if (!secret) return;
    const deadline = Date.now() + secret.visibleSeconds * 1000;
    const timer = window.setInterval(() => {
      const remaining = Math.ceil((deadline - Date.now()) / 1000);
      if (remaining <= 0) hide();
      else setLeft(remaining);
    }, 250);
    const onHidden = () => document.visibilityState === "hidden" && hide();
    document.addEventListener("visibilitychange", onHidden);
    return () => {
      window.clearInterval(timer);
      document.removeEventListener("visibilitychange", onHidden);
    };
  }, [secret, hide]);

  const confirm = async (value: string) => {
    if (!start) return;
    setBusy(true);
    setError(null);
    try {
      const s = await api<Secret>(`/api/v1/credentials/${id}/reveal`, { method: "POST", body: { challengeId: start.challengeId, code: value } });
      setSecret(s);
      setLeft(s.visibleSeconds);
    } catch (e) {
      setError(e);
      setCode("");
    } finally {
      setBusy(false);
    }
  };

  if (secret) {
    const rows: [string, string | null, boolean][] = [
      [t("vault.password"), secret.password, true],
      [t("vault.recoveryEmail"), secret.recoveryEmail, true],
      [t("vault.recoveryPhone"), secret.recoveryPhone, true],
      [t("vault.twoFactorNote"), secret.twoFactorNote, false],
    ];
    return (
      <div className="mt-3 space-y-3 rounded-lg border-2 border-primary/40 bg-primary/5 p-3" data-testid="vault-secret">
        <div className="h-1.5 overflow-hidden rounded-full bg-muted" role="progressbar" aria-valuemin={0} aria-valuemax={secret.visibleSeconds} aria-valuenow={left} aria-label={t("vault.hidesIn", { seconds: formatNumber(left) })}>
          <div className="h-full bg-primary transition-[width] duration-1000 ease-linear" style={{ width: `${(left / secret.visibleSeconds) * 100}%` }} />
        </div>
        <dl className="space-y-2">
          {rows.filter(([, v]) => v).map(([label, value, ltr]) => (
            <div key={label} className="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-2 gap-y-1 sm:grid-cols-[8rem_minmax(0,1fr)_auto]">
              <dt className="col-span-2 text-sm text-muted-foreground sm:col-span-1">{label}</dt>
              <dd className={`min-w-0 break-words rounded-md bg-card px-2 py-1.5 ${ltr ? "font-mono text-sm" : "text-sm"}`} dir={ltr ? "ltr" : undefined} data-testid={`vault-value-${label}`}>
                {value}
              </dd>
              <Button
                type="button"
                variant="ghost"
                size="icon"
                className="size-11"
                aria-label={`${t("vault.copy")} ${label}`}
                onClick={async () => {
                  try {
                    await navigator.clipboard.writeText(value!);
                    notify.success(t("vault.copied"));
                  } catch {
                    /* clipboard blocked: the value is on screen */
                  }
                }}
              >
                <Copy aria-hidden="true" />
              </Button>
            </div>
          ))}
        </dl>
        <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-muted-foreground">
          <span>
            {t("vault.hidesIn", { seconds: formatNumber(left) })} — {t("vault.audited")}
          </span>
          <Button type="button" variant="outline" className="h-11" onClick={hide} data-testid="vault-hide">
            <EyeOff aria-hidden="true" />
            {t("vault.hideNow")}
          </Button>
        </div>
      </div>
    );
  }

  return (
    <form
      className="mt-3 space-y-3 rounded-lg border bg-muted/40 p-3"
      onSubmit={(e) => {
        e.preventDefault();
        if (code.length === 6) void confirm(code);
      }}
      data-testid="vault-otp"
    >
      <p className="font-semibold">{t("vault.revealTitle")}</p>
      {!start && !error && (
        <p className="flex items-center gap-2 text-sm text-muted-foreground">
          <Loader2 className="size-4 animate-spin" aria-hidden="true" />
          {t("vault.revealSending")}
        </p>
      )}
      {start && (
        <>
          <p className="text-sm text-muted-foreground">{t("vault.revealSent", { phone: start.maskedPhone })}</p>
          {start.demoCode && (
            <p className="rounded-md bg-amber-100 px-2 py-1 text-sm text-amber-950 dark:bg-amber-950 dark:text-amber-100" data-testid="vault-demo-code">
              {t("vault.revealDemo", { code: start.demoCode })}
            </p>
          )}
          <div dir="ltr" className="flex justify-center sm:justify-start">
            <InputOTP
              maxLength={6}
              value={code}
              onChange={(v) => {
                const latin = toLatinDigits(v).replace(/\D/g, "");
                setCode(latin);
                if (latin.length === 6) void confirm(latin);
              }}
              inputMode="numeric"
              autoComplete="one-time-code"
              autoFocus
              disabled={busy}
              aria-label={t("vault.revealTitle")}
              data-testid="vault-code"
            >
              <InputOTPGroup>
                {Array.from({ length: 6 }, (_, i) => (
                  <InputOTPSlot key={i} index={i} className="size-11 text-lg" />
                ))}
              </InputOTPGroup>
            </InputOTP>
          </div>
        </>
      )}
      <ErrorText error={error} />
      <div className="flex flex-wrap gap-2">
        <Button type="submit" className="h-11" disabled={!start || busy || code.length !== 6} data-testid="vault-confirm">
          {busy && <Loader2 className="animate-spin" aria-hidden="true" />}
          {t("vault.revealConfirm")}
        </Button>
        <Button type="button" variant="ghost" className="h-11" onClick={onDone}>
          {t("common.cancel")}
        </Button>
      </div>
    </form>
  );
}

function CredentialForm({ mode, initial, accounts, onClose, onSaved }: { mode: Mode; initial: Credential | null; accounts?: Account[]; onClose: () => void; onSaved: () => void }) {
  const { t } = useI18n();
  const platforms = useQuery({ queryKey: ["platforms"], enabled: mode.kind === "staff", queryFn: () => api<{ id: string; name: string; isActive: boolean }[]>("/api/v1/platforms") });
  const firstAccount = accounts?.[0];
  const [platformId, setPlatformId] = useState(initial?.platformId ?? firstAccount?.platformId ?? "");
  const [handle, setHandle] = useState(initial?.handle ?? firstAccount?.handle ?? "");
  const [suite, setSuite] = useState(initial?.businessSuiteAccess ?? false);
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [email, setEmail] = useState("");
  const [phone, setPhone] = useState("");
  const [note, setNote] = useState("");
  const [removeSecret, setRemoveSecret] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  // Staff pick from the platform catalog; the portal only knows the platforms of its own pages.
  const platformOptions =
    mode.kind === "staff"
      ? (platforms.data ?? []).filter((p) => p.isActive || p.id === initial?.platformId).map((p) => ({ value: p.id, label: p.name }))
      : (accounts ?? [])
          .filter((a, i, all) => all.findIndex((b) => b.platformId === a.platformId) === i)
          .map((a) => ({ value: a.platformId, label: a.platformName ?? a.handle }));

  const submit = async () => {
    setBusy(true);
    setError(null);
    // Create: empty fields are simply not stored. Edit: empty = keep the stored value (see the API contract).
    const v = (s: string) => (s === "" ? null : s);
    const anything = [password, email, phone, note].some((s) => s !== "");
    const body = {
      platformId,
      handle,
      businessSuiteAccess: suite,
      secret: anything ? { password: v(password), recoveryEmail: v(email.trim()), recoveryPhone: v(phone.trim()), twoFactorNote: v(note.trim()) } : null,
      removeSecret,
    };
    try {
      if (initial) await api(itemPath(mode, initial.id), { method: "PUT", body });
      else await api(mode.kind === "staff" ? `/api/v1/clients/${mode.clientId}/credentials` : "/api/v1/portal/credentials", { method: "POST", body });
      setPassword("");
      onSaved();
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  return (
    <EditorSheet title={initial ? t("vault.edit") : t("vault.add")} onClose={onClose} onSubmit={submit} busy={busy} error={error} testId="vault-form">
      <aside className="space-y-1 rounded-xl border border-primary/30 bg-primary/5 p-4 text-sm">
        <p className="flex items-center gap-2 font-bold">
          <ShieldCheck className="size-4 text-primary" aria-hidden="true" />
          {t("vault.tipTitle")}
        </p>
        <p className="text-muted-foreground">{t("vault.tipBody")}</p>
      </aside>
      <SelectField label={t("vault.platform")} value={platformId} onChange={setPlatformId} options={platformOptions} testId="vault-platform" />
      <TextField label={t("vault.handle")} help={t("vault.handleHelp")} value={handle} onChange={setHandle} dir="ltr" autoComplete="off" testId="vault-handle" />
      <Toggle label={t("vault.businessSuite")} help={t("vault.businessSuiteHelp")} checked={suite} onChange={setSuite} testId="vault-suite" />
      {!removeSecret && (
        <>
          <div className="relative">
            <TextField
              label={t("vault.password")}
              help={initial?.hasSecret ? t("vault.passwordKeep") : t("vault.passwordHelp")}
              value={password}
              onChange={setPassword}
              type={showPassword ? "text" : "password"}
              dir="ltr"
              autoComplete="new-password"
              spellCheck={false}
              optional
              testId="vault-password"
            />
            <Button type="button" variant="ghost" size="icon" className="absolute end-1 top-8 size-10" aria-label={t(showPassword ? "vault.hidePassword" : "vault.showPassword")} onClick={() => setShowPassword((s) => !s)}>
              {showPassword ? <EyeOff aria-hidden="true" /> : <Eye aria-hidden="true" />}
            </Button>
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <TextField label={t("vault.recoveryEmail")} value={email} onChange={setEmail} dir="ltr" type="email" autoComplete="off" optional testId="vault-email" />
            <TextField label={t("vault.recoveryPhone")} value={phone} onChange={setPhone} dir="ltr" inputMode="tel" autoComplete="off" optional />
          </div>
          <TextArea label={t("vault.twoFactorNote")} help={t("vault.twoFactorNoteHelp")} value={note} onChange={setNote} rows={2} optional />
        </>
      )}
      {initial?.hasSecret && <Toggle label={t("vault.removeSecret")} help={t("vault.removeSecretHelp")} checked={removeSecret} onChange={setRemoveSecret} testId="vault-remove-secret" />}
    </EditorSheet>
  );
}
